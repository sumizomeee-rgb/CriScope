using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private Control RecordContent(bool recording)
    {
        var panel=new StackPanel {Orientation=Orientation.Horizontal,Spacing=7};
        panel.Children.Add(new TextBlock {Text=recording?"●":"○",Foreground=recording?_p.Error:_p.Text,FontSize=16,VerticalAlignment=VerticalAlignment.Center});
        panel.Children.Add(Label(recording?"停止并保存":"开始录制",12));
        return panel;
    }
}
