using Avalonia;
using CriScope.Core;
namespace CriScope.App;
internal static class Program
{
 [STAThread] public static void Main(string[] args)
 {
   if(args.Contains("--mcp")) {McpProxy.Run().GetAwaiter().GetResult();return;}
   if(args.Contains("--collector")) {
       using var c=new Collector(DataDirectory()); c.Start(); using var query=new QueryServer(c);query.Start();
       using var done=new ManualResetEventSlim();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;done.Set();};done.Wait();return;
   }
   BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
 }
 public static string DataDirectory()
 {
     var custom=Environment.GetEnvironmentVariable("CRISCOPE_DATA");
     return string.IsNullOrWhiteSpace(custom)?Path.Combine(AppContext.BaseDirectory,".local","recordings"):Path.GetFullPath(custom);
 }
 public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
