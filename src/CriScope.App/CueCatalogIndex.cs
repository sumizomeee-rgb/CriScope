using System.Globalization;
using System.Text.Json;
using CriScope.Core;

namespace CriScope.App;

/// <summary>Authored Cue configuration, scoped to one connection and one loaded ACB generation.</summary>
public sealed class CueCatalogIndex
{
    private sealed class Directory(WireEvent begin, string generation, double from)
    {
        public WireEvent Begin { get; } = begin;
        public string Generation { get; } = generation;
        public double From { get; } = from;
        public double Until { get; set; } = double.PositiveInfinity;
        public Dictionary<string, WireEvent> Cues { get; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<(string Client, string Capture, string Handle), List<Directory>> directories = [];
    public static bool IsMetadata(WireEvent e) => e.kind is "category-catalog" or "acb-catalog" or "cue-catalog";

    public CueCatalogIndex(IEnumerable<WireEvent> events, double through)
    {
        foreach (var e in events.Where(e => e.time <= through && (IsMetadata(e) || e.kind == "gap" || e.entity == "capture-segment"))
                     .OrderBy(e => e.time).ThenBy(e => Math.Abs(e.seq)))
        {
            if ((e.kind == "gap" || e.entity == "capture-segment") && e.channel == "sdk")
            {
                foreach (var key in directories.Keys.Where(k => k.Client == e.clientId && k.Capture == e.captureId).ToArray())
                    directories.Remove(key);
                continue;
            }
            if (e.kind == "category-catalog" || string.IsNullOrWhiteSpace(e.raw)) continue;
            try
            {
                using var doc = JsonDocument.Parse(e.raw);
                var root = doc.RootElement;
                if (!root.TryGetProperty("acbHandle", out var rawHandle) || NormalizeHandle(rawHandle.ToString()) is not { } handle ||
                    !root.TryGetProperty("generation", out var rawGeneration) || rawGeneration.GetString() is not { Length: > 0 } generation) continue;
                var key = (e.clientId, e.captureId, handle);
                if (e.kind == "acb-catalog")
                {
                    if (!directories.TryGetValue(key, out var versions)) directories[key] = versions = [];
                    if (e.lifecycle == "released")
                    {
                        var ended = versions.LastOrDefault(d => d.Generation == generation);
                        if (ended != null) ended.Until = Math.Min(ended.Until, e.time);
                    }
                    else if (e.lifecycle == "loaded" && versions.All(d => d.Generation != generation))
                    {
                        // Polling bounds the load observation. The initial connection
                        // snapshot also covers playbacks already present when we joined.
                        var initial = root.TryGetProperty("connectionSnapshot", out var snapshot) && snapshot.ValueKind == JsonValueKind.True;
                        var from = initial ? double.NegativeInfinity : e.time;
                        if (!initial && root.TryGetProperty("observedFrom", out var rawFrom) && rawFrom.TryGetDouble(out var since) &&
                            double.IsFinite(since) && since <= (e.originalTime ?? e.time))
                            from = e.time - ((e.originalTime ?? e.time) - since);
                        if (versions.LastOrDefault() is { } previous) previous.Until = Math.Min(previous.Until, from);
                        versions.Add(new Directory(e, generation, from));
                    }
                }
                else if (root.TryGetProperty("basis", out var basis) && basis.GetString() == "acb-cue-catalog" &&
                         root.TryGetProperty("cues", out var cues) && cues.ValueKind == JsonValueKind.Array &&
                         directories.TryGetValue(key, out var versions) && versions.LastOrDefault(d => d.Generation == generation) is { } directory)
                {
                    foreach (var cue in cues.EnumerateArray())
                    {
                        if (!cue.TryGetProperty("name", out var rawName) || rawName.GetString() is not { Length: > 0 } name ||
                            !cue.TryGetProperty("length", out var rawLength) || !rawLength.TryGetDouble(out var length) || !double.IsFinite(length)) continue;
                        directory.Cues[name] = new WireEvent {
                            kind = "cue-info", name = name, value = length, raw = cue.GetRawText(),
                            time = e.time, originalTime = e.originalTime, estimatedTime = e.estimatedTime,
                            session = e.session, clientId = e.clientId, captureId = e.captureId, channel = e.channel,
                            detail = "SDK 已加载 ACB 的 Cue 配置；不是播放实例的运行时覆盖分类"
                        };
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        }
    }

    public WireEvent? Resolve(PlaybackGroup playback)
    {
        if (playback.Request is not { } request || RequestHandle(request) is not { } handle ||
            !directories.TryGetValue((request.clientId, request.captureId, handle), out var versions)) return null;
        var matches = versions.Where(d => request.time >= d.From && request.time < d.Until).ToArray();
        return matches.Length == 1 ? matches[0].Cues.GetValueOrDefault(playback.Name) : null;
    }

    public static string? RequestHandle(WireEvent request)
    {
        try
        {
            using var doc = JsonDocument.Parse(request.raw);
            if (doc.RootElement.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array)
                foreach (var field in parameters.EnumerateArray())
                    if (field.TryGetProperty("name", out var name) && name.GetString() == "CriAtomExAcbHn" && field.TryGetProperty("value", out var value))
                        return NormalizeHandle(value.ToString());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return null;
    }

    private static string? NormalizeHandle(string value)
    {
        var text = value.Trim();
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex) text = text[2..];
        return ulong.TryParse(text, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle) && handle != 0
            ? "0x" + handle.ToString("x", CultureInfo.InvariantCulture) : null;
    }
}
