using CriScope.Core;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private WireEvent[]? _cueCatalogSnapshot;
    private CueCatalogIndex _cueCatalogIndex = new([], 0);

    private WireEvent? CueConfigurationFor(PlaybackGroup playback)
    {
        var associated = _snapshot.LastOrDefault(e => e.kind == "cue-info" && e.name == playback.Name &&
            (e.parentId == playback.Id || e.objectId == playback.Id));
        if (associated != null) return associated;
        if (!ReferenceEquals(_cueCatalogSnapshot, _snapshot))
        {
            _cueCatalogSnapshot = _snapshot;
            // Configuration can be read after the playback observation, including
            // while the ruler is frozen. Generation boundaries still select the ACB
            // that belonged to this request; runtime status keeps its selected time.
            _cueCatalogIndex = new CueCatalogIndex(_snapshot, double.PositiveInfinity);
        }
        return _cueCatalogIndex.Resolve(playback);
    }
}
