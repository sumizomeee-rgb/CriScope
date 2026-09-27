using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CriScope.Core;

var directory = Path.Combine(Path.GetTempPath(), "CriScope-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var failures = new List<string>();
int completed = 0;
try
{
    await Run("TCP 握手、重传去重、多客户端隔离、断线重连与录制回放", async () =>
    {
        using var collector = new Collector(directory);
        var port = FreePort(); collector.Start(port);
        string a = Guid.NewGuid().ToString("N"), b = Guid.NewGuid().ToString("N");
        using var first = await Peer.Connect(port, Hello(a));
        Equal(0L, first.InitialAck, "首次握手水位");
        Check(collector.Sessions.Single().Capturing, "首次握手即标记采集开启");
        using var second = await Peer.Connect(port, Hello(b, "另一个客户端"));
        Equal(1L, await first.Send(Event(a, 1, "state", 1, 1)), "采集状态 ACK");
        Equal(1L, await second.Send(Event(b, 1, "play", 1)), "独立序列 ACK");
        var session = collector.Sessions.Single(s => s.Id == a);
        Check(session.Capturing, "采集已开启");
        Check(session.Recording && File.Exists(session.RecordingPath), "握手后首个事件前自动建档");
        string firstPath=session.RecordingPath;
        Equal(2L, await first.Send(Event(a, 2, "play", 2)), "播放 ACK");
        Equal(2L, await first.Send(Event(a, 2, "play", 2)), "重复事件 ACK 不变");
        Equal(2L, session.Total, "重复事件不计数");
        first.Dispose();
        await Until(() => !session.Connected);
        Check(!session.Recording && File.Exists(firstPath), "断线后自动封口");
        using var reconnected = await Peer.Connect(port, Hello(a));
        Equal(2L, reconnected.InitialAck, "重连保留水位");
        Check(session.Capturing, "重连握手保持采集开启");
        Check(session.Recording && session.RecordingPath!=firstPath, "重连独立建档");
        await reconnected.Send(Event(a, 2, "play", 2));
        await reconnected.Send(Event(a, 3, "aisac", 3, .75));
        await reconnected.Send(Event(a, 4, "gap", 4, 7));
        await reconnected.Send(Event(a, 5, "state", 5, 0));
        reconnected.Dispose();await Until(()=>!session.Connected);
        Check(!session.Capturing && session.Dropped == 7, "采集停止与丢失计数");
        Equal(1L, collector.Sessions.Single(s => s.Id == b).Total, "会话隔离");
        Equal(2,session.RecordingPaths.Length,"两次连接有独立文件");
        var firstReplay=collector.LoadRecording(firstPath);
        var secondReplay=collector.LoadRecording(session.RecordingPath);
        Check(firstReplay.IsReplay && secondReplay.IsReplay,"回放标记");
        Equal(2L,firstReplay.Total,"首段包含首个事件且不包含重传");
        Equal("3,4,5",string.Join(',',secondReplay.Snapshot().Where(e=>!e.baseline).Select(e=>e.seq)),"重连段增量事件完整");
        Check(!secondReplay.Snapshot().Any(e=>e.seq==2&&e.baseline),"断线后的新链接不借用此前活动状态");
        Check(session.RecordingSegments[1].StartsNewConnection,"重连段明确标记证据边界");
        Equal(session.Watermark, secondReplay.Watermark, "末段录制水位");
    });

    await Run("替换同一会话连接不会误标离线", async () =>
    {
        using var collector = new Collector(directory); var port = FreePort(); collector.Start(port);
        string id = Guid.NewGuid().ToString("N");
        using var old = await Peer.Connect(port, Hello(id));
        await old.Send(Event(id, 1, "play", 1));
        using var next = await Peer.Connect(port, Hello(id));
        Equal(1L, next.InitialAck, "替换连接水位");
        await next.Send(Event(id, 2, "stop", 2));
        Check(collector.Sessions.Single().Connected, "替换连接保持在线");
    });

    await Run("桌面重启后握手恢复采集状态，无需重发已确认的 state", async () =>
    {
        string id = Guid.NewGuid().ToString("N");
        using (var previous = new Collector(directory))
        {
            var port = FreePort(); previous.Start(port);
            using var peer = await Peer.Connect(port, Hello(id));
            Equal(1L, await peer.Send(Event(id, 1, "state", 1, 1)), "开启状态此前已确认");
        }
        using var restarted = new Collector(directory);
        var nextPort = FreePort(); restarted.Start(nextPort);
        using var reconnected = await Peer.Connect(nextPort, Hello(id));
        Equal(0L, reconnected.InitialAck, "新接收器没有旧水位");
        var session = restarted.Sessions.Single();
        Check(session.Capturing, "无需重发 state=1 即恢复采集状态");
        await reconnected.Send(Event(id, 2, "play", 2));
        Check(session.Capturing, "接收后续事件时仍为采集中");
        await reconnected.Send(Event(id, 3, "state", 3, 0));
        Check(!session.Capturing, "显式关闭状态结束采集");
    });

    await Run("禁止连接中切换会话", async () =>
    {
        using var collector = new Collector(directory); var port = FreePort(); collector.Start(port);
        string id = Guid.NewGuid().ToString("N");
        using var peer = await Peer.Connect(port, Hello(id));
        await peer.SendRaw(Event(Guid.NewGuid().ToString("N"), 1, "play", 1).ToJson());
        Check(await peer.Closed(), "会话身份变化应断开");
        Equal(0L, collector.Sessions.Single().Total, "伪造来源不入库");
    });

    await Run("重连来源元数据不可篡改且原连接继续可用", async () =>
    {
        using var collector = new Collector(directory); var port = FreePort(); collector.Start(port);
        string id = Guid.NewGuid().ToString("N");
        using var original = await Peer.Connect(port, Hello(id));
        using var imposter = await Peer.ConnectRaw(port);
        var wrong = Hello(id); wrong.pid++;
        await imposter.SendRaw(wrong.ToJson());
        Check(await imposter.Closed(), "不一致来源被拒绝");
        Equal(1L, await original.Send(Event(id, 1, "play", 1)), "原连接未被篡改连接踢下线");
        Equal(101, collector.Sessions.Single().Pid, "来源 PID 保持");
    });

    await Run("TCP 多行合并与 UTF-8 分片不丢事件", async () =>
    {
        using var collector = new Collector(directory); var port = FreePort(); collector.Start(port);
        string id = Guid.NewGuid().ToString("N");
        using var peer = await Peer.Connect(port, Hello(id));
        var large = Event(id, 1, "log", 1); large.detail = new string('测', 5000);
        await peer.SendFragmented(large.ToJson() + "\r\n" + Event(id, 2, "stop", 2).ToJson() + "\n");
        Equal(1L, await peer.ReadAck(), "分片 ACK");
        Equal(2L, await peer.ReadAck(), "合并行 ACK");
        Equal(large.detail, collector.Sessions.Single().Snapshot()[0].detail, "跨缓冲区 UTF-8 内容");
    });

    await Run("禁止连接中再次握手篡改来源元数据", async () =>
    {
        using var collector = new Collector(directory); var port = FreePort(); collector.Start(port);
        string id = Guid.NewGuid().ToString("N");
        using var peer = await Peer.Connect(port, Hello(id));
        await peer.SendRaw(Hello(id, "伪造客户端").ToJson());
        await peer.ReadLine();
        Equal("测试客户端", collector.Sessions.Single().Name, "来源名称不可中途改写");
    });

    await Run("损坏 JSON、空字段、超长行、错误协议不影响监听", async () =>
    {
        using var collector = new Collector(directory); var port = FreePort(); collector.Start(port);
        foreach (var mode in new[] { "json", "null", "length", "version" })
        {
            string id = Guid.NewGuid().ToString("N");
            if (mode == "version")
            {
                var hello = Hello(id); hello.value = 999;
                using var rejected = await Peer.ConnectRaw(port);
                await rejected.SendRaw(hello.ToJson());
                Check(await rejected.Closed(), "错误版本被拒绝");
            }
            else
            {
                using var bad = await Peer.Connect(port, Hello(id));
                await bad.SendRaw(mode == "json" ? "{" : mode == "length" ? new string('x', 65537) :
                    "{\"kind\":\"play\",\"session\":\"" + id + "\",\"seq\":1,\"name\":null}");
                Check(await bad.Closed(), "损坏连接应断开：" + mode);
            }
            string nextId = Guid.NewGuid().ToString("N");
            using var good = await Peer.Connect(port, Hello(nextId));
            Equal(1L, await good.Send(Event(nextId, 1, "play", 1)), "监听继续接受：" + mode);
        }
    });

    await Run("固定时间起点、墙钟估算与录制回放保留", () =>
    {
        string id=Guid.NewGuid().ToString("N");
        var clock=new ManualClock(new DateTimeOffset(2026,9,26,8,0,0,TimeSpan.Zero));
        using var live=new Session(Hello(id),clock);
        var first=Event(id,1,"log",555);first.entity="capture-segment";
        live.Accept(first);
        Equal(555d,live.TimeOrigin,"原始时钟不从零伪造，采集起点固定");
        Equal("capture-start",live.TimeOriginBasis,"起点来源明确");
        clock.Advance(TimeSpan.FromSeconds(1));
        var late=Event(id,2,"play",565);live.Accept(late);
        Equal(first.receivedAtUtc!.Value.AddSeconds(10),live.EstimateWallTime(late)!.Value,"发生时间按来源时差估算，不冒充接收时间");
        Check(live.FormatWallTime(late).Contains("估算"),"墙钟明确估算口径");
        live.StartRecording(directory);
        clock.Advance(TimeSpan.FromSeconds(130));live.Accept(Event(id,3,"metric",700));
        Equal(555d,live.TimeOrigin,"缓存淘汰后起点不移动");
        clock.Advance(TimeSpan.FromSeconds(2));
        Equal(2000d,live.ReceiveAgeMilliseconds!.Value,"新鲜度来自接收单调时钟");
        live.StopRecording();
        using var collector=new Collector(directory);var replay=collector.LoadRecording(live.RecordingPath);
        Equal(live.TimeOrigin,replay.TimeOrigin,"录制恢复原采集起点");
        Equal(live.EstimateWallTime(late),replay.EstimateWallTime(late),"录制恢复墙钟锚点");
        Check(replay.ReceiveAgeMilliseconds==null,"历史记录不冒充在线新鲜度");
        var oldPath=Path.Combine(directory,"old-clock.criscope");
        File.WriteAllLines(oldPath,[Hello(id).ToJson(),Event(id,1,"play",888).ToJson()]);
        var old=collector.LoadRecording(oldPath);
        Equal(888d,old.TimeOrigin,"旧录制采用首个可用事件时间");
        Check(old.EstimateWallTime(old.Snapshot()[0])==null,"旧录制不猜墙钟");
        return Task.CompletedTask;
    });

    await Run("来源到游戏桥观察差仅报告同段相对增长", () =>
    {
        string id=Guid.NewGuid().ToString("N");using var s=new Session(Hello(id));
        WireEvent E(long seq,double time,double observed,int epoch)=>new(){session=id,seq=seq,kind="metric",time=time,observedTime=observed,channel="native",epoch=epoch};
        s.Accept(E(1,100,9000,1));Equal(0d,s.SourceObservationLagGrowthMilliseconds!.Value,"独立时钟偏移不冒充九千秒延迟");
        s.Accept(E(2,101,9004,1));Equal(3000d,s.SourceObservationLagGrowthMilliseconds!.Value,"相对来源到桥时间差增长可见");
        s.Accept(E(3,104,9005,1));Equal(1000d,s.SourceObservationLagGrowthMilliseconds!.Value,"追上来源时相对增长下降");
        s.Accept(E(4,105,9500,2));Equal(0d,s.SourceObservationLagGrowthMilliseconds!.Value,"新段重新建立相对基线");
        return Task.CompletedTask;
    });

    await Run("结束状态按截止时间淘汰且不会误删复用对象", () =>
    {
        string id=Guid.NewGuid().ToString("N");using var s=new Session(Hello(id));
        WireEvent E(long seq,double t,string kind,string entity,string objectId,string lifecycle="")=>new(){session=id,seq=seq,time=t,kind=kind,entity=entity,objectId=objectId,lifecycle=lifecycle};
        s.Accept(E(1,1,"request","cue","c","created"));s.Accept(E(2,2,"play","voice","v","allocated"));
        s.Accept(E(3,3,"stop","voice","v","released"));s.Accept(E(4,4,"log","cue","c","released"));
        Check(!s.Baseline().Any(e=>e.kind is "request" or "play"),"结构化结束不会出现在活动基线");
        s.Accept(E(5,5,"play","voice","v","allocated"));
        s.Accept(E(6,130,"metric","resource","m"));
        Check(s.ViewSnapshot().Any(e=>e.seq==5)&&!s.ViewSnapshot().Any(e=>e.seq==1),"只淘汰已经结束的旧起点，不删除复用后的活动Voice");
        s.Accept(E(7,131,"gap","",""));
        Check(s.Baseline().Length==0,"缺失清空所有有序结束状态");
        return Task.CompletedTask;
    });

    await Run("自动录制轮转、活动基线、空间上限与异常尾行", () =>
    {
        string id=Guid.NewGuid().ToString("N");
        using(var live=new Session(Hello(id)))
        {
            live.StartAutomaticRecording(directory,new AutomaticRecordingOptions(SegmentBytes:long.MaxValue,SegmentEvents:2,MaxSessionBytes:100000,MinimumFreeBytes:0));
            Check(live.Recording && File.Exists(live.RecordingPath),"自动建档立即可见");
            var request=Event(id,1,"request",1);request.entity="cue";request.parentId="player:1";
            live.Accept(request);
            live.Accept(Event(id,2,"aisac",2));
            live.Accept(Event(id,3,"metric",3));
            Equal(2,live.RecordingPaths.Length,"事件数阈值轮转");
            Equal(3L,live.RecordingSegments[1].FirstIncrementalSequence,"段元数据指向首个增量");
            Check(live.RecordingSegments[1].BaselineCount>=1,"新段冻结活动基线");
            string second=live.RecordingPath;
            live.StopRecording();
            using var collector=new Collector(directory);
            var replay=collector.LoadRecording(second);
            Check(replay.Snapshot().Any(e=>e.seq==1&&e.baseline&&e.kind=="request"),"单独打开轮转段仍能看到活动播放起点");
            Check(replay.Snapshot().Any(e=>e.seq==3&&!e.baseline),"轮转边界事件只写入新段增量");
            File.AppendAllText(second,"{\"kind\":",Encoding.UTF8);
            Equal(replay.Total,collector.LoadRecording(second).Total,"异常退出未换行尾字节可恢复");
            File.AppendAllText(second,"\n",Encoding.UTF8);
            try {collector.LoadRecording(second);throw new Exception("完整损坏行被静默忽略");}
            catch(InvalidDataException) { }
        }
        using(var capped=new Session(Hello(Guid.NewGuid().ToString("N"))))
        {
            capped.StartAutomaticRecording(directory,new AutomaticRecordingOptions(SegmentBytes:long.MaxValue,SegmentEvents:int.MaxValue,MaxSessionBytes:2000,MinimumFreeBytes:0));
            Check(capped.Recording,"小容量测试应先成功建档");
            string cappedId=capped.Id;
            capped.Accept(Event(cappedId,1,"metric",1));
            var large=Event(cappedId,2,"metric",2);large.detail=new string('x',4000);
            capped.Accept(large);
            Check(!capped.Recording && capped.Error?.Contains("证据缺口")==true,"达到容量上限应继续直播并显式标注磁盘证据缺口");
            Equal(2L,capped.Total,"录制停止不影响内存事件");
        }
        using(var largeBaseline=new Session(Hello(Guid.NewGuid().ToString("N"))))
        {
            largeBaseline.StartAutomaticRecording(directory,new AutomaticRecordingOptions(SegmentBytes:1000,SegmentEvents:int.MaxValue,MaxSessionBytes:100000,MinimumFreeBytes:0));
            string baselineId=largeBaseline.Id;
            var largeRequest=Event(baselineId,1,"request",1);largeRequest.entity="cue";largeRequest.parentId="player:1";
            largeRequest.detail=new string('x',2000);largeBaseline.Accept(largeRequest);
            largeBaseline.Accept(Event(baselineId,2,"metric",2));
            Equal(2,largeBaseline.RecordingPaths.Length,"大活动基线前只轮转一次");
            largeBaseline.Accept(Event(baselineId,3,"metric",3));
            Equal(2,largeBaseline.RecordingPaths.Length,"大基线不应让后续每条事件都轮转");
        }
        return Task.CompletedTask;
    });

    await Run("原生 Bus 只在离散事件后写快照，直播与证据连续性保留", () =>
    {
        string id = Guid.NewGuid().ToString("N");
        var hello = Hello(id); hello.channel = "native";
        using var live = new Session(hello);
        live.StartAutomaticRecording(directory, new AutomaticRecordingOptions(
            SegmentBytes: long.MaxValue, SegmentEvents: int.MaxValue, MaxSessionBytes: 100000, MinimumFreeBytes: 0));
        WireEvent Bus(long seq, double time, string busId)
        {
            var ev = Event(id, seq, "bus", time, seq);
            ev.entity = "bus"; ev.objectId = busId;
            return ev;
        }
        live.Accept(Bus(1, 1, "rack:0/bus:1"));
        Check(live.Snapshot().Any(e => e.seq == 1), "无触发时 Bus 仍供实时界面使用");
        Check(live.FlushRecordingForRead(), "Bus 落盘测试刷盘");
        using var collector = new Collector(directory);
        Check(!collector.LoadRecording(live.RecordingPath).Snapshot().Any(e => e.kind == "bus"), "无触发的 Bus 不写磁盘");

        var request = Event(id, 2, "request", 2); request.entity = "cue"; live.Accept(request);
        live.Accept(Bus(3, 2.1, "rack:0/bus:1"));
        live.Accept(Bus(4, 2.2, "rack:0/bus:1"));
        live.Accept(Bus(5, 2.3, "rack:0/bus:2"));
        live.Accept(Bus(6, 2.6, "rack:0/bus:3"));
        var voice = Event(id, 7, "play", 3); voice.entity = "voice"; live.Accept(voice);
        live.Accept(Bus(8, 3.1, "rack:0/bus:1"));
        var aisac = Event(id, 9, "aisac", 4); aisac.entity = "control"; live.Accept(aisac);
        live.Accept(Bus(10, 4.1, "rack:0/bus:1"));
        live.Accept(Bus(11, 4.2, "rack:0/bus:1"));
        live.Accept(Bus(12, 4.3, "rack:0/bus:2"));
        Equal(9, live.Snapshot().Count(e => e.kind == "bus"), "实时界面收到全部 Bus 样本");
        Check(live.FlushRecordingForRead(), "触发后的 Bus 快照刷盘");
        var replay = collector.LoadRecording(live.RecordingPath);
        Equal("3,5,8,10,12", string.Join(',', replay.Snapshot().Where(e => e.kind == "bus").Select(e => e.seq)),
            "每个触发窗口内每个 Bus 只写下一条样本");
        Check(replay.IsReplay && replay.Snapshot().Any(e => e.seq == 9 && e.kind == "aisac"),
            "单段导入可以浏览 Bus 快照与触发事件");
        Check(!live.EvidenceSnapshot().HasUnrecordedGap, "有意降采样不标成磁盘证据缺口");
        live.Accept(Event(id, 13, "metric", 130));
        Check(!live.Snapshot().Any(e => e.kind == "bus"), "旧 Bus 已离开实时内存窗口");
        Check(!live.EvidenceSnapshot().HasUnrecordedGap, "内存淘汰后仍不误报降采样造成的缺口");
        live.StopRecording();
        live.Accept(Event(id, 14, "metric", 131));
        live.Accept(Event(id, 15, "metric", 300));
        Check(live.EvidenceSnapshot().HasUnrecordedGap, "真正未落盘且已离开内存的事件仍标出缺口");
        return Task.CompletedTask;
    });

    await Run("Live 时间窗口与事件数量有界，回放不受 Live 裁剪", () =>
    {
        string id = Guid.NewGuid().ToString("N");
        using var session = new Session(Hello(id));
        for (int i = 1; i <= Session.MaxLiveEvents + 13; i++) session.Accept(Event(id, i, "play", 1));
        Equal(Session.MaxLiveEvents, session.Snapshot().Length, "数量上限");
        Equal(13L, session.Evicted, "数量淘汰计数");
        session.Accept(Event(id, Session.MaxLiveEvents + 14, "stop", 122));
        Equal(1, session.Snapshot().Length, "时间淘汰");
        var recording = Path.Combine(directory, "long.criscope");
        File.WriteAllLines(recording, new[] { Hello(id).ToJson(), Event(id, 1, "play", 1).ToJson(), Event(id, 2, "stop", 999).ToJson() });
        using var collector = new Collector(directory);
        Equal(2, collector.LoadRecording(recording).Snapshot().Length, "回放保留完整时间段");
        return Task.CompletedTask;
    });
}
finally { Directory.Delete(directory, true); }
Console.WriteLine($"结果：{completed - failures.Count}/{completed} 通过");
foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

async Task Run(string name, Func<Task> test)
{
    completed++;
    try { await test(); Console.WriteLine("通过：" + name); }
    catch (Exception e) { failures.Add("失败：" + name + " — " + e.Message); }
}
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}，期望 {expected}，实际 {actual}"); }
static WireEvent Hello(string id, string name = "测试客户端") => new() { kind = "hello", session = id, name = name, value = 1, platform = "WindowsEditor", pid = 101 };
static WireEvent Event(string id, long seq, string kind, double time, double value = 0) => new() { session = id, seq = seq, kind = kind, time = time, value = value, name = "测试音频", objectId = "42", detail = "测试证据" };
static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
static async Task Until(Func<bool> condition) { using var timeout = new CancellationTokenSource(5000); while (!condition()) await Task.Delay(10, timeout.Token); }

sealed class ManualClock(DateTimeOffset utc) : TimeProvider
{
    long ticks;
    public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow()=>utc;
    public override long GetTimestamp()=>ticks;
    public void Advance(TimeSpan span){utc+=span;ticks+=span.Ticks;}
}

sealed class Peer : IDisposable
{
    readonly TcpClient client;
    readonly StreamReader reader;
    readonly StreamWriter writer;
    public long InitialAck { get; private set; }
    Peer(TcpClient client) { this.client = client; reader = new StreamReader(client.GetStream()); writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }; }
    public static async Task<Peer> ConnectRaw(int port) { var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port); return new Peer(client); }
    public static async Task<Peer> Connect(int port, WireEvent hello) { var peer = await ConnectRaw(port); peer.InitialAck = await peer.Send(hello); return peer; }
    public async Task<string?> ReadLine() => await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
    public Task SendRaw(string line) => writer.WriteLineAsync(line);
    public async Task SendFragmented(string text) { var bytes = Encoding.UTF8.GetBytes(text); for (int offset = 0; offset < bytes.Length; offset += 17) await client.GetStream().WriteAsync(bytes.AsMemory(offset, Math.Min(17, bytes.Length - offset))); }
    public async Task<long> ReadAck() { using var doc = JsonDocument.Parse(await ReadLine() ?? throw new IOException("ACK 前断开")); return doc.RootElement.GetProperty("ack").GetInt64(); }
    public async Task<long> Send(WireEvent e) { await SendRaw(e.ToJson()); return await ReadAck(); }
    public async Task<bool> Closed() { try { return await ReadLine() == null; } catch (IOException) { return true; } }
    public void Dispose() { client.Dispose(); }
}
