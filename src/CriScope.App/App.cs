using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using CriScope.Core;
namespace CriScope.App;
public sealed class App : Application
{
 public override void Initialize()=>Styles.Add(new FluentTheme());
 public override void OnFrameworkInitializationCompleted()
 {
   if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
     var collector=new Collector(Program.DataDirectory());
     QueryServer? query=null;
     try {collector.Start();query=new QueryServer(collector);query.Start();}
     catch(Exception e) {
       var logs=Path.Combine(AppContext.BaseDirectory,".local","logs");Directory.CreateDirectory(logs);
       File.AppendAllText(Path.Combine(logs,"startup.log"),DateTime.Now+" "+e+Environment.NewLine);
     }
     var window=new MainWindow(collector);
     desktop.MainWindow=window;
     if(query!=null) {
       query.CaptureScreenshot=async panel=>await Dispatcher.UIThread.InvokeAsync(()=>window.CapturePng(panel));
       query.ReadUiState=async ()=>await Dispatcher.UIThread.InvokeAsync(()=>window.UiState());
       query.ChangeUiState=async (action,value)=>await Dispatcher.UIThread.InvokeAsync(()=> {window.ApplyUiAction(action,value);return window.UiState();});
     }
     window.Opened+=async (_,_)=> {
       var args=Environment.GetCommandLineArgs();var at=Array.IndexOf(args,"--connect");
       if(at>=0 && at+1<args.Length && Uri.TryCreate("tcp://"+args[at+1],UriKind.Absolute,out var endpoint)) {
         try {var session=await collector.ConnectNativeAsync(endpoint.Host,endpoint.Port<0?2002:endpoint.Port);window.ApplyUiAction("session",session.Id);}
         catch(Exception ex) {Console.Error.WriteLine(ex.Message);}
       }
     };
     desktop.Exit+=(_,_)=>{query?.Dispose();collector.Dispose();};
   }
   base.OnFrameworkInitializationCompleted();
 }
}
