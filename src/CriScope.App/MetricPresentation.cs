using System.Globalization;
using CriScope.Core;

namespace CriScope.App;

/// <summary>Display names and units only; preserves the underlying source values.</summary>
public static class MetricPresentation
{
    public static bool IsStreamingPoolUsed(WireEvent e) => e.name == "voices.streaming.used" || e.objectId == "voices.streaming.used";
    public static bool IsStreamingPoolCapacity(WireEvent e) => e.name == "voices.streaming.capacity" || e.objectId == "voices.streaming.capacity";
    public static string Name(WireEvent e)
    {
        var key = (e.name + " " + e.objectId).ToLowerInvariant();
        if (key.Contains("memory") && key.Contains("atom")) return "Atom 内存";
        if (key.Contains("memory") && key.Contains("fs")) return "文件系统内存";
        if (IsStreamingPoolUsed(e)) return "Streaming 声池";
        if (IsStreamingPoolCapacity(e)) return "Streaming 声池容量";
        if (e.objectId == "stream.used") return "流式播放声部（原生）";
        return e.name;
    }
    public static string Value(WireEvent e, double? value = null)
    {
        double number = value ?? e.value;
        if (!double.IsFinite(number)) return "未提供";
        var key = (e.name + " " + e.objectId).ToLowerInvariant();
        string F(double n, string pattern = "0.###") => n.ToString(pattern, CultureInfo.InvariantCulture);
        if (key.Contains("memory")) return F(number / 1048576, "0.00") + " MiB";
        if (e.objectId == "stream.bps" || e.detail == "bit/s")
            return number >= 1_000_000 ? F(number / 1_000_000) + " Mbit/s" : number >= 1000 ? F(number / 1000) + " kbit/s" : F(number) + " bit/s";
        if (key.Contains("cpu") || e.detail == "%") return F(number) + " %";
        if (e.detail is "µs" or "μs" or "us" || key.Contains("servertime") || key.EndsWith(".us")) return F(number) + " µs";
        if (key.Contains(".ms") || e.detail == "ms") return F(number) + " ms";
        if (e.detail is "LKFS" or "LUFS" or "dB" or "dBFS") return F(number) + " " + e.detail;
        if (e.detail == "个") return F(number) + " 个";
        return F(number);
    }
    public static string StreamingPool(WireEvent used, WireEvent? capacity) =>
        used.value.ToString("0", CultureInfo.InvariantCulture) + " / " +
        (capacity == null ? "未提供" : capacity.value.ToString("0", CultureInfo.InvariantCulture));
}
