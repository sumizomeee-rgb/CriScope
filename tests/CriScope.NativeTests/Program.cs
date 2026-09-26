using System.Buffers.Binary;
using System.Text.Json;
using CriScope.Core;

static void Check(bool result, string message) { if (!result) throw new Exception(message); }
static void Reject(Action action, string message)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception(message);
}
var start = NativeProtocol.Command(22);
Check(Convert.ToHexString(start) == "00000028001608000000000000000000000000020000000000000000000000000081FFFFFFFF0000", "Official START command differs");
Reject(() => NativeProtocol.Decode(start[..30]), "Truncated frame accepted");
var wrongWidth = (byte[])start.Clone(); wrongWidth[6] = 16;
Reject(() => NativeProtocol.Decode(wrongWidth), "Unsupported pointer accepted");
var huge = new byte[] { 0x7f, 0xff, 0xff, 0xff };
Reject(() => NativeProtocol.ReadFrameLength(huge), "Unbounded allocation accepted");
var unknownParameter = (byte[])start.Clone();
BinaryPrimitives.WriteUInt16BigEndian(unknownParameter.AsSpan(4), 31);
BinaryPrimitives.WriteUInt16BigEndian(unknownParameter.AsSpan(32), 65535);
Reject(() => NativeProtocol.Decode(unknownParameter), "Unknown parameter silently interpreted");
var shortValue = (byte[])start.Clone();
BinaryPrimitives.WriteUInt16BigEndian(shortValue.AsSpan(4), 31);
BinaryPrimitives.WriteUInt16BigEndian(shortValue.AsSpan(18), 5);
Reject(() => NativeProtocol.Decode(shortValue), "Parameter overrun accepted");

// Synthetic parameters mirror the observed native lifecycle; no game assets are fixtures.
var lifecycleMapper=new NativeEventMapper("lifecycle");
NativePacket P(string name,ulong time,params NativeParameter[] parameters)=>new(31,8,0,time,0,name,0,parameters);
NativeParameter N(string name,object value)=>new(0,name,value);
lifecycleMapper.Map(P("StartLogging",1000000)).ToArray();
var requested=lifecycleMapper.Map(P("ExPlaybackId",1100000,N("ExPlaybackId_unique64",167UL),N("CriAtomExPlayerHn","0x1"),N("cue_name","Fixture"))).Single();
Check(requested.lifecycle=="created","Cue creation lacks structured lifecycle");
var limited=lifecycleMapper.Map(P("ExCue_StopByLimit",1200000,N("ExPlaybackId_unique64",167UL),N("cause ExPlaybackId_unique64",168UL),N("VoiceStopReason",54))).Single();
Check(limited.kind=="stop"&&limited.entity=="cue"&&limited.lifecycle=="stopped"&&limited.endReason=="playback-limit"&&limited.causeId=="playback:1:168","Playback limit lost structured reason/cause");
Check(EventSemantics.IsPlaybackEnd(limited)&&!EventSemantics.IsPlaybackReleased(limited),"Stop must not masquerade as release");
var released=lifecycleMapper.Map(P("ExPlaybackInfo_FreeInfo",1300000,N("ExPlaybackId_unique64",167UL))).Single();
Check(released.name=="Fixture"&&EventSemantics.IsPlaybackReleased(released),"Release loses the stopped instance identity");
var legacy=new WireEvent{kind="log",name="ExCue_StopByLimit",objectId="9:playback:1:167",raw=limited.raw};
Check(EventSemantics.IsPlaybackEnd(legacy)&&EventSemantics.EndReason(legacy)=="playback-limit"&&EventSemantics.CauseId(legacy)=="9:playback:1:168","Legacy limit evidence not reconstructed with epoch");
legacy.raw="{";Check(!EventSemantics.IsPlaybackEnd(legacy)&&EventSemantics.CauseId(legacy)=="","Corrupt legacy evidence must not fabricate reason/cause");
Check(EventSemantics.IsPlaybackReleased(new(){entity="cue",detail="CRI 播放实例释放"}),"Legacy release no longer supported");
Console.WriteLine("PASS structured cue lifecycle, limit cause, stopped/released distinction and legacy evidence compatibility");

int fixtures = 0;
foreach (var path in args)
{
    byte[] bytes = File.ReadAllBytes(path); int offset = 0, frames = 0, logs = 0;
    var mapper = new NativeEventMapper("fixture-session");
    var kinds = new Dictionary<string, int>(); long lastSequence = 0;
    bool foundBusChannels = false, snapshotVoice = false, exactControl = false;
    while (offset + 4 <= bytes.Length)
    {
        int length = NativeProtocol.ReadFrameLength(bytes.AsSpan(offset));
        if (offset + length > bytes.Length) break; // Research captures can end in a partial frame.
        var packet = NativeProtocol.Decode(bytes.AsSpan(offset, length));
        offset += length; frames++; if (packet.Command == 31) logs++;
        foreach (var ev in mapper.Map(packet))
        {
            Check(ev.seq == ++lastSequence, "Noncontiguous event sequence");
            Check(ev.session == "fixture-session" && ev.source == "cri-native", "Lost provenance/session");
            Check(double.IsFinite(ev.time) && ev.time >= 0, "Invalid timestamp");
            Check(!ev.raw.Contains("NaN"), "Invalid scalar propagated");
            kinds[ev.kind] = kinds.GetValueOrDefault(ev.kind) + 1;
            if (ev.kind == "aisac" && ev.name == "EG_Attack" && Math.Abs(ev.value - 0.0123) < 0.000001) exactControl = true;
            if (ev.kind == "play")
            {
                Check(ev.entity == "voice" && ev.objectId.StartsWith("voice:") && ev.parentId.StartsWith("playback"), "Voice/cue identity conflated");
                snapshotVoice |= ev.detail.Contains("起点未知");
            }
            if (ev.kind == "bus")
            {
                using var raw = JsonDocument.Parse(ev.raw);
                Check(raw.RootElement.GetProperty("channels").GetArrayLength() > 0, "Bus channels absent");
                foundBusChannels = true;
            }
        }
    }
    var metadata = Path.ChangeExtension(path, ".json");
    if (File.Exists(metadata))
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(metadata));
        if (expected.RootElement.TryGetProperty("packets", out var count)) Check(frames == count.GetInt32(), "Frame count differs from independent capture decoder");
    }
    if (Path.GetFileName(path).StartsWith("native-login"))
    {
        Check(logs == 56273, "Real login fixture count changed");
        Check(kinds.GetValueOrDefault("play") > 0 && kinds.GetValueOrDefault("stop") > 0 && kinds.GetValueOrDefault("aisac") > 0, "Real lifecycle/AISAC missing");
        Check(snapshotVoice && foundBusChannels, "Snapshot/Bus evidence missing");
    }
    if (Path.GetFileName(path).StartsWith("native-control-verified"))
        Check(kinds.GetValueOrDefault("selector") > 0 && kinds.GetValueOrDefault("block") > 0 && exactControl, "Verified controls/scalar value missing");
    Console.WriteLine($"{Path.GetFileName(path)}: {frames} frames, {logs} logs, {lastSequence} mapped events, trailing={bytes.Length - offset}, kinds={JsonSerializer.Serialize(kinds)}");
    fixtures++;
}
Console.WriteLine($"PASS framing, malformed-input rejection, and {fixtures} independent real capture fixture(s)");
await NativeIntegration.Run();
