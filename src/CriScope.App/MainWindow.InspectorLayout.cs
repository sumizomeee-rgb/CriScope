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
            else if(item.Tag?.ToString() is "row:请求播放" or "row:请求停止" ||
                    _selected.kind=="position" && item.Tag?.ToString()=="row:详情") more.Children.Add(item);
            else content.Children.Add(item);
        }
        // Never make a long Cue name into a large action button. Keep the name readable
        // and provide a separate, consistently sized navigation affordance.
        foreach(var button in links.Children.OfType<Button>().ToArray())
        {
            var full=ToolTip.GetTip(button)?.ToString() ?? "定位";
            var caption=button is NavigationButton nav?nav.Caption:full;
            button.Content=NavigationContent(caption);
            button.Height=32;button.Padding=new Thickness(8,0);
            button.HorizontalAlignment=HorizontalAlignment.Left;
            button.VerticalContentAlignment=VerticalAlignment.Center;
            if(button is NavigationButton {RelatedName.Length:>0} related) {
                var index=links.Children.IndexOf(button);links.Children.Remove(button);
                var row=new Grid {Tag=button.Tag,ColumnDefinitions=new ColumnDefinitions("*,Auto,32"),ColumnSpacing=8,MinHeight=32};
                var split=related.RelatedName.LastIndexOf(" · 播放实例 #",StringComparison.Ordinal);
                var name=Label(split>=0?related.RelatedName[..split]:related.RelatedName,12);
                if(split>=0){var number=Label(related.RelatedName[(split+3)..].Replace("播放实例 ",""),11,_p.Muted);Grid.SetColumn(number,1);row.Children.Add(number);}
                name.TextTrimming=TextTrimming.CharacterEllipsis;ToolTip.SetTip(name,full);
                button.Content=NavigationContent("");button.Width=32;button.Padding=new Thickness(8,0);Grid.SetColumn(button,2);
                row.Children.Add(name);row.Children.Add(button);links.Children.Insert(index,row);
            }
        }
        ArrangePlaybackFacts(content);
        if(links.Children.Count>0)content.Children.Add(links);
        if(more.Children.Count>0)content.Children.Add(new Expander {
            Tag="more-information",Header="更多信息",Content=more,FontSize=13,
            HorizontalAlignment=HorizontalAlignment.Stretch });
    }

    private void ArrangePlaybackFacts(StackPanel content)
    {
        Grid? Take(string tag) {var row=content.Children.OfType<Grid>().FirstOrDefault(x=>x.Tag?.ToString()==tag);if(row!=null)content.Children.Remove(row);return row;}
        var identity=Take("row:播放实例");
        var title=content.Children.OfType<TextBlock>().FirstOrDefault(x=>x.Tag?.ToString()=="inspector-title");
        if(identity!=null && title!=null) {
            var id=identity.Children.OfType<SelectableTextBlock>().FirstOrDefault()?.Text??"";
            var index=content.Children.IndexOf(title);content.Children.Remove(title);
            var heading=new Grid {Tag="playback-heading",ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=8};
            title.MaxLines=2;title.TextTrimming=TextTrimming.CharacterEllipsis;ToolTip.SetTip(title,title.Text);
            heading.Children.Add(title);var number=Label(id.Replace("播放实例 ",""),12,_p.Muted);Grid.SetColumn(number,1);heading.Children.Add(number);content.Children.Insert(index,heading);
        }
        var sheet=Take("row:CueSheet / ACB");
        if(sheet!=null) {
            var index=content.Children.ToList().FindIndex(x=>x.Tag?.ToString()=="playback-heading");
            var value=sheet.Children.OfType<SelectableTextBlock>().FirstOrDefault()?.Text??"未获取";
            var subtitle=new StackPanel {Tag="cue-sheet",Orientation=Orientation.Horizontal,Spacing=6,Margin=new Thickness(0,2,0,8)};
            subtitle.Children.Add(VisualLanguage.Glyph(IconKind.CueSheet,_p.Muted,14));
            var text=Label("CueSheet / ACB · "+value,11,_p.Muted);text.TextTrimming=TextTrimming.CharacterEllipsis;text.MaxWidth=250;
            ToolTip.SetTip(subtitle,value+"\n"+ToolTip.GetTip(sheet));subtitle.Children.Add(text);
            content.Children.Insert(index>=0?index+1:Math.Min(2,content.Children.Count),subtitle);
        }
        var elapsed=Take("row:已播放")??Take("row:播放历时")??Take("row:本次观测");
        var duration=Take("row:Cue 时长");
        if(elapsed!=null && duration!=null) {
            var summary=new Grid {Tag="duration-summary",ColumnDefinitions=new ColumnDefinitions("*,*"),ColumnSpacing=12,Margin=new Thickness(0,8,0,10)};
            foreach(var (row,column) in new[]{(elapsed,0),(duration,1)}) {
                var fields=row.Children.ToArray();row.Children.Clear();var cell=new StackPanel {Spacing=4};
                foreach(var field in fields){if(field is SelectableTextBlock value){value.FontSize=value.Text?.EndsWith(" 秒")==true?18:13;value.FontWeight=value.FontSize==18?FontWeight.SemiBold:FontWeight.Normal;}cell.Children.Add(field);}
                ToolTip.SetTip(cell,ToolTip.GetTip(row));Grid.SetColumn(cell,column);summary.Children.Add(cell);
            }
            var status=content.Children.ToList().FindIndex(x=>x.Tag?.ToString()=="row:状态");content.Children.Insert(status>=0?status+1:Math.Min(2,content.Children.Count),summary);
        }
        var category=Take("categories")??Take("row:Category")??Take("row:Cue 分类");
        if(category!=null)content.Children.Add(category);
    }

    private Control NavigationContent(string caption)
    {
        var panel=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6};
        panel.Children.Add(VisualLanguage.Glyph(IconKind.Locate,_p.Text));
        panel.Children.Add(Label(caption));
        return panel;
    }
}
