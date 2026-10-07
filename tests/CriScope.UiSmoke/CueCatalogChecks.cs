using System.Text.Json;
using CriScope.App;
using CriScope.Core;

internal static class CueCatalogChecks
{
    private const string Client = "catalog-fixture", Capture = "catalog-capture";
    private static WireEvent Event(string kind, double time, long seq, string raw = "") => new() {
        kind = kind, time = time, observedTime = time + 100, seq = seq, raw = raw,
        clientId = Client, captureId = Capture, session = "sdk-catalog-fixture", channel = "sdk"
    };
    private static WireEvent Bank(double time, string generation = "first", bool snapshot = true, string handle = "0xabc", string lifecycle = "loaded")
    {
        var e = Event("acb-catalog", time, (long)(time * 10), JsonSerializer.Serialize(new {
            acbHandle = handle, generation, connectionSnapshot = snapshot, observedFrom = time - 1
        }));
        e.objectId = "acb:" + handle; e.lifecycle = lifecycle; return e;
    }
    private static WireEvent Part(double time, string category = "Volume_Music", int ordinal = 0, string generation = "first", string handle = "0xabc", string name = "Shared Cue")
    {
        var e = Event("cue-catalog", time, (long)(time * 10), JsonSerializer.Serialize(new {
            basis = "acb-cue-catalog", acbHandle = handle, generation,
            cues = new[] { new { name, id = 7, length = 184615, basis = "cue-config", firstGroupNo = 0,
                categories = new[] { category }, categoryDetails = new[] { new { name = category, index = ordinal, groupNo = 0, ordinal } } } }
        }));
        e.objectId = "acb:" + handle + ":part:0"; e.parentId = "acb:" + handle; return e;
    }
    private static WireEvent Request(double time = 5, string handle = "0X000ABC", string name = "Shared Cue")
    {
        var e = Event("request", time, (long)(time * 10), JsonSerializer.Serialize(new {
            parameters = new[] { new { name = "CriAtomExAcbHn", value = handle }, new { name = "Acb Name", value = "MusicSheet" } }
        }));
        e.session = "native-catalog-fixture"; e.channel = "native"; e.entity = "cue"; e.objectId = "1:playback:1:9";
        e.name = name; e.baseline = true; e.detail = "热接入；起点未知"; return e;
    }
    private static PlaybackGroup Playback(WireEvent request) => new(request.objectId, request, null, []) { ObservedThrough = 30 };

    public static void Run(Action<bool, string> check)
    {
        var begin = Bank(10); var part = Part(11); var request = Request(); var group = Playback(request);
        var catalog = new CueCatalogIndex([begin, part], 30);
        var authored = catalog.Resolve(group);
        check(group.UnknownStart && authored is { value: 184615 } && AssociationPresentation.CueCategories(authored).SequenceEqual(["Volume_Music"]),
            "热接入实例无 Source 最新播放记录，仍按 ACB 句柄和 Cue 名查询配置");
        check(PlaybackCategoryPresentation.Resolve(null, authored, []) is { Name: "Volume_Music", Ordinal: 0 }, "热接入配置分类可给轨道归色");
        check(CueCatalogIndex.RequestHandle(Request(handle: "2748")) == "0xabc", "原生句柄统一十六进制、十进制和补零格式");
        check(catalog.Resolve(Playback(Request(handle: "0xdef"))) == null && catalog.Resolve(Playback(Request(name: "Other Cue"))) == null,
            "同名 Cue、ACB 别名都不能替代精确句柄匹配");
        var otherCapture = Request(); otherCapture.captureId = "other-capture";
        var otherClient = Request(); otherClient.clientId = "other-client";
        check(catalog.Resolve(Playback(otherCapture)) == null && catalog.Resolve(Playback(otherClient)) == null, "ACB 目录不跨客户端或采集连接");
        check(new CueCatalogIndex([begin, part], 10).Resolve(group) == null, "历史时刻不使用尚未采集的 Cue 目录");
        check(new CueCatalogIndex([Bank(10, snapshot: false), part], 30).Resolve(group) == null, "后续加载的 ACB 不反套到早于加载观测范围的播放");

        var released = Bank(20, lifecycle: "released");
        var reload = Bank(21, "second", false); var replacement = Part(22, "Volume_Voice", 2, "second");
        var reloaded = new CueCatalogIndex([begin, part, released, reload, replacement], 30);
        check(AssociationPresentation.CueCategories(reloaded.Resolve(group)).SequenceEqual(["Volume_Music"]) &&
              AssociationPresentation.CueCategories(reloaded.Resolve(Playback(Request(21)))).SequenceEqual(["Volume_Voice"]),
            "同句柄重载按 ACB 世代区分，旧播放保留旧配置");
        check(new CueCatalogIndex([begin, part, released], 30).Resolve(Playback(Request(25))) == null, "卸载后的播放不使用陈旧配置");
        check(new CueCatalogIndex([begin, part, Event("gap", 15, 150)], 30).Resolve(group) == null, "采集缺口撤销未经重新核实的目录");
        check(new CueCatalogIndex([begin, Event("cue-catalog", 11, 110, "{broken")], 30).Resolve(group) == null, "坏目录数据不会使界面崩溃或造出分类");
        var firstGroup = Event("category-catalog", 10, 99, JsonSerializer.Serialize(new {
            basis = "acf-category-catalog", firstGroupNo = 0, firstGroupCategories = new[] {
                new { name = "Volume_Music", index = 0, groupNo = 0, ordinal = 0 }, new { name = "Volume_SFX", index = 1, groupNo = 0, ordinal = 1 }
            }
        }));
        check(PlaybackCategoryPresentation.Resolve(firstGroup, authored, [new WireEvent { kind = "category", objectId = "1:category-index:1", name = "Volume_SFX" }]) is { Name: "Volume_SFX" },
            "原生实例的运行时 Category 覆盖静态 Cue 配置");
        var projected = ClientTimelineProjection.Combine([request], [begin, part, Event("beat", 11, 115)]);
        check(projected.Count(e => e.kind is "acb-catalog" or "cue-catalog") == 2 && projected.All(e => e.kind != "beat") &&
              new CueCatalogIndex(projected, 30).Resolve(group) != null, "只有热接入锚点时可投影静态目录，回调仍要求真实原生锚点");
        check(projected.Single(e => e.kind == "cue-catalog").objectId == part.objectId && projected.Single(e => e.kind == "cue-catalog").parentId == part.parentId,
            "目录保留独立身份，不伪装成播放实例事件");
        check(part.originalTime == null && part.time == 11 && part.parentId == "acb:0xabc", "视图投影不修改磁盘原始目录事件");
        var nativeGap = Event("gap", 15, 150); nativeGap.channel = "native";
        check(new CueCatalogIndex([begin, part, nativeGap], 30).Resolve(group) != null,
            "原生播放数据缺口不撤销独立 SDK 通道仍在核实的静态目录");
        var laterAnchor = Event("metric", 150, 1500); laterAnchor.channel = "native";
        var pastDirectory = ClientTimelineProjection.Combine([request, nativeGap, laterAnchor], [begin, part]);
        check(new CueCatalogIndex(pastDirectory, 150).Resolve(group) != null,
            "两分钟窗口外保留的 SDK 目录可跨原生数据缺口投影，目录不因原生丢包消失");

        using var session = new Session(new WireEvent { kind = "hello", session = "sdk-catalog-fixture", clientId = Client, captureId = Capture, channel = "sdk" });
        session.Accept(firstGroup); session.Accept(begin); session.Accept(part);
        session.Accept(Event("metric", 250, 2500));
        var retained = session.ViewSnapshot();
        check(session.Snapshot().All(e => !CueCatalogIndex.IsMetadata(e)) && new CueCatalogIndex(retained, 250).Resolve(group) != null &&
              retained.Any(e => e.kind == "category-catalog"), "事件超出两分钟后，当前 ACB 和 ACF 配置单独保留");
        var recordingDirectory = Path.Combine(Path.GetTempPath(), "CriScope-catalog-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            session.StartAutomaticRecording(recordingDirectory, new AutomaticRecordingOptions(SegmentEvents: 1, MinimumFreeBytes: 0));
            session.Accept(Event("metric", 251, 2510)); session.Accept(Event("metric", 252, 2520)); session.StopRecording();
            var baseline = File.ReadLines(session.RecordingPaths[^1]).Select(WireEvent.Parse).Where(e => e.baseline).ToArray();
            check(baseline.Any(e => e.kind == "acb-catalog") && baseline.Any(e => e.kind == "cue-catalog") &&
                  new CueCatalogIndex(baseline, 252).Resolve(group) != null, "物理日志分段基线保留热接入 Cue 配置");
        }
        finally { foreach (var path in session.RecordingPaths) File.Delete(path); if (Directory.Exists(recordingDirectory)) Directory.Delete(recordingDirectory); }
        var retire = Bank(260, lifecycle: "released"); retire.seq = 2600; session.Accept(retire);
        session.Accept(Event("metric", 390, 3900));
        check(session.ViewSnapshot().All(e => e.kind != "cue-catalog") && new CueCatalogIndex(session.ViewSnapshot(), 390).Resolve(group) == null,
            "卸载清除内存目录及后续分段基线，不无限累积已卸载 Cue");
    }

    public static (Session Native, Session Sdk) UiFixture(bool includeCatalog = true)
    {
        var native = new Session(new WireEvent { kind = "hello", session = "native-catalog-fixture", clientId = Client, captureId = Capture, channel = "native", name = "目录验收", platform = "WindowsPlayer" });
        var sdk = new Session(new WireEvent { kind = "hello", session = "sdk-catalog-fixture", clientId = Client, captureId = Capture, channel = "sdk", name = "目录验收", platform = "WindowsPlayer" });
        var music = Request(5, name: "m_ablum_ev0_1_0_1");
        music.raw = JsonSerializer.Serialize(new { parameters = new[] {
            new { name = "CriAtomExAcbHn", value = "0xabc" }, new { name = "Acb Name", value = "music_ambience_shared_cue_sheet_very_long_resource_name" }
        } });
        native.Accept(music);
        var voice = Event("play", 5, 51); voice.session = native.Id; voice.channel = "native"; voice.baseline = true;
        voice.entity = "voice"; voice.objectId = "1:voice:1:9"; voice.parentId = music.objectId; voice.name = music.name; voice.detail = "热接入；起点未知";
        native.Accept(voice); var latest = Event("metric", 30, 300); latest.session = native.Id; latest.channel = "native"; native.Accept(latest);
        if (includeCatalog) SupplyCatalog(sdk);
        return (native, sdk);
    }

    public static void SupplyCatalog(Session sdk)
    {
        sdk.Accept(Event("category-catalog", 10, 99, JsonSerializer.Serialize(new {
            basis = "acf-category-catalog", firstGroupNo = 0,
            firstGroupCategories = new[] { new { name = "Volume_Music", index = 0, groupNo = 0, ordinal = 0 }, new { name = "Volume_SFX", index = 1, groupNo = 0, ordinal = 1 } }
        })));
        sdk.Accept(Bank(10)); sdk.Accept(Part(11, name: "m_ablum_ev0_1_0_1"));
    }
}
