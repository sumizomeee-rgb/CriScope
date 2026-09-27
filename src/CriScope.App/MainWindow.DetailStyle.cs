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
    private Button IconAction(string tip,IconKind icon,Action action)
    {
        var button=Action(tip,action);
        button.Content=VisualLanguage.Glyph(icon,_p.Text,18);
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
                var title=new ContentPresenter {FontSize=12,VerticalAlignment=VerticalAlignment.Center};title.Bind(ContentPresenter.ContentProperty,new Binding("Header"){Source=owner});Grid.SetColumn(title,1);heading.Children.Add(arrow);heading.Children.Add(title);
                var toggle=new Button {Content=heading,Height=28,Padding=new Thickness(6,0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                toggle.Click+=(_,_)=>owner.IsExpanded=!owner.IsExpanded;
                void Rotate()=>arrow.RenderTransform=new RotateTransform(owner.IsExpanded?90:0);
                Rotate();owner.PropertyChanged+=(_,change)=>{if(change.Property==Expander.IsExpandedProperty)Rotate();};
                var body=new ContentPresenter {Margin=new Thickness(12,0,0,8)};body.Bind(ContentPresenter.ContentProperty,new Binding("Content"){Source=owner});body.Bind(IsVisibleProperty,new Binding("IsExpanded"){Source=owner});
                panel.Children.Add(toggle);panel.Children.Add(body);return panel;
            });
        }
    }
}
