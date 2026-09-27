using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace CriScope.App;

public sealed partial class MainWindow
{
    // Information, navigation, and secondary evidence have separate, stable regions.
    private void OrganizeInspector(StackPanel content)
    {
        if (_selected == null) { EnableInspectorCopy(content); return; }
        var more = new StackPanel { Spacing = UiMetrics.Space1 };
        var links = new StackPanel { Tag = "inspector-links", Spacing = UiMetrics.Space1, Margin = new Thickness(0,0,0,UiMetrics.Space2) };
        var original = content.Children.ToArray();
        content.Children.Clear();
        void Flatten(Control item, StackPanel target)
        {
            if(item is Expander {Tag:"technical"} advanced)
            {
                advanced.Header="原始记录";
                target.Children.Add(advanced);
            }
            else if (item is Expander fold && fold.Content is Control body)
            {
                fold.Content = null;
                target.Children.Add(InspectorSection(fold.Header?.ToString() ?? "","section:"+fold.Tag));
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
            else if(item is Button record && record.Tag?.ToString()?.StartsWith("event:",StringComparison.Ordinal)==true && record.Content is TextBlock recordText)
            {
                var row=new Grid {ColumnDefinitions=new ColumnDefinitions("*,32"),ColumnSpacing=UiMetrics.Space2,MinHeight=44,Background=Brushes.Transparent};
                var originalText=recordText.Text??"";
                var breakAt=originalText.LastIndexOf('\n');
                var summary=breakAt>=0?originalText[..breakAt]:originalText;
                var timestamp=breakAt>=0?originalText[(breakAt+1)..]:"";
                var text=new StackPanel {Spacing=2,Margin=new Thickness(0,4,0,4)};
                var label=InspectorValue(summary,UiMetrics.BodySize);
                label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxLines=2;
                ToolTip.SetTip(label,originalText);
                text.Children.Add(label);
                if(timestamp.Length>0)text.Children.Add(InspectorValue(timestamp,11,_p.Muted));
                record.Content=NavigationContent("");record.Width=32;record.Height=32;record.Padding=new Thickness(8,0);
                ToolTip.SetTip(record,"定位此记录");Grid.SetColumn(record,1);
                StyleInspectorLink(row,record);
                row.Children.Add(text);row.Children.Add(record);target.Children.Add(row);
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
            else if(_selected.kind=="position" && item is Grid note && note.Tag?.ToString()=="row:详情")
            {
                var description=note.Children.OfType<SelectableTextBlock>().FirstOrDefault();
                if(description!=null && description.Text!="未提供")
                {
                    note.Children.Remove(description);
                    var heading=Label("记录说明",11,_p.Muted);heading.Tag="position-note-heading";heading.FontWeight=FontWeight.SemiBold;heading.Margin=new Thickness(0,8,0,2);
                    description.FontSize=12;description.Foreground=_p.Text;
                    more.Children.Add(heading);more.Children.Add(description);
                }
            }
            else if(item.Tag?.ToString() is "row:请求播放" or "row:请求停止") more.Children.Add(item);
            else content.Children.Add(item);
        }
        // Never make a long Cue name into a large action button. Keep the name readable
        // and provide a separate, consistently sized navigation affordance.
        foreach(var button in links.Children.OfType<Button>().ToArray())
        {
            var full=ToolTip.GetTip(button)?.ToString() ?? "定位";
            var caption=button is NavigationButton nav?nav.Caption switch {"定位空间音源"=>"空间音源","定位开始播放"=>"开始播放","定位事件时间"=>"事件时间",_=>nav.Caption}:full;
            button.Content=NavigationContent(caption);
            button.Height=28;button.Padding=new Thickness(4,0);
            button.HorizontalAlignment=HorizontalAlignment.Left;
            button.VerticalContentAlignment=VerticalAlignment.Center;
            if(button is NavigationButton {RelatedName.Length:>0} related) {
                var index=links.Children.IndexOf(button);links.Children.Remove(button);
                var row=new Grid {Tag=button.Tag,ColumnDefinitions=new ColumnDefinitions("*,Auto,32"),ColumnSpacing=UiMetrics.Space2,MinHeight=36,Background=Brushes.Transparent};
                var split=related.RelatedName.LastIndexOf(" · 播放实例 #",StringComparison.Ordinal);
                var name=InspectorValue(split>=0?related.RelatedName[..split]:related.RelatedName,UiMetrics.BodySize);
                if(split>=0){var number=InspectorValue(related.RelatedName[(split+3)..].Replace("播放实例 ",""),11,_p.Muted);Grid.SetColumn(number,1);row.Children.Add(number);}
                name.TextTrimming=TextTrimming.CharacterEllipsis;name.MaxLines=2;ToolTip.SetTip(name,related.RelatedName);
                button.Content=NavigationContent("");button.Width=32;button.Padding=new Thickness(8,0);Grid.SetColumn(button,2);
                StyleInspectorLink(row,button);
                row.Children.Add(name);row.Children.Add(button);links.Children.Insert(index,row);
            }
        }
        var actions=new WrapPanel {Orientation=Orientation.Horizontal};
        foreach(var action in links.Children.OfType<NavigationButton>().ToArray()) {
            links.Children.Remove(action);action.Margin=new Thickness(0,0,8,0);actions.Children.Add(action);
        }
        if(actions.Children.Count>0)links.Children.Insert(0,actions);
        ArrangePlaybackFacts(content);
        if(links.Children.Count>0){links.Children.Insert(0,InspectorSection("关联与定位","section:associations"));content.Children.Add(links);}
        if(more.Children.Count>0)content.Children.Add(new Expander {
            Tag="more-information",Header="更多信息",Content=more,FontSize=12,
            HorizontalAlignment=HorizontalAlignment.Stretch });
        NormalizeInspectorRows(content);
        EnableInspectorCopy(content);
    }

    private void StyleInspectorLink(Grid row,Button button)
    {
        button.Height=30;button.CornerRadius=new CornerRadius(4);
        button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;button.BorderThickness=new Thickness(1);
        button.Cursor=new Cursor(StandardCursorType.Hand);
        row.PointerEntered+=(_,_)=>row.Background=_p.Hover;
        row.PointerExited+=(_,_)=>row.Background=Brushes.Transparent;
        var hovered=false;
        button.PointerEntered+=(_,_)=>{hovered=true;button.Background=_p.Hover;if(!button.IsFocused)button.BorderBrush=_p.Muted;};
        button.PointerExited+=(_,_)=>{hovered=false;button.Background=Brushes.Transparent;if(!button.IsFocused)button.BorderBrush=Brushes.Transparent;};
        button.GotFocus+=(_,_)=>{button.BorderBrush=_p.Text;button.BorderThickness=new Thickness(2);};
        button.LostFocus+=(_,_)=>{button.BorderBrush=hovered?_p.Muted:Brushes.Transparent;button.BorderThickness=new Thickness(1);};
        ToolTip.SetTip(row,"点击右侧图标定位；名称和编号可选择、复制");
    }

    private void NormalizeInspectorRows(Control content)
    {
        foreach(var row in content.GetLogicalDescendants().OfType<Grid>())
        {
            var tag=row.Tag?.ToString();
            if(tag is null || !tag.StartsWith("row:",StringComparison.Ordinal) && tag!="categories")continue;
            row.ColumnDefinitions=new ColumnDefinitions(row.ColumnDefinitions.Count==3?UiMetrics.InspectorActionColumns:UiMetrics.InspectorColumns);
            row.ColumnSpacing=UiMetrics.InspectorFieldGap;
            if(tag=="categories")row.Margin=new Thickness(0);
            foreach(var button in row.Children.OfType<Button>())
            {
                button.CornerRadius=new CornerRadius(4);
                button.BorderBrush=_p.Border;button.BorderThickness=new Thickness(1);
                button.Cursor=new Cursor(StandardCursorType.Hand);
            }
        }
    }

    private Border InspectorSection(string title,string tag)
    {
        var caption=Label(title,UiMetrics.CaptionSize,_p.Muted);caption.FontWeight=FontWeight.SemiBold;
        return new Border {Tag=tag,BorderBrush=_p.Border,BorderThickness=new Thickness(0,1,0,0),
            Margin=new Thickness(0,UiMetrics.Space3,0,UiMetrics.Space1),Padding=new Thickness(0,UiMetrics.Space2,0,0),Child=caption};
    }

    private void ArrangePlaybackFacts(StackPanel content)
    {
        Grid? Take(string tag) {var row=content.Children.OfType<Grid>().FirstOrDefault(x=>x.Tag?.ToString()==tag);if(row!=null)content.Children.Remove(row);return row;}
        var sheet=Take("row:CueSheet / ACB");
        var status=Take("row:状态");
        var start=Take("row:开始播放");
        var elapsed=Take("row:已播放")??Take("row:播放历时")??Take("row:本次观测");
        var end=Take("row:结束播放");
        var reason=Take("row:结束原因");
        var cause=Take("row:由谁触发");
        var category=Take("categories")??Take("row:Category")??Take("row:Cue 分类");
        var duration=Take("row:Cue 时长");
        var occurred=Take("row:发生时间");
        if(sheet==null && status==null && start==null && elapsed==null && category==null && duration==null && occurred==null)return;
        var insertAt=0;
        if(sheet!=null)
        {
            var value=sheet.Children.OfType<SelectableTextBlock>().FirstOrDefault()?.Text??"未获取";
            var subtitle=new StackPanel {Tag="cue-sheet",Orientation=Orientation.Horizontal,Spacing=6,Margin=new Thickness(0,2,0,8)};
            var icon=VisualLanguage.Glyph(IconKind.CueSheet,_p.Muted,14);icon.Tag="cue-sheet-icon";subtitle.Children.Add(icon);
            var text=new SelectableTextBlock {Tag="cue-sheet-name",Text=value,FontSize=11,Foreground=_p.Muted};AddCopyMenu(text,"复制 CueSheet / ACB 名称");text.TextTrimming=TextTrimming.CharacterEllipsis;text.MaxWidth=250;
            ToolTip.SetTip(subtitle,value+"\n"+ToolTip.GetTip(sheet));subtitle.Children.Add(text);
            content.Children.Insert(insertAt++,subtitle);
        }
        void Insert(Grid? row)
        {
            if(row==null)return;
            content.Children.Insert(insertAt++,row);
        }
        Insert(occurred);Insert(status);
        if(start!=null||end!=null||elapsed!=null)
        {
            content.Children.Insert(insertAt++,InspectorSection("播放时间","section:playback-time"));
            Insert(start);Insert(end);
            if(elapsed!=null){foreach(var value in elapsed.Children.OfType<SelectableTextBlock>())value.FontWeight=FontWeight.SemiBold;Insert(elapsed);}
            Insert(reason);Insert(cause);
        }
        if(category!=null||duration!=null)
        {
            content.Children.Insert(insertAt++,InspectorSection("Cue 信息","section:cue-info"));
            Insert(category);Insert(duration);
        }
    }

    private SelectableTextBlock InspectorValue(string value,double size=UiMetrics.BodySize,IBrush? color=null) => new()
    {
        Text=value,FontSize=size,Foreground=color??_p.Text,VerticalAlignment=VerticalAlignment.Center,
        TextWrapping=TextWrapping.Wrap
    };

    private void EnableInspectorCopy(Control content)
    {
        foreach(var text in content.GetLogicalDescendants().OfType<TextBlock>())
            if(text.ContextMenu==null)AddCopyMenu(text,"复制完整文字");
        foreach(var button in content.GetLogicalDescendants().OfType<Button>())
            if(button.ContextMenu==null)AddCopyMenu(button,()=>{
                var tip=ToolTip.GetTip(button)?.ToString()??"";
                if(button is NavigationButton nav) {
                    var prefix=nav.Caption+" · ";
                    if(tip.StartsWith(prefix,StringComparison.Ordinal))return tip[prefix.Length..];
                }
                return tip;
            },"复制文字");
    }

    private void AddCopyMenu(TextBlock text,string caption)
    {
        var items=new List<MenuItem>();
        if(text is SelectableTextBlock selectable)
        {
            var copySelection=new MenuItem {Header="复制选中或完整文字"};
            copySelection.Click+=async (_,_)=>{if(Clipboard is {} clipboard)await clipboard.SetTextAsync(
                string.IsNullOrEmpty(selectable.SelectedText)?text.Text??"":selectable.SelectedText);};
            items.Add(copySelection);
        }
        var copyAll=new MenuItem {Header=caption};
        copyAll.Click+=async (_,_)=>{if(Clipboard is {} clipboard)await clipboard.SetTextAsync(text.Text??"");};
        items.Add(copyAll);
        text.ContextMenu=new ContextMenu {ItemsSource=items};
    }

    private void AddCopyMenu(Control control,Func<string> value,string caption)
    {
        var copy=new MenuItem {Header=caption};
        copy.Click+=async (_,_)=>{if(Clipboard is {} clipboard)await clipboard.SetTextAsync(value());};
        control.ContextMenu=new ContextMenu {ItemsSource=new[]{copy}};
    }

    private Control NavigationContent(string caption)
    {
        var panel=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6};
        panel.Children.Add(VisualLanguage.Glyph(IconKind.Locate,_p.Text));
        panel.Children.Add(Label(caption));
        return panel;
    }
}
