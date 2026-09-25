using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CriScope.App;

internal sealed class WindowTitleBar : Border
{
    public WindowTitleBar(Window owner, Palette palette)
    {
        Height = 36; Background = palette.Shell;
        BorderBrush = palette.Border; BorderThickness = new Thickness(0, 0, 0, 1);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M2,10 V18 M8,3 V25 M14,6 V22 M20,12 V16"), Stroke = palette.Voice, StrokeThickness = 2.5, Width = 18, Height = 23, Stretch = Stretch.Uniform });
        brand.Children.Add(new TextBlock { Text = "CriScope", FontSize = 13, Foreground = palette.Text, VerticalAlignment = VerticalAlignment.Center });
        layout.Children.Add(brand);
        var title = new TextBlock { Text = "音频诊断工作台", FontSize = 11, Foreground = palette.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0) };
        Grid.SetColumn(title, 1); layout.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Caption("最小化", "M4,11 H20 V12 H4 Z", () => owner.WindowState = WindowState.Minimized));
        buttons.Children.Add(Caption("最大化 / 还原", "M5,5 H19 V19 H5 Z", ToggleMaximize));
        buttons.Children.Add(Caption("关闭", "M5,5 L19,19 M19,5 L5,19", owner.Close, true));
        Grid.SetColumn(buttons, 2); layout.Children.Add(buttons); Child = layout;
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsInteractive(e.Source)) return;
            if (e.ClickCount == 2) ToggleMaximize(); else owner.BeginMoveDrag(e);
            e.Handled = true;
        };
        void ToggleMaximize() => owner.WindowState = owner.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        Button Caption(string text, string path, Action action, bool close = false)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Stroke = palette.Text, StrokeThickness = 1.4, Stretch = Stretch.None });
            var button = new Button { Width = 46, Height = 35, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Content = new Viewbox { Width = 16, Height = 16, Child = canvas } };
            ToolTip.SetTip(button, text); button.Click += (_, _) => action();
            button.PointerEntered += (_, _) => button.Background = close ? palette.Error : palette.Hover;
            button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
            return button;
        }
    }
    private static bool IsInteractive(object? source)
    {
        for (var visual = source as Visual; visual != null; visual = visual.GetVisualParent())
            if (visual is Button or TextBox or MenuItem) return true;
        return false;
    }
}
