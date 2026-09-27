namespace CriScope.Core;

public sealed record AutomaticRecordingOptions(
    long SegmentBytes = Session.AutoSegmentBytes,
    int SegmentEvents = Session.AutoSegmentEvents,
    long MaxSessionBytes = Session.MaxAutomaticBytesPerSession,
    long MinimumFreeBytes = Session.MinimumFreeSpaceBytes)
{
    internal void Validate()
    {
        if(SegmentBytes<1 || SegmentEvents<1 || MaxSessionBytes<1 || MinimumFreeBytes<0)
            throw new ArgumentOutOfRangeException(nameof(AutomaticRecordingOptions),"自动录制阈值必须为有效正数");
    }
}
