using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CriScope.Core;

namespace CriScope.App;
public sealed partial class MainWindow
{
    private Button IconAction(string tip,IconKind icon,Action action)
    {
        var button=Action(tip,action);
        button.Content=VisualLanguage.Glyph(icon,_p.Text,18);
        button.Width=UiMetrics.IconTarget;button.Height=UiMetrics.IconTarget;button.Padding=new Thickness(0);
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
                var panel=new StackPanel {Spacing=UiMetrics.Space1};
                var heading=new Grid {ColumnDefinitions=new ColumnDefinitions("*,18")};
                var arrow=new Avalonia.Controls.Shapes.Path {Data=Geometry.Parse("M5,3 L12,10 L5,17"),Stroke=_p.Muted,StrokeThickness=1.5,Width=10,Height=12,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=RelativePoint.Center};
                var rotation=new RotateTransform(owner.IsExpanded?90:0);arrow.RenderTransform=rotation;
                if(_animateInterface)rotation.Transitions=new Transitions {new DoubleTransition {Property=RotateTransform.AngleProperty,Duration=TimeSpan.FromMilliseconds(135)}};
                Grid.SetColumn(arrow,1);
                var title=new ContentPresenter {FontSize=UiMetrics.CaptionSize,VerticalAlignment=VerticalAlignment.Center};title.Bind(ContentPresenter.ContentProperty,new Binding("Header"){Source=owner});heading.Children.Add(title);heading.Children.Add(arrow);
                var toggle=new Button {Content=heading,Height=UiMetrics.InspectorRowHeight,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(4),Cursor=new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                toggle.PointerEntered+=(_,_)=>toggle.Background=_p.Hover;
                toggle.PointerExited+=(_,_)=>toggle.Background=Brushes.Transparent;
                AddCopyMenu(toggle,()=>owner.Header?.ToString()??"","复制标题");
                toggle.Click+=(_,_)=>owner.IsExpanded=!owner.IsExpanded;
                // The disclosure glyph sits outside the content column; expanded fields keep
                // the same left edge as the event facts above the fold.
                var body=new ContentPresenter {Margin=new Thickness(0,2,0,UiMetrics.Space2),Opacity=owner.IsExpanded?1:0};
                if(_animateInterface)body.Transitions=new Transitions {new DoubleTransition {Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(135)}};
                body.Bind(ContentPresenter.ContentProperty,new Binding("Content"){Source=owner});body.Bind(IsVisibleProperty,new Binding("IsExpanded"){Source=owner});
                var fadeVersion=0;
                owner.PropertyChanged+=(_,change)=>{
                    if(change.Property!=Expander.IsExpandedProperty)return;
                    rotation.Angle=owner.IsExpanded?90:0;
                    var version=++fadeVersion;
                    if(owner.IsExpanded&&_animateInterface){body.Opacity=0;Dispatcher.UIThread.Post(()=>{if(version==fadeVersion&&owner.IsExpanded)body.Opacity=1;});}
                    else body.Opacity=owner.IsExpanded?1:0;
                };
                panel.Children.Add(toggle);panel.Children.Add(body);return panel;
            });
        }
    }
}
