using System.Net;
using System.Text;
using System.Text.Json;
using System.Globalization;
namespace CriScope.Core;

// 只读本机端点，明确会话与水位；不提供游戏控制或任意文件读取。
public sealed class QueryServer : IDisposable
{
 readonly Collector collector;
 readonly HttpListener listener=new();
 public QueryServer(Collector c) {collector=c;listener.Prefixes.Add("http://127.0.0.1:18962/");}
 public void Start(){listener.Start();_=Loop();}
 async Task Loop()
 {
   while(listener.IsListening) {
     try {var context=await listener.GetContextAsync();_=Respond(context);} catch(HttpListenerException){break;} catch(ObjectDisposedException){break;}
   }
 }
 async Task Respond(HttpListenerContext ctx)
 {
   try {
     if(ctx.Request.HttpMethod!="GET") {ctx.Response.StatusCode=405;return;}
     // 不允许浏览器跨站读取本地音频数据。
     if(ctx.Request.Headers["Origin"]!=null) {ctx.Response.StatusCode=403;return;}
     object result;
     if(ctx.Request.Url!.AbsolutePath=="/sessions") result=collector.Sessions.Select(s=>new {id=s.Id,name=s.Name,pid=s.Pid,platform=s.Platform,connected=s.Connected,capturing=s.Capturing,recording=s.Recording,replay=s.IsReplay,total=s.Total,dropped=s.Dropped,evicted=s.Evicted,watermark=s.Watermark,lastTime=s.LastTime});
     else if(ctx.Request.Url.AbsolutePath is "/events" or "/summary") {
       var q=ctx.Request.QueryString;
       var candidates=collector.Sessions.Where(s=>s.Id==q["session"]).ToArray();
       if(candidates.Length!=1) {ctx.Response.StatusCode=400;result=new {error="必须指定唯一会话；相同记录已打开多次时请关闭重复查看实例"};}
       else {
         var s=candidates[0];var snapshot=s.Snapshot();
         double from=Number(q["from"],0),to=Number(q["to"],s.LastTime);
         long watermark=(long)Number(q["watermark"],s.Watermark),after=(long)Number(q["after"],0);
         int limit=Math.Clamp((int)Number(q["limit"],200),1,2000);
         var selected=snapshot.Where(e=>e.seq<=watermark&&e.time>=from&&e.time<=to&&(string.IsNullOrEmpty(q["kind"])||e.kind==q["kind"]));
         if(ctx.Request.Url.AbsolutePath=="/summary") result=new {session=s.Id,from,to,watermark,earliest=snapshot.FirstOrDefault()?.time,evicted=s.Evicted,dropped=s.Dropped,groups=selected.GroupBy(e=>e.kind).Select(g=>new{kind=g.Key,count=g.Count()}),coverage="Haru bridge observed events; not native Voice allocation; bounded Live window"};
         else {var page=selected.Where(e=>e.seq>after).Take(limit+1).ToArray();result=new{session=s.Id,from,to,watermark,evicted=s.Evicted,events=page.Take(limit),hasMore=page.Length>limit,nextAfter=page.Take(limit).LastOrDefault()?.seq??after};}
       }
     } else {ctx.Response.StatusCode=404;result=new{error="使用 /sessions、/events 或 /summary"};}
     var bytes=JsonSerializer.SerializeToUtf8Bytes(result);ctx.Response.ContentType="application/json; charset=utf-8";ctx.Response.ContentLength64=bytes.Length;await ctx.Response.OutputStream.WriteAsync(bytes);
   } catch(Exception e) when(e is not OutOfMemoryException) {try{ctx.Response.StatusCode=400;var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{error=e.Message}));await ctx.Response.OutputStream.WriteAsync(bytes);}catch{}}
   finally {ctx.Response.Close();}
 }
 static double Number(string? s,double fallback)=>double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v)?v:fallback;
 public void Dispose()=>listener.Close();
}
