using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CriScope.Core;

public static class ProblemBundle
{
    public static string Export(Collector collector, Session selected, string directory, double from, double to, byte[] screenshot, string description = "")
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || to < from) throw new ArgumentException("日志范围无效");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"CriScope-problem-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        var sources = collector.Sessions.Where(s => ReferenceEquals(s, selected) ||
            selected.ClientId.Length > 0 && s.ClientId == selected.ClientId && s.CaptureId == selected.CaptureId && s.IsReplay == selected.IsReplay).ToArray();
        var manifest = new List<object>();
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var source in sources)
        {
            var evidence = source.EvidenceSnapshot();
            var all = evidence.Events;
            // Companion channel clocks are independent. Do not apply the selected channel's time range.
            bool primary = ReferenceEquals(source, selected);
            double start = primary ? from : all.FirstOrDefault()?.time ?? 0;
            double end = primary ? to : source.LastTime;
            bool contextCrossesUnrecordedGap=evidence.HasUnrecordedGap && start>evidence.RecordedThroughTime;
            var context = Reconstruct(all.Where(e => (e.time < start || e.baseline && e.time <= start) &&
                (!contextCrossesUnrecordedGap || e.seq>=evidence.ContextAfterSequence)));
            var interval = all.Where(e => !e.baseline && e.time >= start && e.time <= end).ToArray();
            string channelName = source.Channel is "native" or "sdk" ? source.Channel : "events";
            string fileId = Guid.TryParse(source.Id,out var safeId) ? safeId.ToString("N") : Guid.NewGuid().ToString("N");
            string name = $"{channelName}-{fileId}.criscope";
            using (var writer = Writer(archive, name))
            {
                writer.WriteLine(source.RecordingHeader("Problem bundle; baseline is explicitly marked; independent source clock").ToJson());
                foreach (var ev in context.Concat(interval).OrderBy(e => e.seq)) writer.WriteLine(ev.ToJson());
            }
            bool lost = all.Any(e => e.kind == "gap" && e.time <= end) || evidence.HasUnrecordedGap && end>evidence.RecordedThroughTime;
            bool availableFromStart = evidence.FromRecording && all.Any(e => e.baseline) && all.Where(e=>!e.baseline).FirstOrDefault()?.time <= start ||
                !evidence.FromRecording && source.Evicted == 0 && start >= (all.FirstOrDefault()?.time ?? 0);
            manifest.Add(new { file = name, session = source.Id, source = source.Source, source.ClientId, source.CaptureId,
                source.Channel, source.Pid, source.Machine, from = start, to = end, selectedChannel = primary,
                clock = "source-native; companion time range is not synchronized", evidence = evidence.FromRecording ? "frozen-recorded-prefix-and-available-memory-window" : "available-memory-window",
                evidence.HasUnrecordedGap, evidence.RecordedThroughSequence, evidence.RecordedThroughTime,
                contextAfterSequence=evidence.ContextAfterSequence,
                missingSequences=evidence.HasUnrecordedGap ? new { after=evidence.RecordedThroughSequence, before=evidence.ContextAfterSequence } : null,
                baselineEntries = context.Length, eventCount = interval.Length, knownGap = lost, source.Evicted,
                source.ViewStatesEvicted, startState = availableFromStart && !lost ? "observed fields reconstructed; absent fields remain unknown" : "incomplete or not reconstructable; no guessed state" });
        }
        using (var writer = Writer(archive, "manifest.json")) writer.Write(JsonSerializer.Serialize(new {
            format = "CriScope problem bundle/1", exportedAtUtc = DateTime.UtcNow, description,
            screenshot = "screenshot.png shows the UI at export time, not a reconstructed historical screenshot",
            requestedRange = new { session = selected.Id, from, to }, sources = manifest
        }, new JsonSerializerOptions { WriteIndented = true }));
        using (var output = archive.CreateEntry("screenshot.png").Open()) output.Write(screenshot);
        using (var writer = Writer(archive, "README.txt")) writer.Write("CriScope 问题包\n在软件中使用“打开日志”查看各 .criscope 文件。\nmanifest.json 标注来源、独立时钟、实际可用范围与缺失。\nscreenshot.png 是导出时界面，不冒充历史截图。\n记录不包含声音。\n");
        return path;
    }

    static StreamWriter Writer(ZipArchive archive, string name) => new(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
    static WireEvent[] Reconstruct(IEnumerable<WireEvent> events)
    {
        var state = new Dictionary<string, WireEvent>();
        foreach (var e in events.OrderBy(e => e.seq))
        {
            if (e.kind == "gap" || e.entity == "capture-segment") { state.Clear(); continue; }
            if(EventSemantics.IsPlaybackEnd(e))
            { state.Remove("request|"+e.objectId); continue; }
            if (e.kind == "stop") { state.Remove("play|" + e.objectId); continue; }
            if(e.kind=="selector" && e.detail.Contains("清除全部"))
                foreach(var old in state.Keys.Where(k=>k.StartsWith("selector|"+e.objectId+"|",StringComparison.Ordinal)).ToArray()) state.Remove(old);
            string? key = e.kind switch {
                "play" or "request" or "bus" => e.kind + "|" + e.objectId,
                "position" or "remove" => "position|" + e.entity + "|" + e.objectId,
                "aisac" or "selector" or "metric" => e.kind + "|" + e.objectId + "|" + e.name,
                _ => null };
            if (key != null) state[key] = e;
        }
        return state.Values.OrderBy(e => e.seq).Select(e => { var copy = WireEvent.Parse(e.ToJson()); copy.baseline = true; return copy; }).ToArray();
    }
}
