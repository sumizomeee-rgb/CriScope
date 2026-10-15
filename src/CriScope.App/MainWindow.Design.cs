using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CriScope.App;

public sealed partial class MainWindow
{
    // 同一套命中区域、按压反馈和焦点轮廓，避免文字按钮与图标按钮各自使用不同的默认皮肤。
    private void RefineButton(Button button, bool accent = false)
    {
        button.Cursor = new Cursor(StandardCursorType.Hand);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Template = new FuncControlTemplate<Button>((owner, _) =>
        {
            var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
            presenter.Bind(ContentPresenter.ContentProperty, new Binding("Content") { Source=owner });
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { Source=owner });
            presenter.Bind(ContentPresenter.PaddingProperty, new Binding("Padding") { Source=owner });
            presenter.Bind(ContentPresenter.BackgroundProperty, new Binding("Background") { Source=owner });
            presenter.Bind(ContentPresenter.BorderBrushProperty, new Binding("BorderBrush") { Source=owner });
            presenter.Bind(ContentPresenter.BorderThicknessProperty, new Binding("BorderThickness") { Source=owner });
            presenter.Bind(ContentPresenter.CornerRadiusProperty, new Binding("CornerRadius") { Source=owner });
            presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new Binding("HorizontalContentAlignment") { Source=owner });
            presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, new Binding("VerticalContentAlignment") { Source=owner });
            presenter.Bind(ContentPresenter.ForegroundProperty, new Binding("Foreground") { Source=owner });
            var focus = new Border { CornerRadius=button.CornerRadius, BorderBrush=_p.Selection,
                BorderThickness=new Thickness(1),Opacity=0,IsHitTestVisible=false };
            var root = new Grid {Children={presenter,focus}};
            owner.GotFocus+=(_,_)=>focus.Opacity=1;
            owner.LostFocus+=(_,_)=>focus.Opacity=0;
            owner.PropertyChanged+=(_,e)=>
            {
                if(e.Property==Button.IsPressedProperty) presenter.Opacity=owner.IsPressed?.7:1;
                if(e.Property==IsEnabledProperty) root.Opacity=owner.IsEnabled?1:.4;
            };
            root.Opacity=owner.IsEnabled?1:.4;
            return root;
        });
        IBrush? rest=null;
        button.PointerEntered+=(_,_)=> { rest=button.Background; button.Background=accent?_p.Good:_p.Hover; };
        button.PointerExited+=(_,_)=> { if(rest!=null)button.Background=rest; };
        if(_animateInterface) button.Transitions = new Transitions {
            new BrushTransition {Property=Button.BackgroundProperty,Duration=TimeSpan.FromMilliseconds(90)}
        };
    }
}
