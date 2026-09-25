using Avalonia;
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
     desktop.MainWindow=new MainWindow(collector);
     desktop.Exit+=(_,_)=>{query?.Dispose();collector.Dispose();};
   }
   base.OnFrameworkInitializationCompleted();
 }
}
