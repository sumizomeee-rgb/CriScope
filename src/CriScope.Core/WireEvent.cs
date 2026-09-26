using System.Text.Json;
namespace CriScope.Core;

// 字段名与 Unity JsonUtility 协议保持一致。原始行保留用于证据和录制。
public sealed class WireEvent
{
    public string clientId { get; set; } = "";
    public string machine { get; set; } = "";
    public string captureId { get; set; } = "";
    public string channel { get; set; } = "";
    public int epoch { get; set; }
    public double observedTime { get; set; }
    public bool baseline { get; set; }
    // Receiver metadata is persisted with recordings. Source timestamps remain untouched.
    public DateTimeOffset? receivedAtUtc { get; set; }
    public double? timeOrigin { get; set; }
    public string timeOriginBasis { get; set; } = "";
    public double? clockAnchorTime { get; set; }
    public DateTimeOffset? clockAnchorUtc { get; set; }
    public string clockBasis { get; set; } = "";
    public double? originalTime { get; set; }
    public bool estimatedTime { get; set; }
    public string lifecycle { get; set; } = "";
    public string endReason { get; set; } = "";
    public string causeId { get; set; } = "";
    public string kind { get; set; } = "";
    public string source { get; set; } = "";
    public string parentId { get; set; } = "";
    public string entity { get; set; } = "";
    public string raw { get; set; } = "";
    public string session { get; set; } = "";
    public long seq { get; set; }
    public double time { get; set; }
    public string name { get; set; } = "";
    public string objectId { get; set; } = "";
    public int cue { get; set; }
    public double value { get; set; }
    public string detail { get; set; } = "";
    public double x { get; set; }
    public double y { get; set; }
    public double z { get; set; }
    public int pid { get; set; }
    public string platform { get; set; } = "";
    public string ToJson() => JsonSerializer.Serialize(this);
    public static WireEvent Parse(string json) => JsonSerializer.Deserialize<WireEvent>(json) ?? throw new InvalidDataException("空事件");
}
