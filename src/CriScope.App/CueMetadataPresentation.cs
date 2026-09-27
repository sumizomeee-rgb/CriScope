using System.Text.Json;
using CriScope.Core;
namespace CriScope.App;

public static class CueMetadataPresentation
{
    // Native ACB name is resource identity, not necessarily the engine's registered alias.
    public static string AcbName(WireEvent? request)
    {
        if(string.IsNullOrWhiteSpace(request?.raw))return "";
        try {
            using var doc=JsonDocument.Parse(request.raw);
            if(!doc.RootElement.TryGetProperty("parameters",out var parameters)||parameters.ValueKind!=JsonValueKind.Array)return "";
            foreach(var p in parameters.EnumerateArray())
                if(p.TryGetProperty("name",out var name)&&name.GetString()=="Acb Name"&&p.TryGetProperty("value",out var value)&&value.ValueKind==JsonValueKind.String)
                    return value.GetString()??"";
        } catch(JsonException) { }
        return "";
    }
}
