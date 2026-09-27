using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CriScope.App;

public sealed partial class MainWindow
{
    // Information, navigation, and secondary evidence have separate, stable regions.
    private void OrganizeInspector(StackPanel content)
    {
        if (_selected == null) return;
        var more = new StackPanel { Spacing = 4 };
        var links = new StackPanel { Tag = "inspector-links", Spacing = 4, Margin = new Thickness(0,12,0,8) };
        var original = content.Children.ToArray();
        content.Children.Clear();
        void Flatten(Control item, StackPanel target)
        {
            if (item is Expander fold && fold.Content is Control body)
            {
                fold.Content = null;
                var heading = Label(fold.Header?.ToString() ?? "",12,_p.Muted);
                heading.Margin = new Thickness(0,12,0,4);
                target.Children.Add(heading);
                if(body is StackPanel panel) {
                    var children=panel.Children.ToArray();panel.Children.Clear();
                    foreach(var child in children) Flatten(child,target);
                } else target.Children.Add(body);
            }
            else if(item is StackPanel associations && associations.Tag?.ToString()=="association-links")
            {
                var children=associations.Children.ToArray();associations.Children.Clear();
                foreach(var child in children)links.Children.Add(child);
            }
            else if(item is TextBlock section && section.Text=="关联与定位") { }
            else if(item is Button navigation && navigation.Tag?.ToString()?.StartsWith("cause:")==true)
                links.Children.Add(navigation);
            else if(item is Button record && record.Content is TextBlock recordText)
            {
                var row=new Grid {ColumnDefinitions=new ColumnDefinitions("*,32"),ColumnSpacing=8,MinHeight=32};
                var label=Label((recordText.Text??"").Replace("\n"," · "),11);
                label.TextTrimming=TextTrimming.CharacterEllipsis;
                ToolTip.SetTip(label,recordText.Text);
                record.Content=NavigationContent("");record.Width=32;record.Height=32;record.Padding=new Thickness(8,0);
                ToolTip.SetTip(record,"定位此记录");Grid.SetColumn(record,1);
                row.Children.Add(label);row.Children.Add(record);target.Children.Add(row);
            }
            else target.Children.Add(item);
        }
        foreach(var item in original)
        {
            if(item is Expander) Flatten(item,more);
            else if(item is Button button) links.Children.Add(button);
            else if(item is StackPanel group && group.Tag?.ToString()=="association-links") {
                var children=group.Children.ToArray();group.Children.Clear();
                foreach(var child in children)links.Children.Add(child);
            }
            else if(item is TextBlock text && text.Text=="关联与定位") { }
            else if(item.Tag?.ToString() is "row:请求播放" or "row:请求停止" or "row:播放实例" ||
                    _selected.kind=="position" && item.Tag?.ToString()=="row:详情") more.Children.Add(item);
            else content.Children.Add(item);
        }
        // Never make a long Cue name into a large action button. Keep the name readable
        // and provide a separate, consistently sized navigation affordance.
        foreach(var button in links.Children.OfType<Button>().ToArray())
        {
            var full=ToolTip.GetTip(button)?.ToString() ?? "定位";
            var caption=full.StartsWith("定位声音") ? "声音时间线" : full.Contains("音源") ? "空间音源" :
                full.Contains("触发") ? "触发实例" : full.Contains("轨道") ? "事件轨道" :
                full.StartsWith("查看 ") ? "声音时间线" : "事件时间";
            button.Content=NavigationContent(caption);
            button.Height=32;button.Padding=new Thickness(8,0);
            button.HorizontalAlignment=HorizontalAlignment.Left;
            button.VerticalContentAlignment=VerticalAlignment.Center;
            if(full.Contains(" · ") || full.StartsWith("查看 ")) {
                var index=links.Children.IndexOf(button);links.Children.Remove(button);
                var row=new Grid {Tag=button.Tag,ColumnDefinitions=new ColumnDefinitions("*,32"),ColumnSpacing=8,MinHeight=32};
                var name=Label(full.Replace("定位声音 · ","").Replace("查看 ","").Replace(" 的播放",""),12);
                name.TextTrimming=TextTrimming.CharacterEllipsis;ToolTip.SetTip(name,full);
                button.Content=NavigationContent("");button.Width=32;button.Padding=new Thickness(8,0);Grid.SetColumn(button,1);
                row.Children.Add(name);row.Children.Add(button);links.Children.Insert(index,row);
            }
        }
        if(links.Children.Count>0)content.Children.Add(links);
        if(more.Children.Count>0)content.Children.Add(new Expander {
            Tag="more-information",Header="更多信息",Content=more,FontSize=13,
            HorizontalAlignment=HorizontalAlignment.Stretch });
    }

    private Control NavigationContent(string caption)
    {
        var panel=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6};
        panel.Children.Add(new Avalonia.Controls.Shapes.Path {
            Data=Geometry.Parse(IconPath(UiIcon.Locate)),Stroke=_p.Text,StrokeThickness=1.5,
            Width=15,Height=15,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center });
        panel.Children.Add(Label(caption));
        return panel;
    }
}
