using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CriScope.Core;

public sealed partial class Collector
{
    readonly ConcurrentDictionary<string, TcpClient> bridgeClients = new();
    readonly ConcurrentDictionary<string, string> bridgeIdentities = new();
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    async Task ReadClient(TcpClient client)
    {
        try
        {
            using var greeting = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            greeting.CancelAfter(TimeSpan.FromSeconds(10));
            var magic = new byte[4];
            int peek;
            while ((peek = await client.Client.ReceiveAsync(magic, SocketFlags.Peek, greeting.Token)) is > 0 and < 4)
                await Task.Delay(10, greeting.Token);
            if (peek == 0) return;
            if (magic.AsSpan().SequenceEqual("CSB3"u8)) await ReadBridgeClient(client);
            else await ReadLegacyClient(client);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
        { if (!stop.IsCancellationRequested) Status = "接入失败：" + e.Message; }
        finally { client.Dispose(); }
    }

    async Task ReadBridgeClient(TcpClient client)
    {
        Session? native = null, sdk = null;
        string identity = "";
        string ending = "游戏桥接已断开";
        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var greeting = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            greeting.CancelAfter(TimeSpan.FromSeconds(10));
            var prefix = new byte[8];
            await stream.ReadExactlyAsync(prefix, greeting.Token);
            int size = BinaryPrimitives.ReadInt32BigEndian(prefix.AsSpan(4));
            if (size is < 2 or > 16384) throw new InvalidDataException("桥接握手长度无效");
            var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, greeting.Token);
            using var doc = JsonDocument.Parse(bytes); var hello = doc.RootElement;
            string Text(string key) => hello.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
            identity = Text("clientId"); var capture = Text("captureId"); var name = Text("name");
            var machine = Text("machine"); var platform = Text("platform"); int pid = hello.GetProperty("pid").GetInt32();
            if (hello.GetProperty("version").GetInt32() != 3 || !Guid.TryParse(identity, out _) || !Guid.TryParse(capture, out _) ||
                name.Length is < 1 or > 256 || machine.Length > 256 || platform.Length > 100 || pid <= 0)
                throw new InvalidDataException("桥接身份无效");
            string fingerprint = JsonSerializer.Serialize(new { machine, pid, platform, name });
            if (bridgeIdentities.GetOrAdd(identity, fingerprint) != fingerprint) throw new InvalidDataException("客户端生命周期身份发生变化");
            if (bridgeClients.Count >= 32 && !bridgeClients.ContainsKey(identity)) throw new InvalidDataException("同时接入客户端已达上限32");
            if (bridgeClients.TryGetValue(identity, out var prior)) prior.Dispose();
            bridgeClients[identity] = client;
            Session Channel(string type, string source)
            {
                var s = new Session(new WireEvent { kind = "hello", value = 1, session = Guid.NewGuid().ToString("N"),
                    clientId = identity, captureId = capture, machine = machine, channel = type, source = source,
                    name = name, platform = platform, pid = pid, detail = client.Client.RemoteEndPoint?.ToString() ?? "" });
                sessions[s.Id] = s; return s;
            }
            native = Channel("native", "CRI Monitor"); sdk = Channel("sdk", "cri-sdk");
            native.SetCaptureState(false, false, "等待本进程原生通道"); sdk.SetCaptureState(true, true, "游戏扩展已接入");
            var mapper = new NativeEventMapper(native.Id);
            long sequence = 0; int nativeEpoch = 0; long nativeSequence = 0, sdkSequence = 0;
            void Accept(Session target, WireEvent ev, double observed, int epoch)
            {
                ev.session = target.Id; ev.clientId = identity; ev.captureId = capture; ev.channel = target.Channel;
                ev.machine = machine; ev.epoch = epoch; ev.observedTime = observed;
                ev.seq = ReferenceEquals(target, native) ? ++nativeSequence : ++sdkSequence;
                target.Accept(ev);
            }
            var header = new byte[25];
            while (!stop.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, stop.Token);
                int length = BinaryPrimitives.ReadInt32BigEndian(header);
                int kind = header[4]; long seq = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(5));
                int epoch = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(13));
                long micros = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(17)); double observed = micros / 1_000_000d;
                if (length is < 0 or > 8 * 1024 * 1024 || kind is < 1 or > 4 || seq <= sequence || epoch < 0 || micros < 0)
                    throw new InvalidDataException("桥接帧头无效");
                var payload = new byte[length]; await stream.ReadExactlyAsync(payload, stop.Token);
                sequence = seq;
                if (kind == 1)
                {
                    if (epoch <= 0 || epoch < nativeEpoch) throw new InvalidDataException("原生段编号无效或倒退");
                    if (epoch != nativeEpoch) { mapper = new NativeEventMapper(native.Id); nativeEpoch = epoch; }
                    native.ObserveTransport(observed, client.Available);
                    try
                    {
                        foreach (var ev in mapper.Map(NativeProtocol.Decode(payload)))
                        { ev.objectId = string.IsNullOrEmpty(ev.objectId) ? "" : epoch + ":" + ev.objectId;
                          ev.parentId = string.IsNullOrEmpty(ev.parentId) ? "" : epoch + ":" + ev.parentId;
                          ev.causeId = string.IsNullOrEmpty(ev.causeId) ? "" : epoch + ":" + ev.causeId;
                          Accept(native, ev, observed, epoch); }
                        native.SetCaptureState(true, true, "原生采集中 · 游戏桥接");
                    }
                    catch (InvalidDataException ex)
                    {
                        // The missing packet may have changed focus, destroyed an object or
                        // freed a Voice. Never derive new state from pre-gap cached inputs.
                        mapper = new NativeEventMapper(native.Id);
                        Accept(native, new WireEvent { kind = "gap", name = "无法解析原生帧", time = native.LastTime,
                            value = 1, source = "cri-native", detail = ex.Message }, observed, epoch);
                    }
                }
                else if (kind == 2)
                {
                    var ev = WireEvent.Parse(StrictUtf8.GetString(payload));
                    if (!double.IsFinite(ev.time) || ev.time < 0 || !double.IsFinite(ev.value) || ev.kind is "hello" ||
                        ev.kind == null || ev.name == null || ev.detail == null || ev.name.Length > 4096 || ev.detail.Length > 32768)
                        throw new InvalidDataException("SDK事件无效");
                    sdk.ObserveTransport(observed, client.Available);
                    ev.source = "cri-sdk"; Accept(sdk, ev, observed, epoch);
                }
                else if (kind == 3)
                {
                    using var status = JsonDocument.Parse(payload); var s = status.RootElement;
                    int channel = s.GetProperty("channel").GetInt32();
                    if (channel is not (1 or 2)) throw new InvalidDataException("状态通道无效");
                    var target = channel == 1 ? native : sdk;
                    target.ObserveTransport(observed, client.Available);
                    bool connected = s.GetProperty("connected").GetBoolean();
                    target.SetCaptureState(connected, connected, s.GetProperty("status").GetString() ?? "状态未知");
                }
                else
                {
                    using var gap = JsonDocument.Parse(payload); var g = gap.RootElement;
                    long count = g.GetProperty("count").GetInt64(); int channel = g.GetProperty("channel").GetInt32();
                    if (count < 0 || channel is not (1 or 2)) throw new InvalidDataException("丢失数量或通道无效");
                    var target = channel == 1 ? native : sdk;
                    target.ObserveTransport(observed, client.Available);
                    if (channel == 1 && count > 0) mapper = new NativeEventMapper(native.Id);
                    Accept(target, new WireEvent { kind = "gap", name = "传输记录缺失", time = target.LastTime,
                        value = count, source = target.Source, detail = g.GetProperty("reason").GetString() ?? "未知",
                        raw = StrictUtf8.GetString(payload) }, observed, epoch);
                }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or
            InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or DecoderFallbackException or FormatException or OverflowException)
        { ending = stop.IsCancellationRequested ? "采集服务已关闭" : e is EndOfStreamException ? "游戏连接已结束" : "桥接已断开：" + e.Message; }
        finally
        {
            native?.SetCaptureState(false, false, ending); sdk?.SetCaptureState(false, false, ending);
            if (bridgeClients.TryGetValue(identity, out var current) && ReferenceEquals(current, client)) bridgeClients.TryRemove(identity, out _);
        }
    }
}
