using System.Text.Json;
using CriScope.Core;

namespace CriScope.App;

public sealed record PrimaryGroupCategory(int Ordinal, int Index, string Name);

/// <summary>Colors a playback only when the ACF's first populated Category Group is known.</summary>
public static class PlaybackCategoryPresentation
{
    public static PrimaryGroupCategory? Resolve(WireEvent? catalog, WireEvent? cueInfo,
        IReadOnlyList<WireEvent> runtimeCategories)
    {
        var catalogMembers = ReadMembers(catalog, "acf-category-catalog", "firstGroupCategories");
        var cueMembers = ReadMembers(cueInfo, "cue-config", "categoryDetails");

        // Runtime membership wins over a Cue's static configuration. A playback can
        // acquire different Categories; an unrecognized override must stay neutral.
        if (runtimeCategories.Count > 0)
        {
            // Use the full catalog for runtime overrides when it agrees with this
            // playback's CueInfo. If ACF changed first, trust the newer CueInfo.
            var possible = catalogMembers;
            if (cueMembers is { Length: > 0 } &&
                (possible is null || cueMembers.Any(member => !possible.Contains(member))))
                possible = cueMembers;
            if (possible is null) return null;
            var matches = possible.Where(member => runtimeCategories.Any(e => MatchesCategory(e, member)))
                .DistinctBy(member => member.Index).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        // The SDK CueInfo is explicitly associated with this playback by the clock
        // projection. Without native Category observations it remains a config label.
        return cueMembers is { Length: 1 } ? cueMembers[0] : null;
    }

    private static bool MatchesCategory(WireEvent observed, PrimaryGroupCategory member)
    {
        const string marker = "category-index:";
        var id = observed.objectId ?? "";
        var markerAt = id.IndexOf(marker, StringComparison.Ordinal);
        if (markerAt >= 0)
        {
            // The receiver prefixes native ids with an epoch, for example
            // "2:category-index:9". A valid index is stronger evidence than a name.
            var prefix = id[..markerAt];
            if (prefix.Length == 0 || (prefix.EndsWith(':') &&
                long.TryParse(prefix.AsSpan(0, prefix.Length - 1), out _)))
            {
                return int.TryParse(id.AsSpan(markerAt + marker.Length), out var index) && index == member.Index;
            }
        }
        return observed.name == member.Name;
    }

    private static PrimaryGroupCategory[]? ReadMembers(WireEvent? evidence, string basis, string property)
    {
        if (evidence is null || string.IsNullOrEmpty(evidence.raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(evidence.raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("basis", out var observedBasis) || observedBasis.GetString() != basis ||
                !root.TryGetProperty("firstGroupNo", out var firstGroup) || !firstGroup.TryGetInt32(out var groupNo) || groupNo < 0 ||
                !root.TryGetProperty(property, out var entries) || entries.ValueKind != JsonValueKind.Array) return null;
            var result = new List<PrimaryGroupCategory>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("groupNo", out var entryGroup) || !entryGroup.TryGetInt32(out var number) || number != groupNo ||
                    !entry.TryGetProperty("index", out var rawIndex) || !rawIndex.TryGetInt32(out var index) || index < 0 ||
                    !entry.TryGetProperty("ordinal", out var rawOrdinal) || !rawOrdinal.TryGetInt32(out var ordinal) || ordinal < 0 ||
                    !entry.TryGetProperty("name", out var rawName) || rawName.GetString() is not { Length: > 0 } name) continue;
                result.Add(new PrimaryGroupCategory(ordinal, index, name));
            }
            return result.ToArray();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }
}
