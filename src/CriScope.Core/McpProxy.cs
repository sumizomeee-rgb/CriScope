using System.Text;
using System.Text.Json;
namespace CriScope.Core;

public static class McpProxy
{
    public static async Task Run()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:18962/"), Timeout = TimeSpan.FromSeconds(20) };
        string? line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            JsonElement id = default;
            try
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                if (!root.TryGetProperty("id", out id)) continue;
                id = id.Clone(); var method = root.GetProperty("method").GetString(); object result;
                if (method == "initialize") result = new { protocolVersion = "2024-11-05", capabilities = new { tools = new { } }, serverInfo = new { name = "criscope", version = "0.2.0" } };
                else if (method == "ping") result = new { };
                else if (method == "tools/list") result = new { tools = new object[] {
                    Tool("list_sessions", "列出采集会话、来源与连接状态", new { }),
                    new { name="query_events", description="按唯一会话、时间范围与水位查询证据；after分页", inputSchema=QuerySchema() },
                    new { name="summarize_window", description="汇总指定会话的时间窗口，标注数据来源和保留边界", inputSchema=QuerySchema() },
                    Tool("get_ui_state", "读取桌面工作区、选择、实时跟随和时间范围", new { }),
                    Tool("capture_screenshot", "从真实运行界面导出PNG；不依赖OS截图，不关闭窗口。panel可省略或为timeline", new { panel=new { type="string" } }),
                    Tool("control_ui", "操作与人工共享的界面状态。action: workspace/live/filter/select/session/range/theme/diagnostics；range的value为起点:终点，select为事件seq", new { action=new { type="string" }, value=new { type="string" } }, new[]{"action"}),
                    Tool("connect_native", "连接指定CRI Monitor端口；开始读取后续原生日志，不初始化游戏内Monitor", new { host=new { type="string" }, port=new { type="integer" } }, new[]{"host"}),
                    Tool("disconnect_native", "停止指定会话的原生日志采集；不卸载游戏的Monitor", new { session=new { type="string" } }, new[]{"session"}) } };
                else if (method == "tools/call")
                {
                    var p = root.GetProperty("params"); var name = p.GetProperty("name").GetString();
                    var args = p.TryGetProperty("arguments", out var a) ? a : JsonSerializer.SerializeToElement(new { });
                    string endpoint = name switch { "list_sessions" => "sessions", "query_events" => "events", "summarize_window" => "summary",
                        "get_ui_state" => "ui/state", "capture_screenshot" => "screenshot", "control_ui" => "ui/action",
                        "connect_native" => "native/connect", "disconnect_native" => "native/disconnect", _ => throw new ArgumentException("未知工具") };
                    bool post = name is "control_ui" or "connect_native" or "disconnect_native";
                    if (name is "query_events" or "summarize_window")
                    {
                        if (!args.TryGetProperty("session", out _)) throw new ArgumentException("必须指定session");
                        endpoint += "?" + string.Join("&", args.EnumerateObject().Where(x => new[] { "session", "from", "to", "kind", "watermark", "after", "limit" }.Contains(x.Name))
                            .Select(x => Uri.EscapeDataString(x.Name) + "=" + Uri.EscapeDataString(x.Value.ToString())));
                    }
                    if (name == "capture_screenshot" && args.TryGetProperty("panel", out var panel)) endpoint += "?panel=" + Uri.EscapeDataString(panel.ToString());
                    using var response = post ? await http.PostAsync(endpoint, new StringContent(args.GetRawText(), Encoding.UTF8, "application/json")) : await http.GetAsync(endpoint);
                    if (name == "capture_screenshot" && response.IsSuccessStatusCode)
                        result = new { content = new object[] { new { type = "image", mimeType = "image/png", data = Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync()) } }, isError = false };
                    else result = new { content = new[] { new { type = "text", text = await response.Content.ReadAsStringAsync() } }, isError = !response.IsSuccessStatusCode };
                }
                else { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = -32601, message = "未知方法" } })); continue; }
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
            }
            catch (Exception e)
            { if (id.ValueKind != JsonValueKind.Undefined) await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = -32603, message = e.Message } })); }
        }
    }
    static object Tool(string name, string description, object properties, string[]? required = null) =>
        new { name, description, inputSchema = new { type = "object", properties, required = required ?? [] } };
    static object QuerySchema() => new { type = "object", properties = new { session = new { type = "string" }, from = new { type = "number" }, to = new { type = "number" },
        kind = new { type = "string" }, watermark = new { type = "integer" }, after = new { type = "integer" }, limit = new { type = "integer" } }, required = new[] { "session" } };
}
