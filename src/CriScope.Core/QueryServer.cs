using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
namespace CriScope.Core;

// Loopback only. Agent endpoints operate the same UI state as human input.
public sealed class QueryServer : IDisposable
{
    readonly Collector collector;
    readonly HttpListener listener = new();
    public Func<string?, Task<byte[]>>? CaptureScreenshot { get; set; }
    public Func<Task<object>>? ReadUiState { get; set; }
    public Func<string, string?, Task<object>>? ChangeUiState { get; set; }
    public QueryServer(Collector c, int port = 18962)
    { collector = c; listener.Prefixes.Add($"http://127.0.0.1:{port}/"); }
    public void Start() { listener.Start(); _ = Loop(); }
    async Task Loop()
    {
        while (listener.IsListening)
        {
            try { var context = await listener.GetContextAsync(); _ = Respond(context); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }
    async Task Respond(HttpListenerContext ctx)
    {
        try
        {
            if (ctx.Request.Headers["Origin"] != null || ctx.Request.Headers["Sec-Fetch-Site"] is "cross-site" or "same-site")
            { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var path = ctx.Request.Url!.AbsolutePath;
            var method = ctx.Request.HttpMethod;
            object result;
            if (method == "GET" && path == "/screenshot")
            {
                if (CaptureScreenshot is null) throw new InvalidOperationException("当前为无界面采集器，截图不可用");
                var image = await CaptureScreenshot(ctx.Request.QueryString["panel"]);
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = image.Length;
                await ctx.Response.OutputStream.WriteAsync(image);
                return;
            }
            if (method == "GET" && path == "/ui/state")
                result = ReadUiState is null ? throw new InvalidOperationException("桌面界面尚未就绪") : await ReadUiState();
            else if (method == "POST" && path == "/ui/action")
            {
                using var body = await Body(ctx.Request);
                var action = body.RootElement.GetProperty("action").GetString() ?? "";
                if (!new[] { "workspace", "live", "filter", "select", "session", "range", "theme", "diagnostics" }.Contains(action))
                    throw new ArgumentException("不支持的界面操作");
                string? value = body.RootElement.TryGetProperty("value", out var v) ? v.ToString() : null;
                result = ChangeUiState is null ? throw new InvalidOperationException("桌面界面尚未就绪") : await ChangeUiState(action, value);
            }
            else if (method == "POST" && path == "/native/connect")
            {
                using var body = await Body(ctx.Request);
                var host = body.RootElement.GetProperty("host").GetString() ?? "127.0.0.1";
                var port = body.RootElement.TryGetProperty("port", out var p) ? p.GetInt32() : 2002;
                var s = await collector.ConnectNativeAsync(host, port);
                result = new { session = s.Id, connected = s.Connected, status = s.ConnectionStatus };
            }
            else if (method == "POST" && path == "/native/disconnect")
            {
                using var body = await Body(ctx.Request);
                var id = body.RootElement.GetProperty("session").GetString();
                var s = collector.Sessions.Single(s => s.Id == id && !s.IsReplay);
                collector.DisconnectNative(s); result = new { session = s.Id, status = "已请求停止原生采集" };
            }
            else if (method == "GET" && path == "/sessions")
                result = collector.Sessions.Select(s => new { id = s.Id, name = s.Name, pid = s.Pid, platform = s.Platform,
                    source = s.Source, endpoint = s.Endpoint, status = s.ConnectionStatus, connected = s.Connected,
                    capturing = s.Capturing, recording = s.Recording, replay = s.IsReplay, total = s.Total,
                    dropped = s.Dropped, evicted = s.Evicted, viewStatesEvicted = s.ViewStatesEvicted, watermark = s.Watermark, lastTime = s.LastTime });
            else if (method == "GET" && path is "/events" or "/summary")
            {
                var q = ctx.Request.QueryString;
                var candidates = collector.Sessions.Where(s => s.Id == q["session"]).ToArray();
                if (candidates.Length != 1) throw new ArgumentException("必须指定唯一会话；相同记录已打开多次时请关闭重复查看实例");
                var s = candidates[0]; var snapshot = s.Snapshot();
                double from = Number(q["from"], 0), to = Number(q["to"], s.LastTime);
                long watermark = (long)Math.Clamp(Number(q["watermark"], s.Watermark), 0, long.MaxValue),
                    after = (long)Math.Clamp(Number(q["after"], 0), 0, long.MaxValue);
                int limit = (int)Math.Clamp(Number(q["limit"], 200), 1, 2000);
                var selected = snapshot.Where(e => e.seq <= watermark && e.time >= from && e.time <= to &&
                    (string.IsNullOrEmpty(q["kind"]) || e.kind == q["kind"]));
                if (path == "/summary") result = new { session = s.Id, from, to, watermark, earliest = snapshot.FirstOrDefault()?.time,
                    evicted = s.Evicted, dropped = s.Dropped, groups = selected.GroupBy(e => e.kind).Select(g => new { kind = g.Key, count = g.Count() }),
                    coverage = s.Source + "; evidence is source-specific; SDK and native clocks are not implicitly merged; bounded Live window" };
                else
                {
                    var page = selected.Where(e => e.seq > after).Take(limit + 1).ToArray();
                    result = new { session = s.Id, from, to, watermark, evicted = s.Evicted, events = page.Take(limit),
                        hasMore = page.Length > limit, nextAfter = page.Take(limit).LastOrDefault()?.seq ?? after };
                }
            }
            else { ctx.Response.StatusCode = method is "GET" or "POST" ? 404 : 405; result = new { error = "未知接口或方法" }; }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            try
            {
                ctx.Response.StatusCode = 400; ctx.Response.ContentType = "application/json; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = e.Message }));
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            catch { }
        }
        finally { ctx.Response.Close(); }
    }
    static async Task<JsonDocument> Body(HttpListenerRequest request)
    {
        if (request.ContentLength64 > 16384) throw new ArgumentException("请求体过大");
        using var buffer = new MemoryStream(); var chunk = new byte[2048];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int read;
        while ((read = await request.InputStream.ReadAsync(chunk, timeout.Token)) != 0)
        { if (buffer.Length + read > 16384) throw new ArgumentException("请求体过大"); buffer.Write(chunk, 0, read); }
        return JsonDocument.Parse(buffer.ToArray());
    }
    static double Number(string? s, double fallback) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : fallback;
    public void Dispose() => listener.Close();
}
