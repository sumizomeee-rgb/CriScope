using System.Text.Json;
namespace CriScope.Core;

// 字段名与 Unity JsonUtility 协议保持一致。原始行保留用于证据和录制。
public sealed class WireEvent
{
    public string kind { get; set; } = "";
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
