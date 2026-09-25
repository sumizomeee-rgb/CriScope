using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using CriScope.Core;

using var reserve = new TcpListener(IPAddress.Loopback, 0);
reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
using var collector = new Collector(Path.Combine(Path.GetTempPath(), "CriScope-AgentTests"));
using var server = new QueryServer(collector, port);
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ0kAAAAASUVORK5CYII=");
string workspace = "播放", filter = "";
int captures = 0;
server.ReadUiState = () => Task.FromResult<object>(new { workspace, filter });
server.CaptureScreenshot = panel => { if (panel is not (null or "timeline")) throw new ArgumentException("unknown panel"); captures++; return Task.FromResult(png); };
server.ChangeUiState = (action, value) => { if (action == "workspace") workspace = value ?? workspace; if (action == "filter") filter = value ?? ""; return Task.FromResult<object>(new { workspace, filter }); };
server.Start();
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
using (var screenshot = await http.GetAsync("screenshot?panel=timeline"))
    Check(screenshot.Content.Headers.ContentType?.MediaType == "image/png" && (await screenshot.Content.ReadAsByteArrayAsync()).SequenceEqual(png) && captures == 1,
        "截图端点按原始 PNG 返回，面板参数送达提供者");
using (var action = await http.PostAsJsonAsync("ui/action", new { action = "workspace", value = "资源" }))
    Check(action.IsSuccessStatusCode && workspace == "资源", "Agent工作区操作经统一委托执行");
using (var state = JsonDocument.Parse(await http.GetStringAsync("ui/state")))
    Check(state.RootElement.GetProperty("workspace").GetString() == "资源", "读取真实变更后的UI状态");
using (var request = new HttpRequestMessage(HttpMethod.Post, "ui/action") { Content = JsonContent.Create(new { action = "filter", value = "untrusted" }) })
{
    request.Headers.Add("Origin", "https://example.invalid"); using var denied = await http.SendAsync(request);
    Check(denied.StatusCode == HttpStatusCode.Forbidden && filter == "", "跨站请求无法操作UI");
}
using (var request = new HttpRequestMessage(HttpMethod.Get, "screenshot"))
{
    request.Headers.Add("Sec-Fetch-Site", "cross-site"); using var denied = await http.SendAsync(request);
    Check(denied.StatusCode == HttpStatusCode.Forbidden && captures == 1, "跨站请求无法读取截图");
}
using (var bad = await http.PostAsJsonAsync("ui/action", new { action = "execute", value = "anything" }))
    Check(bad.StatusCode == HttpStatusCode.BadRequest, "未知动作拒绝执行");
using (var big = await http.PostAsync("ui/action", new StringContent(new string('x', 20000))))
    Check(big.StatusCode == HttpStatusCode.BadRequest, "超限请求体有界拒绝");
using (var bad = await http.GetAsync("screenshot?panel=unknown"))
    Check(bad.StatusCode == HttpStatusCode.BadRequest, "截图失败有明确错误而非伪PNG");
Check((await http.GetStringAsync("sessions")).Trim() == "[]", "无连接时不会凭空创建采集会话");
Console.WriteLine($"{passed}/{passed} Agent API checks passed");
