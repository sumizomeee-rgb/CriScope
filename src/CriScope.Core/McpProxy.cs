using System.Text.Json;
namespace CriScope.Core;

public static class McpProxy
{
 public static async Task Run()
 {
   using var http=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:18962/"),Timeout=TimeSpan.FromSeconds(10)};
   string? line;
   while((line=await Console.In.ReadLineAsync())!=null) {
     JsonElement id=default;
     try {
       using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
       if(!root.TryGetProperty("id",out id))continue;
       id=id.Clone();
       var method=root.GetProperty("method").GetString();object result;
       if(method=="initialize") result=new{protocolVersion="2024-11-05",capabilities=new{tools=new{}},serverInfo=new{name="criscope",version="0.1.0"}};
       else if(method=="ping")result=new{};
       else if(method=="tools/list") result=new{tools=new object[]{
         new{name="list_sessions",description="列出独立音频采集会话",inputSchema=new{type="object",properties=new{}}},
         new{name="query_events",description="按会话、时间窗、水位分页查询真实事件；after用于翻页",inputSchema=Schema()},
         new{name="summarize_window",description="汇总指定会话和时间窗，标注缺失和Live窗口边界",inputSchema=Schema()}}};
       else if(method=="tools/call") {
         var p=root.GetProperty("params");var name=p.GetProperty("name").GetString();
         string endpoint=name switch{"list_sessions"=>"sessions","query_events"=>"events","summarize_window"=>"summary",_=>throw new InvalidOperationException("未知工具")};
         if(endpoint!="sessions") {
           var a=p.GetProperty("arguments");if(!a.TryGetProperty("session",out _))throw new InvalidOperationException("必须指定session");
           endpoint+="?"+string.Join("&",a.EnumerateObject().Where(x=>new[]{"session","from","to","kind","watermark","after","limit"}.Contains(x.Name)).Select(x=>Uri.EscapeDataString(x.Name)+"="+Uri.EscapeDataString(x.Value.ToString())));
         }
         using var response=await http.GetAsync(endpoint);string data=await response.Content.ReadAsStringAsync();result=new{content=new[]{new{type="text",text=data}},isError=!response.IsSuccessStatusCode};
       } else {await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,error=new{code=-32601,message="未知方法"}}));continue;}
       await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,result}));
     } catch(Exception e) {
       if(id.ValueKind!=JsonValueKind.Undefined)await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,error=new{code=-32603,message=e.Message}}));
     }
   }
 }
 static object Schema()=>new{type="object",properties=new{session=new{type="string"},from=new{type="number"},to=new{type="number"},kind=new{type="string"},watermark=new{type="integer"},after=new{type="integer"},limit=new{type="integer"}},required=new[]{"session"}};
}
