using CriScope.App;
using CriScope.Core;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
WireEvent E(string kind, string entity, string id, double time, string parent = "", long seq = 0) => new() { kind=kind, entity=entity, objectId=id, time=time, parentId=parent, seq=seq, name="Cue" };
PlaybackGroup One(params WireEvent[] e) => PlaybackPresentation.Group(e, 20).Single();
var request = E("request", "cue", "p1", 1, "player1", 10);
var voice = E("play", "voice", "v1", 1.1, "p1", 11);
var stop = E("stop", "voice", "v1", 3.1, "p1", 14);
var release = E("log", "cue", "p1", 3.2, seq:15); release.lifecycle="released";
var active = One(request, voice);
Check(active.StatusLabel=="播放中" && active.ActiveVoiceCount==1, "零音量默认值仍是正常播放实例");
Check(active.RequestAt==1 && active.StartedAt==1.1 && active.DurationAt(2.1)==1, "请求与实际Voice开始分开");
Check(One(request).StatusLabel=="等待 Voice 分配" && One(request).StartedAt==null, "单独请求不伪造播放开始");
var done = One(request, voice, stop, release);
Check(done.StatusLabel=="已结束" && done.ActiveVoiceCount==0, "完整释放闭合播放状态");
Check(Math.Abs(done.DurationAt(20)!.Value-2)<0.000001 && done.EndedAt==3.1 && done.InstanceEndedAt==3.2, "末Voice释放与实例释放各有时间");
Check(Math.Abs(done.InstanceDurationAt(20)!.Value-2.2)<0.000001, "实例历时不冒充Voice历时");
Check(One(request,voice,stop).StatusLabel=="声部已结束 · 实例待结束", "声部结束不能伪造实例释放");
var gap=E("gap", "", "", 2, seq:13);
Check(One(request,voice,gap).StatusLabel.Contains("待确认") && One(request,voice,gap).DurationAt(20)==null, "真实缺口阻止无根据播放状态及历时");
Check(One(request,voice,stop,release,E("gap","","",4)).StatusLabel=="已结束", "结束后的缺口不污染历史实例");
Check(One(stop).UnknownStart && One(stop).StartedAt==null, "只见释放不猜开始");
var prior=E("play","voice","prior",1,"p1"); prior.detail="连接时已存在 Voice；起点未知";
Check(One(request,prior).UnknownStart && One(request,prior).StartedAt==null && One(request,prior).StatusLabel=="播放中", "热连接的活动Voice保留活动状态而起点未知");
var lateRequest=E("request","cue","p2",4,"player1");
Check(PlaybackPresentation.Group([request,voice,lateRequest],20).Length==2, "同名Cue不合并实例");
var limit=E("stop","cue","p1",1.05); limit.lifecycle="stopped";limit.endReason="playback-limit";limit.causeId="p2";
var limited=One(request,limit,release);
Check(limited.StatusLabel=="数量限制终止" && limited.CausePlaybackId=="p2", "数量限制及触发实例明确");
Check(limited.InstanceEndedAt==1.05 && limited.InstanceReleasedAt==3.2 && limited.DurationAt(20)==null, "限制发生与释放分开，无Voice不产生历时");
var legacy=E("log","cue","p1",3.2);legacy.detail="CRI 播放实例释放";
Check(One(request,voice,stop,legacy).StatusLabel=="已结束", "兼容旧录制的释放");
Check(One(request,voice,release).DurationAt(20)==null && One(request,voice,release).StatusLabel=="已结束", "实例结束但Voice结束缺失不伪造Voice历时");
var oldLimit=E("log","","1:playback:1:167",2);oldLimit.name="ExCue_StopByLimit";
oldLimit.raw="""{"function":"ExCue_StopByLimit","parameters":[{"name":"ExPlaybackId_unique64","value":167},{"name":"cause ExPlaybackId_unique64","value":168}]}""";
Check(One(oldLimit).CausePlaybackId=="1:playback:1:168" && One(oldLimit).StatusLabel=="数量限制终止", "旧原生限制日志保留关联前缀");
var a0=E("aisac","control","player1",0.5); a0.name="Volume";a0.value=0.2;
var a1=E("aisac","control","player1",2);a1.name="Volume";a1.value=0.4;
var a2=E("aisac","control","player1",4);a2.name="Volume";a2.value=0.8;
var wrong=E("aisac","control","player2",0.6);wrong.name="Volume";wrong.value=1;
var beat=E("beat","control","p1",2.5);
var wrongBeat=E("beat","control","p2",2.5);
var controls=PlaybackPresentation.ControlsFor(done,[a0,a1,a2,wrong,beat,wrongBeat],20);
Check(controls.BeforeStart.Single()==a0, "旧实例显示开始前设置，不倒填未来Source设置");
Check(controls.DuringPlayback.Length==2 && controls.DuringPlayback.Contains(a1) && controls.DuringPlayback.Contains(beat), "变化及节拍按Player/实例与生命周期关联");
Check(PlaybackPresentation.ControlsFor(done,[a0,E("gap","","",0.9)],20).BeforeStart.Length==0, "开始前缺口清除不可信设置快照");
var selector=E("selector","control","player1",0.3);selector.name="Foot";selector.detail="Stone";
var clear=E("selector","control","player1",0.4);clear.name="全部 Selector";clear.detail="清除全部选择";
var selected=PlaybackPresentation.ControlsFor(done,[selector,clear,a0],20).BeforeStart;
Check(!selected.Contains(selector) && selected.Contains(clear), "Selector清除不会残留旧标签");
var overlapping=E("play","voice","v2",2,"p1");var overlappingEnd=E("stop","voice","v2",4,"p1");
Check(One(request,voice,stop,overlapping,overlappingEnd).EndedAt==4, "多Voice以最后一个释放为末端");
var nativeRequest=E("request","cue","native",1,"player1");nativeRequest.session="native";nativeRequest.channel="native";
var nativeVoice=E("play","voice","nv",1.1,"native");nativeVoice.session="native";nativeVoice.channel="native";
var sdkGap=E("gap","","",2);sdkGap.session="sdk";sdkGap.channel="sdk";
Check(One(nativeRequest,nativeVoice,sdkGap).StatusLabel=="播放中", "SDK缺口不污染原生Voice状态");
var sdkEarlyGap=E("gap","","",0.9);sdkEarlyGap.session="sdk";sdkEarlyGap.channel="sdk";
Check(PlaybackPresentation.ControlsFor(One(nativeRequest,nativeVoice),[a0,sdkEarlyGap],20).BeforeStart.Contains(a0), "SDK缺口不清除原生Player历史设置");
Check(One(release).StatusLabel.Contains("记录不完整"), "只有Cue结束时不宣称从未分配Voice");

WireEvent History(string kind, string entity, string id, long seq, string parent = "", string name = "Cue") =>
    new() { kind = kind, entity = entity, objectId = id, parentId = parent, seq = seq,
        time = seq, session = "history", channel = "native", epoch = 1, name = name };
var historyIndex = new CaptureRelationshipIndex();
var hRequest = History("request", "cue", "playback:1", 1, "player:1", "Cue A");
var hVoice = History("play", "voice", "voice:1", 2, "playback:1");
var hSetting = History("aisac", "control", "player:1", 3, name: "MuteSFX");
historyIndex.Observe(hRequest); historyIndex.Observe(hVoice); historyIndex.Observe(hSetting);
Check(historyIndex.TryGet(hSetting, out var frozen) &&
    frozen.Status == RelationshipEvidenceStatus.Observed &&
    frozen.Events.Any(e => e.seq == hRequest.seq) && frozen.Events.Any(e => e.seq == hVoice.seq) &&
    frozen.RecentCue?.seq == hRequest.seq, "写入时冻结播放实例、Voice 和最近 Cue 原始证据");
var hEnd = History("log", "cue", "playback:1", 4); hEnd.lifecycle = "released";
var hAfterEnd = History("aisac", "control", "player:1", 5, name: "MuteSFX");
historyIndex.Observe(hEnd); historyIndex.Observe(hAfterEnd);
Check(historyIndex.TryGet(hAfterEnd, out var endedSetting) &&
    endedSetting.Status == RelationshipEvidenceStatus.ObservedNoOpenPlayback &&
    endedSetting.Events.Length == 0 && endedSetting.RecentCue?.seq == hRequest.seq,
    "已结束 Cue 只作为此前最近播放线索，不冒充写入时实例");
var hGap = History("gap", "", "", 6);
var hAfterGap = History("aisac", "control", "player:1", 7, name: "MuteSFX");
historyIndex.Observe(hGap); historyIndex.Observe(hAfterGap);
Check(historyIndex.TryGet(hAfterGap, out var uncertainSetting) &&
    uncertainSetting.Status == RelationshipEvidenceStatus.Partial && uncertainSetting.RecentCue == null,
    "缺口后不跨越断点借用旧 Player 的 Cue");
var hNewRequest = History("request", "cue", "playback:2", 8, "player:1", "Cue B");
var hConcurrent = History("request", "cue", "playback:3", 9, "player:1", "Cue C");
var hConcurrentSetting = History("aisac", "control", "player:1", 10, name: "MuteSFX");
historyIndex.Observe(hNewRequest); historyIndex.Observe(hConcurrent); historyIndex.Observe(hConcurrentSetting);
Check(historyIndex.TryGet(hConcurrentSetting, out var concurrentSetting) &&
    concurrentSetting.Events.Count(e => e.kind == "request") == 2 &&
    concurrentSetting.Status == RelationshipEvidenceStatus.Partial &&
    concurrentSetting.RecentCue?.name == "Cue C", "并发实例分别归属同 Player，最近 Cue 单独标注");
var hDestroy = History("log", "", "player:1", 11, name: "ExPlayer_Destroy");
var hReused = History("aisac", "control", "player:1", 12, name: "MuteSFX");
historyIndex.Observe(hDestroy); historyIndex.Observe(hReused);
Check(historyIndex.TryGet(hReused, out var reusedSetting) && reusedSetting.Events.Length == 0 &&
    reusedSetting.RecentCue == null, "Player 句柄销毁后不继承旧实例和最近 Cue");
var hAfterReuseRequest = History("request", "cue", "playback:4", 13, "player:1", "Cue D");
var hCreateSuccess = History("log", "", "player:1", 14, name: "ExPlayer_Create_Success");
var hAfterCreate = History("aisac", "control", "player:1", 15, name: "MuteSFX");
historyIndex.Observe(hAfterReuseRequest); historyIndex.Observe(hCreateSuccess); historyIndex.Observe(hAfterCreate);
Check(historyIndex.TryGet(hAfterCreate, out var newGeneration) && newGeneration.Events.Length == 0 &&
    newGeneration.RecentCue == null, "Player 重建成功事件切断旧句柄关联");
var nextEpochSetting = History("aisac", "control", "player:1", 16, name: "MuteSFX");
nextEpochSetting.epoch = 2;
Check(AssociationPresentation.PlayerPlaybacksAtSetting([hAfterReuseRequest, nextEpochSetting], nextEpochSetting).Length == 0,
    "即使缺少显式边界，也不把旧 epoch 的 Playback 归给新 epoch 的同名 Player");

using (var liveSession = new Session(new WireEvent { kind = "hello", value = 1, session = "history", name = "test" }))
{
    var sessionEnd = WireEvent.Parse(hEnd.ToJson()); sessionEnd.time = 3.1;
    liveSession.Accept(hRequest); liveSession.Accept(hVoice); liveSession.Accept(hSetting); liveSession.Accept(sessionEnd);
    var muchLater = History("metric", "resource", "cpu", 5); muchLater.time = 124;
    liveSession.Accept(muchLater);
    var retained = liveSession.ViewSnapshot();
    var resolvedAtWrite = AssociationPresentation.ResolveAvailable(liveSession, hSetting, retained);
    Check(retained.Any(e => e.seq == hSetting.seq) && !retained.Any(e => e.seq == hRequest.seq) &&
        liveSession.TryGetSettingRelationship(hSetting, out var keptAtWrite) &&
        keptAtWrite.Events.Any(e => e.seq == hRequest.seq) &&
        resolvedAtWrite.Owners.Single().Request?.seq == hRequest.seq &&
        resolvedAtWrite.Source == "capture-index",
        "播放已结束且两分钟直播窗口逐出 Request 后，ResolveAvailable 仍恢复写入瞬间关联");
}

var historyDir = Path.Combine(Path.GetTempPath(), "CriScope-history-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(historyDir);
var firstPart = Path.Combine(historyDir, "part1.criscope");
var secondPart = Path.Combine(historyDir, "part2.criscope");
var standalone = Path.Combine(historyDir, "standalone.criscope");
var endedPart = Path.Combine(historyDir, "ended-part.criscope");
var clueOnlyPart = Path.Combine(historyDir, "clue-only.criscope");
var reconnectPart = Path.Combine(historyDir, "reconnect.criscope");
var firstConnectionFile = Path.Combine(historyDir, "first-connection.criscope");
var hHello = new WireEvent { kind = "hello", value = 1, session = "history", name = "test" };
try
{
    var other = History("metric", "resource", "cpu", 2);
    var diskSetting = History("aisac", "control", "player:1", 3, name: "MuteSFX");
    var diskEnd = History("log", "cue", "playback:1", 4); diskEnd.lifecycle = "released";
    var diskAfterEnd = History("aisac", "control", "player:1", 5, name: "MuteSFX");
    var duplicate = WireEvent.Parse(hRequest.ToJson()); duplicate.name = "错误的基线名"; duplicate.baseline = true;
    File.WriteAllText(firstPart, string.Join('\n', new[] { hHello, hRequest, other }.Select(e => e.ToJson())) + "\n");
    File.WriteAllText(secondPart, string.Join('\n', new[] { hHello, duplicate, diskSetting, diskEnd, diskAfterEnd }.Select(e => e.ToJson())) + "\n" +
        "{\"kind\":\"aisac\"", new System.Text.UTF8Encoding(false));
    var diskEvents = RecordingEvidenceReader.ReadRange([firstPart, secondPart], 1, 5, "history").ToArray();
    Check(diskEvents.Length == 5 && diskEvents[0].name == "Cue A" && diskEvents[^1].seq == 5,
        "跨分段范围读取去重基线，忽略未换行尾字节，保留原始请求");
    var recovered = HistoricalAssociation.Resolve([firstPart, secondPart], diskSetting);
    Check(recovered.Events.Single(e => e.kind == "request").name == "Cue A" &&
        recovered.Status == RelationshipEvidenceStatus.Observed && recovered.RecentCue?.seq == 1,
        "从物理日志回推写入时关联实例，不依赖两分钟直播缓存");
    var boundedSegments = new[] {
        new RecordingSegmentInfo(firstPart, 1, 2, 0, false),
        new RecordingSegmentInfo(secondPart, 3, 5, 1, false) };
    var boundedRecovered = HistoricalAssociation.Resolve(boundedSegments, diskSetting);
    Check(boundedRecovered.Status == RelationshipEvidenceStatus.Partial &&
        boundedRecovered.Events.Any(e => e.kind == "request" && e.objectId == "playback:1"),
        "按目标段元数据定位，只读轮转段的活动基线恢复实例");
    var recoveredAfterEnd = HistoricalAssociation.Resolve([firstPart, secondPart], diskAfterEnd);
    Check(recoveredAfterEnd.Status == RelationshipEvidenceStatus.ObservedNoOpenPlayback &&
        recoveredAfterEnd.Events.Length == 0 && recoveredAfterEnd.RecentCue?.name == "Cue A",
        "物理日志中已释放实例只作为历史 Cue 线索");

    var shortEnd = WireEvent.Parse(hEnd.ToJson()); shortEnd.seq = 2; shortEnd.time = 2;
    File.WriteAllText(endedPart, string.Join('\n', new[] { hHello, hRequest, shortEnd }.Select(e => e.ToJson())) + "\n");
    var clueOnlySetting = History("aisac", "control", "player:1", 3, name: "MuteSFX");
    File.WriteAllText(clueOnlyPart, string.Join('\n', new[] { hHello, clueOnlySetting }.Select(e => e.ToJson())) + "\n");
    var priorEnded = HistoricalAssociation.Resolve(new[] {
        new RecordingSegmentInfo(endedPart, 1, 2, 0, false),
        new RecordingSegmentInfo(clueOnlyPart, 3, 3, 0, false) }, clueOnlySetting);
    Check(priorEnded.Events.Length == 0 && priorEnded.RecentCue?.name == "Cue A" &&
        priorEnded.Status == RelationshipEvidenceStatus.Partial,
        "目标段无活动实例时，按需从前段补此前最近 Cue，仍不将其作为 Owner");

    var reconnectionHeader = WireEvent.Parse(hHello.ToJson());
    reconnectionHeader.detail = "connectionStart=true";
    var firstConnectionSetting = History("aisac", "control", "player:1", 2, name: "MuteSFX");
    File.WriteAllText(firstConnectionFile, string.Join('\n', new[] { reconnectionHeader, hRequest, firstConnectionSetting }
        .Select(e => e.ToJson())) + "\n");
    var firstConnectionEvidence = HistoricalAssociation.Resolve([firstConnectionFile], firstConnectionSetting);
    Check(firstConnectionEvidence.Status == RelationshipEvidenceStatus.Observed &&
        firstConnectionEvidence.Events.Single(e => e.kind == "request").seq == 1,
        "首段从 seq1 完整采集时，连接文件头本身不伪造采集缺口");
    File.WriteAllText(reconnectPart, string.Join('\n', new[] { reconnectionHeader, clueOnlySetting }.Select(e => e.ToJson())) + "\n");
    var reconnectSegments = new[] {
        new RecordingSegmentInfo(firstPart, 1, 2, 0, false, true),
        new RecordingSegmentInfo(reconnectPart, 3, 3, 0, false, true) };
    var afterReconnect = HistoricalAssociation.Resolve(reconnectSegments, clueOnlySetting);
    var afterReconnectFull = HistoricalAssociation.Resolve([firstPart, reconnectPart], clueOnlySetting);
    Check(afterReconnect.Events.Length == 0 && afterReconnect.RecentCue == null &&
        afterReconnect.Status == RelationshipEvidenceStatus.Partial &&
        afterReconnectFull.Events.Length == 0 && afterReconnectFull.RecentCue == null,
        "重连边界阻止借用上一链接的活跃实例和此前 Cue，完整扫描亦同");

    var sparseRequest = History("request", "cue", "playback:sparse", 10, "player:1", "Sparse Cue");
    sparseRequest.baseline = true;
    var sparseSetting = History("aisac", "control", "player:1", 101, name: "MuteSFX");
    File.WriteAllText(standalone, string.Join('\n', new[] { hHello, sparseRequest, sparseSetting }.Select(e => e.ToJson())) + "\n");
    var sparseRecovered = HistoricalAssociation.Resolve([standalone], sparseSetting);
    Check(sparseRecovered.Status == RelationshipEvidenceStatus.Partial &&
        sparseRecovered.Events.Single(e => e.kind == "request").name == "Sparse Cue" &&
        sparseRecovered.Reason.Contains("分段基线", StringComparison.Ordinal),
        "单独读取轮转段时保留稀疏基线播放证据，并标注前史不完整");
}
finally
{
    File.Delete(firstPart); File.Delete(secondPart); File.Delete(standalone);
    File.Delete(endedPart); File.Delete(clueOnlyPart); File.Delete(reconnectPart);
    File.Delete(firstConnectionFile); Directory.Delete(historyDir);
}
Console.WriteLine($"Playback presentation checks passed: {checks}");
