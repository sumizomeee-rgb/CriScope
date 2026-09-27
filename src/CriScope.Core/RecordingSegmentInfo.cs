namespace CriScope.Core;

public sealed record RecordingSegmentInfo(
    string Path,
    long FirstIncrementalSequence,
    long LastIncrementalSequence,
    int BaselineCount,
    bool IsOpen,
    bool StartsNewConnection = false);
