using System.Globalization;
using System.Text.Json;

namespace CriScope.Core;

/// <summary>Lifecycle evidence shared by live views, replay and exports. Numeric CRI reasons remain raw.</summary>
public static class EventSemantics
{
    public static bool IsPlaybackReleased(WireEvent e) => e.entity == "cue" &&
        (e.lifecycle == "released" || string.IsNullOrEmpty(e.lifecycle) &&
            (NativeFunction(e) == "ExPlaybackInfo_FreeInfo" || e.detail?.Contains("播放实例释放", StringComparison.Ordinal) == true));

    public static bool IsPlaybackEnd(WireEvent e) => IsPlaybackReleased(e) ||
        e.entity == "cue" && (e.lifecycle == "stopped" || e.kind == "stop") ||
        e.kind == "log" && e.name == "ExCue_StopByLimit" && EndReason(e) == "playback-limit";

    public static string EndReason(WireEvent e) => !string.IsNullOrEmpty(e.endReason) ? e.endReason :
        string.IsNullOrEmpty(e.lifecycle) && e.name == "ExCue_StopByLimit" && NativeFunction(e) == "ExCue_StopByLimit" ? "playback-limit" : "";

    public static string CauseId(WireEvent e)
    {
        if (!string.IsNullOrEmpty(e.causeId)) return e.causeId;
        if (EndReason(e) != "playback-limit" || string.IsNullOrEmpty(e.objectId)) return "";
        try
        {
            using var doc = JsonDocument.Parse(e.raw);
            if (!doc.RootElement.TryGetProperty("parameters", out var parameters)) return "";
            string? unique = null, legacy = null;
            foreach (var p in parameters.EnumerateArray())
            {
                if (!p.TryGetProperty("name", out var name) || !p.TryGetProperty("value", out var value)) continue;
                if (name.GetString() == "cause ExPlaybackId_unique64") unique = value.ToString();
                if (name.GetString() == "cause CriAtomExPlaybackId") legacy = value.ToString();
            }
            string marker = unique != null ? "playback:" : "playback-id:";
            string? id = unique ?? legacy;
            if (id == null || !ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return "";
            int markerIndex = e.objectId.IndexOf("playback", StringComparison.Ordinal);
            int lastColon = e.objectId.LastIndexOf(':');
            // Keep the bridge epoch and mapper segment from this same recorded event.
            if (markerIndex < 0 || lastColon < markerIndex) return "";
            var segments = e.objectId[(e.objectId.IndexOf(':', markerIndex) + 1)..lastColon];
            return e.objectId[..markerIndex] + marker + segments + ":" + id;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return ""; }
    }

    static string NativeFunction(WireEvent e)
    {
        if (string.IsNullOrEmpty(e.raw)) return "";
        try
        {
            using var doc = JsonDocument.Parse(e.raw);
            return doc.RootElement.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.String
                ? function.GetString() ?? "" : "";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return ""; }
    }
}
