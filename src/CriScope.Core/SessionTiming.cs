using System.Globalization;

namespace CriScope.Core;

public sealed partial class Session
{
    private readonly TimeProvider clock;
    private double? timeOrigin;
    private string timeOriginBasis = "";
    private double? clockAnchorTime;
    private DateTimeOffset? clockAnchorUtc;
    private DateTimeOffset? lastReceivedAtUtc;
    private long? lastReceivedTimestamp;
    private double? transportOrigin;
    private long transportTimestamp;
    private double lastTransportTime, minimumTransportOffset;
    private double? transportLagGrowthMilliseconds;
    private double? sourceObservationMinimum;
    private double? sourceObservationLagGrowthMilliseconds;
    private int sourceObservationEpoch;
    private int? receiverBufferedBytes;

    public bool HasTimeOrigin { get { lock(gate) return timeOrigin.HasValue; } }
    public double TimeOrigin { get { lock(gate) return timeOrigin ?? 0; } }
    public string TimeOriginBasis { get { lock(gate) return timeOriginBasis; } }
    public DateTimeOffset? LastReceivedAtUtc { get { lock(gate) return lastReceivedAtUtc; } }
    public double? ReceiveAgeMilliseconds
    {
        get { lock(gate) return IsReplay || !lastReceivedTimestamp.HasValue ? null :
            Math.Max(0,clock.GetElapsedTime(lastReceivedTimestamp.Value).TotalMilliseconds); }
    }
    // Difference of elapsed receiver/bridge clocks relative to the best observed transfer.
    // It can reveal growing buffering; it is NOT an absolute network or end-to-end latency.
    public double? TransportLagGrowthMilliseconds { get { lock(gate) return transportLagGrowthMilliseconds; } }
    // Relative native-clock-to-bridge-observation change within one native epoch.
    // Clock drift and packet scheduling also contribute; no absolute clock alignment is implied.
    public double? SourceObservationLagGrowthMilliseconds { get { lock(gate) return sourceObservationLagGrowthMilliseconds; } }
    // Pending bytes in this TCP socket only, not the CRI/game/desktop queue depth.
    public int? ReceiverBufferedBytes { get { lock(gate) return receiverBufferedBytes; } }

    internal void ObserveTransport(double observedSeconds, int bufferedBytes)
    {
        lock(gate)
        {
            var timestamp=clock.GetTimestamp();
            lastReceivedAtUtc=clock.GetUtcNow();lastReceivedTimestamp=timestamp;
            receiverBufferedBytes=Math.Max(0,bufferedBytes);
            if(!double.IsFinite(observedSeconds)) return;
            if(!transportOrigin.HasValue || observedSeconds<lastTransportTime)
            {
                transportOrigin=observedSeconds;transportTimestamp=timestamp;minimumTransportOffset=0;
            }
            lastTransportTime=observedSeconds;
            var offset=clock.GetElapsedTime(transportTimestamp,timestamp).TotalSeconds-(observedSeconds-transportOrigin.Value);
            minimumTransportOffset=Math.Min(minimumTransportOffset,offset);
            transportLagGrowthMilliseconds=Math.Max(0,(offset-minimumTransportOffset)*1000);
        }
    }

    private void RestoreClock(WireEvent hello)
    {
        if(hello.timeOrigin is { } origin && double.IsFinite(origin))
        {timeOrigin=origin;timeOriginBasis=hello.timeOriginBasis;}
        if(hello.clockAnchorTime is { } anchor && double.IsFinite(anchor) && hello.clockAnchorUtc.HasValue)
        {clockAnchorTime=anchor;clockAnchorUtc=hello.clockAnchorUtc;}
    }

    private void UpdateTiming(WireEvent e)
    {
        if(!IsReplay)
        {
            e.receivedAtUtc=clock.GetUtcNow();lastReceivedAtUtc=e.receivedAtUtc;lastReceivedTimestamp=clock.GetTimestamp();
        }
        // Connection control messages have no source clock. Baselines can predate this capture.
        bool timed=double.IsFinite(e.time) && e.time>=0 && (e.time>0 || e.entity=="capture-segment") && e.kind is not ("gap" or "error");
        if(!timeOrigin.HasValue && timed && (!e.baseline || IsReplay))
        {
            timeOrigin=e.time;
            timeOriginBasis=IsReplay ? "recording-first-available" : e.entity=="capture-segment" ? "capture-start" : "first-observed-event";
        }
        if(!clockAnchorTime.HasValue && timed && !e.baseline && !e.detail.Contains("起点未知",StringComparison.Ordinal) && e.receivedAtUtc.HasValue)
        {
            clockAnchorTime=e.time;clockAnchorUtc=e.receivedAtUtc;
        }
        if(!IsReplay && timed && !e.baseline && e.channel=="native" && e.epoch>0 && e.observedTime>0 && e.time>=LastTime &&
            !e.detail.Contains("起点未知",StringComparison.Ordinal))
        {
            double difference=e.observedTime-e.time;
            if(!sourceObservationMinimum.HasValue || sourceObservationEpoch!=e.epoch)
            {sourceObservationMinimum=difference;sourceObservationEpoch=e.epoch;}
            sourceObservationMinimum=Math.Min(sourceObservationMinimum.Value,difference);
            sourceObservationLagGrowthMilliseconds=Math.Max(0,(difference-sourceObservationMinimum.Value)*1000);
        }
    }

    public DateTimeOffset? EstimateWallTime(WireEvent e)
    {
        lock(gate)
        {
            if(!clockAnchorTime.HasValue || !clockAnchorUtc.HasValue || !double.IsFinite(e.time)) return null;
            try {return clockAnchorUtc.Value.AddSeconds(e.time-clockAnchorTime.Value);}
            catch(ArgumentOutOfRangeException) {return null;}
        }
    }

    public string FormatWallTime(WireEvent e) => EstimateWallTime(e) is { } value ?
        "约 "+value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",CultureInfo.InvariantCulture)+"（接收锚点估算）" : "无墙钟锚点，无法换算";

    internal WireEvent RecordingHeader(string detail)
    {
        lock(gate)
            return new WireEvent {kind="hello",value=1,session=Id,name=Name,pid=Pid,platform=Platform,source=Source,
                clientId=ClientId,machine=Machine,captureId=CaptureId,channel=Channel,detail=detail,
                timeOrigin=timeOrigin,timeOriginBasis=timeOriginBasis,clockAnchorTime=clockAnchorTime,clockAnchorUtc=clockAnchorUtc,
                clockBasis=clockAnchorUtc.HasValue?"receive-anchor-estimate":"unavailable"};
    }
}
