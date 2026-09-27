using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;
using CriScope.Core;

namespace CriScope.App;
public sealed partial class MainWindow
{
    private enum UiIcon { More, Sun, Moon, Locate }
    private static string IconPath(UiIcon icon)=>icon switch {
        UiIcon.More=>"M3,11 H5 V13 H3 Z M11,11 H13 V13 H11 Z M19,11 H21 V13 H19 Z",
        UiIcon.Sun=>"M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 M12,1 V4 M12,20 V23 M1,12 H4 M20,12 H23 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
        UiIcon.Moon=>"M18,3 A9,9 0 1 0 21,17 A10,10 0 0 1 18,3 Z",
        _=>"M9,5 H4 V20 H19 V15 M11,3 H21 V13 M21,3 L10,14" };
    private Button IconAction(string tip,UiIcon icon,Action action)
    {
        var button=Action(tip,action);
        // A fixed view box preserves the same center for thin and full-height icons.
        var canvas=new Canvas {Width=24,Height=24};
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path {Data=Geometry.Parse(IconPath(icon)),Stroke=_p.Text,StrokeThickness=1.6,Fill=icon==UiIcon.More?_p.Text:null,Stretch=Stretch.None});
        button.Content=new Viewbox {Width=18,Height=18,Child=canvas};
        button.Width=34;button.Height=34;button.Padding=new Thickness(0);
        button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;return button;
    }
    private string WallTime(double time,Session? session)
    {
        var date=session?.EstimateWallTime(new WireEvent {time=time})?.ToLocalTime();
        if(date==null)return "时间未提供";
        var origin=session?.EstimateWallTime(new WireEvent {time=session.TimeOrigin})?.ToLocalTime();
        return date.Value.ToString(origin?.Date==date.Value.Date?"HH:mm:ss.fff":"MM-dd HH:mm:ss.fff");
    }
    private void StyleFolds(Control content)
    {
        foreach(var fold in content.GetLogicalDescendants().OfType<Expander>())
        {
            fold.HorizontalAlignment=HorizontalAlignment.Stretch;fold.MinHeight=0;fold.Padding=new Thickness(0);fold.Margin=new Thickness(0);
            fold.Template=new FuncControlTemplate<Expander>((owner,scope)=>{
                var panel=new StackPanel {Spacing=4};
                var heading=new Grid {ColumnDefinitions=new ColumnDefinitions("18,*"),ColumnSpacing=7};
                var arrow=new Avalonia.Controls.Shapes.Path {Data=Geometry.Parse("M5,3 L12,10 L5,17"),Stroke=_p.Muted,StrokeThickness=1.5,Width=10,Height=12,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=RelativePoint.Center};
                var title=new ContentPresenter {FontSize=13,VerticalAlignment=VerticalAlignment.Center};title.Bind(ContentPresenter.ContentProperty,new Binding("Header"){Source=owner});Grid.SetColumn(title,1);heading.Children.Add(arrow);heading.Children.Add(title);
                var toggle=new Button {Content=heading,Height=32,Padding=new Thickness(6,0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                toggle.Click+=(_,_)=>owner.IsExpanded=!owner.IsExpanded;
                void Rotate()=>arrow.RenderTransform=new RotateTransform(owner.IsExpanded?90:0);
                Rotate();owner.PropertyChanged+=(_,change)=>{if(change.Property==Expander.IsExpandedProperty)Rotate();};
                var body=new ContentPresenter {Margin=new Thickness(12,0,0,8)};body.Bind(ContentPresenter.ContentProperty,new Binding("Content"){Source=owner});body.Bind(IsVisibleProperty,new Binding("IsExpanded"){Source=owner});
                panel.Children.Add(toggle);panel.Children.Add(body);return panel;
            });
        }
    }
}
