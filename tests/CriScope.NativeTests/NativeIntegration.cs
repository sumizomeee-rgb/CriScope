using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CriScope.Core;

internal static class NativeIntegration
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static byte[] Frame(ushort function, byte[]? payload = null)
    {
        payload ??= [];
        var bytes = new byte[32 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 31);
        bytes[6] = 8;
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), 1000000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16), function);
        payload.CopyTo(bytes, 32);
        return bytes;
    }
    private static async Task<ushort> ReadCommand(NetworkStream stream, CancellationToken ct)
    {
        byte[] prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, ct);
        int length = NativeProtocol.ReadFrameLength(prefix);
        byte[] body = new byte[length - 4]; await stream.ReadExactlyAsync(body, ct);
        return BinaryPrimitives.ReadUInt16BigEndian(body);
    }
    public static async Task Run()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var events = new ConcurrentQueue<WireEvent>();
            var metricArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var connection = new NativeConnection("127.0.0.1", port, "tcp-fixture", e =>
            { events.Enqueue(e); if (e.kind == "metric") metricArrived.TrySetResult(); }, _ => { });
            var run = connection.Run(ct);
            using var peer = await listener.AcceptTcpClientAsync(ct);
            var stream = peer.GetStream();
            Check(await ReadCommand(stream, ct) == 111 && await ReadCommand(stream, ct) == 22, "Handshake order differs from official client");
            await connection.Connected.WaitAsync(ct);
            // An undecodable, but correctly framed packet must not desynchronize the next one.
            await stream.WriteAsync(Frame(2138, [0xff, 0xff]), ct);
            byte[] valid = Frame(2138, [0x00, 0x89, 0, 0, 0, 3]);
            // Deliberately split both length/header and scalar across independent writes.
            for (int offset = 0; offset < valid.Length; offset += 3)
                await stream.WriteAsync(valid.AsMemory(offset, Math.Min(3, valid.Length - offset)), ct);
            await metricArrived.Task.WaitAsync(ct);
            Check(events.Any(e => e.kind == "error" && e.detail.Contains("Unknown native parameter")), "Unknown parameter not visible");
            Check(events.Any(e => e.kind == "metric" && e.value == 3), "Frame following unknown packet lost");
            connection.Dispose();
            Check(await ReadCommand(stream, ct) == 24, "Cancellation did not send STOP_LOG_RECORD");
            await run.WaitAsync(ct);
            Check(run.IsCompletedSuccessfully, "Cancellation leaked a faulted run task");

            using var disconnected = new NativeConnection("127.0.0.1", port, "disconnect-fixture", _ => { }, _ => { });
            var disconnectedRun = disconnected.Run(ct);
            using (var peer2 = await listener.AcceptTcpClientAsync(ct))
            {
                await ReadCommand(peer2.GetStream(), ct); await ReadCommand(peer2.GetStream(), ct);
                await disconnected.Connected.WaitAsync(ct);
            }
            await disconnectedRun.WaitAsync(ct);
            Check(disconnectedRun.IsCompletedSuccessfully, "Remote EOF escaped as a faulted task");
        }
        finally { listener.Stop(); }

        NativePacket P(string f, ulong t, params NativeParameter[] p) => new(31, 8, 0, t, 0, f, 0, p);
        NativeParameter V(string name, object value) => new(0, name, value);
        var mapper = new NativeEventMapper("segments");
        _ = mapper.Map(P("StartLogging", 1000000)).ToArray();
        _ = mapper.Map(P("ExPlaybackId", 1100000, V("ExPlaybackId_unique64", 7UL), V("cue_name", "Old cue"), V("CriAtomExPlayerHn", "0x1"))).ToArray();
        var voiceA = mapper.Map(P("SoundVoice_Allocate", 1200000, V("ExPlaybackId_unique64", 7UL), V("CriAtomSoundVoiceId_unique64", 8UL))).Single();
        _ = mapper.Map(P("StartLogging", 2000000)).ToArray();
        var voiceB = mapper.Map(P("SoundVoice_Allocate", 2100000, V("ExPlaybackId_unique64", 7UL), V("CriAtomSoundVoiceId_unique64", 8UL))).Single();
        Check(voiceA.name == "Old cue" && voiceB.name == "Voice", "New capture segment reused old cue association");
        Check(voiceA.objectId != voiceB.objectId && voiceA.parentId != voiceB.parentId, "Capture segments reused timeline identities");
        var orientation = P("Ex3dListener_SetOrientation", 2200000, V("CriAtomEx3dListenerHn", "0x2"), V("3dPosVector_Forward", new float[] { 0, 0, 1 }));
        Check(mapper.Map(orientation).Any(e => e.name.Contains("朝向") && e.raw.Contains("3dPosVector_Forward")), "Orientation evidence omitted");
        Check(!mapper.Map(orientation with { TimeMicroseconds = 2300000 }).Any(), "Unchanged orientation repeated");
        var changed = orientation with { TimeMicroseconds = 2400000, Parameters = new[] { V("CriAtomEx3dListenerHn", "0x2"), V("3dPosVector_Forward", new float[] { 1, 0, 0 }) } };
        Check(mapper.Map(changed).Any(e => e.name.Contains("朝向")), "Changed orientation suppressed forever");
        var bus = mapper.Map(P("AsrBusAnalyzeInfoAllChWithRackId", 2500000,
            V("RackId",0),V("BusNo",0),V("BusName","MasterOut"),V("NumCh",2),
            V("PeakLevel",1f),V("RmsLevel",.5f),V("PeakHoldLevel",1f),
            V("PeakLevel",.25f),V("RmsLevel",.125f),V("PeakHoldLevel",.3f))).Single();
        using(var busRaw=System.Text.Json.JsonDocument.Parse(bus.raw))
        {
            var channels=busRaw.RootElement.GetProperty("channels");
            Check(channels.GetArrayLength()==2 && channels[1].GetProperty("peak").GetDouble()==.25 && bus.value==1,
                "Bus scalar/channel decoding changed or assumed fixed channel count");
        }
        using var live = new Session(new WireEvent {kind="hello",session="carry"});
        live.Accept(new WireEvent {kind="play",session="carry",seq=1,time=1,objectId="voice:1",name="Long BGM"});
        live.Accept(new WireEvent {kind="aisac",session="carry",seq=2,time=2,objectId="player:1",name="Intensity",value=.4});
        live.Accept(new WireEvent {kind="log",session="carry",seq=3,time=123});
        Check(live.Snapshot().Length==1,"State carry polluted original event window");
        Check(live.ViewSnapshot().Any(e=>e.kind=="play"&&e.time==1&&e.seq==1),"Long BGM vanished after window eviction");
        Check(live.ViewSnapshot().Any(e=>e.kind=="aisac"&&e.value==.4),"Last-known AISAC vanished");
        live.Accept(new WireEvent {kind="stop",session="carry",seq=4,time=124,objectId="voice:1"});
        Check(live.ViewSnapshot().Any(e=>e.kind=="play"),"Stopped voice lost its start while stop remains visible");
        live.Accept(new WireEvent {kind="log",session="carry",seq=5,time=245});
        Check(!live.ViewSnapshot().Any(e=>e.kind=="play"),"Completed voice retained forever");
        Check(live.Total==5&&live.Watermark==5,"State carry forged sequence or total");
        for(int i=0;i<Session.MaxViewStates+1;i++) live.Accept(new WireEvent {kind="aisac",session="carry",seq=6+i,time=246,objectId="player:"+i,name="Control"});
        live.Accept(new WireEvent {kind="log",session="carry",seq=Session.MaxViewStates+8,time=400});
        Check(live.ViewSnapshot().Length<=Session.MaxViewStates+1&&live.ViewStatesEvicted>0,"View-state memory is unbounded");
        Console.WriteLine("PASS TCP fragmented reads, unknown-packet recovery, cancellation STOP, remote EOF, capture-segment isolation, orientation changes");
        Console.WriteLine("PASS bounded view-state carry preserves long voices and last-known controls without forging raw-window events");
    }
}
