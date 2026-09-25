using System.Text.Json;
using CriScope.Core;

namespace CriScope.App;

public sealed record MeterChannel(string Label, double Peak, double Rms);
public sealed record BusMeter(WireEvent Event, MeterChannel[] Channels)
{
    // These are maxima of individual returned slots, not a reconstructed mixed signal.
    public double MaxPeak => Channels.Length==0 ? 0 : Channels.Max(ch=>ch.Peak);
    public double MaxRms => Channels.Length==0 ? 0 : Channels.Max(ch=>ch.Rms);
}

public static class MixingPresentation
{
    public static BusMeter[] Latest(IEnumerable<WireEvent> events, double end) => events
        .Where(e=>e.kind=="bus"&&e.time<=end)
        .GroupBy(e=>e.objectId)
        .Select(g=>g.OrderBy(e=>e.time).ThenBy(e=>e.seq).Last())
        .Select(e=>new BusMeter(e,ReadChannels(e.raw)))
        .ToArray();

    private static MeterChannel[] ReadChannels(string raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return [];
        try
        {
            using var doc=JsonDocument.Parse(raw);
            if(!doc.RootElement.TryGetProperty("channels",out var channels)||channels.ValueKind!=JsonValueKind.Array)return [];
            return channels.EnumerateArray().Select(ch=>new MeterChannel(
                "CH "+ch.GetProperty("channel"),ch.GetProperty("peak").GetDouble(),ch.GetProperty("rms").GetDouble()))
                .Where(ch=>double.IsFinite(ch.Peak)&&double.IsFinite(ch.Rms)).ToArray();
        }
        catch(JsonException){return [];}
        catch(InvalidOperationException){return [];}
        catch(KeyNotFoundException){return [];}
    }
}
