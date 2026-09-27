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
    private enum UiIcon { None, More, Sun, Moon, Locate, Timeline, Controls, Mixer, Space, Resources, Log, Back, Pause, Play, Live, Fit, Open, Export, Down, Details }
    private static string IconPath(UiIcon icon)=>icon switch {
        UiIcon.Timeline=>"M3,5 V19 M7,8 H21 M7,12 H17 M7,16 H21",
        UiIcon.Controls=>"M5,3 V21 M12,3 V21 M19,3 V21 M2,8 H8 M9,16 H15 M16,6 H22",
        UiIcon.Mixer=>"M4,9 V18 M9,4 V20 M14,7 V17 M19,2 V22",
        UiIcon.Space=>"M12,3 L21,8 V17 L12,22 L3,17 V8 Z M3,8 L12,13 L21,8 M12,13 V22",
        UiIcon.Resources=>"M4,4 H20 V9 H4 Z M4,14 H20 V19 H4 Z",
        UiIcon.Log=>"M4,3 H20 V21 H4 Z M8,7 H16 M8,12 H16 M8,17 H13",
        UiIcon.Back=>"M10,5 L3,12 L10,19 M3,12 H16 Q22,12 21,19",
        UiIcon.Pause=>"M8,4 V20 M16,4 V20",
        UiIcon.Play=>"M7,4 L20,12 L7,20 Z",
        UiIcon.Live=>"M4,12 H8 L11,4 L15,20 L18,12 H22",
        UiIcon.Fit=>"M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15",
        UiIcon.Open=>"M3,7 H10 L12,9 H21 L18,19 H3 Z M3,7 V5 H10 L12,7",
        UiIcon.Export=>"M12,16 V3 M7,8 L12,3 L17,8 M4,14 V21 H20 V14",
        UiIcon.Down=>"M12,4 V20 M6,14 L12,20 L18,14",
        UiIcon.Details=>"M3,4 H21 V20 H3 Z M15,4 V20",
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
