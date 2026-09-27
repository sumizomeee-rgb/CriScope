using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CriScope.Core;
namespace CriScope.App;
    internal enum IconKind { None, More, Sun, Moon, Locate, Timeline, Controls, Mixer, Space, Resources, Log, Back, Pause, Play, Live, Fit, Open, Export, Down, Details, CueSheet, Source, Listener, Stop, Selector, Block, Beat, Sequence, ClockTime, RelativeTime }

internal enum SemanticColor { Text, Muted, Selected, Started, Ended, Warning, Aisac, Selector, Block, Beat, Sequence, Source, Listener }
internal static class VisualLanguage
{
    internal static string Path(IconKind icon)=>icon switch {
        IconKind.CueSheet=>"M4,6 L12,2 L20,6 L12,10 Z M4,11 L12,15 L20,11 M4,16 L12,20 L20,16",
        IconKind.Source=>"M3,9 H7 L12,4 V20 L7,15 H3 Z M16,8 Q21,12 16,16",
        IconKind.Listener=>"M7,8 C7,0 20,0 20,9 C20,14 14,13 14,18 C14,23 7,23 7,18 M11,9 C11,5 16,5 16,9",
        IconKind.Stop=>"M5,5 H19 V19 H5 Z",
        IconKind.Selector=>"M4,5 H20 M4,12 H14 M4,19 H20 M17,9 L21,12 L17,15",
        IconKind.Block=>"M3,4 H10 V11 H3 Z M14,13 H21 V20 H14 Z M10,7 H17 V13",
        IconKind.Beat=>"M3,12 H7 L10,4 L14,20 L17,12 H21",
        IconKind.Sequence=>"M3,12 H21 M17,8 L21,12 L17,16 M5,7 V17 M11,7 V17",
        IconKind.ClockTime=>"M12,2 A10,10 0 1 1 11.99,2 M12,6 V12 L16,14",
        IconKind.RelativeTime=>"M9,2 H15 M12,2 V5 M12,5 A8,8 0 1 1 11.99,5 M12,8 V12 L16,12",
        IconKind.Timeline=>"M3,5 V19 M7,8 H21 M7,12 H17 M7,16 H21",
        IconKind.Controls=>"M5,3 V21 M12,3 V21 M19,3 V21 M2,8 H8 M9,16 H15 M16,6 H22",
        IconKind.Mixer=>"M4,9 V18 M9,4 V20 M14,7 V17 M19,2 V22",
        IconKind.Space=>"M12,3 L21,8 V17 L12,22 L3,17 V8 Z M3,8 L12,13 L21,8 M12,13 V22",
        IconKind.Resources=>"M4,4 H20 V9 H4 Z M4,14 H20 V19 H4 Z",
        IconKind.Log=>"M4,3 H20 V21 H4 Z M8,7 H16 M8,12 H16 M8,17 H13",
        IconKind.Back=>"M10,5 L3,12 L10,19 M3,12 H16 Q22,12 21,19",
        IconKind.Pause=>"M8,4 V20 M16,4 V20",
        IconKind.Play=>"M7,4 L20,12 L7,20 Z",
        IconKind.Live=>"M4,12 H8 L11,4 L15,20 L18,12 H22",
        IconKind.Fit=>"M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15",
        IconKind.Open=>"M3,7 H10 L12,9 H21 L18,19 H3 Z M3,7 V5 H10 L12,7",
        IconKind.Export=>"M12,16 V3 M7,8 L12,3 L17,8 M4,14 V21 H20 V14",
        IconKind.Down=>"M12,4 V20 M6,14 L12,20 L18,14",
        IconKind.Details=>"M3,4 H21 V20 H3 Z M15,4 V20",
        IconKind.More=>"M3,11 H5 V13 H3 Z M11,11 H13 V13 H11 Z M19,11 H21 V13 H19 Z",
        IconKind.Sun=>"M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 M12,1 V4 M12,20 V23 M1,12 H4 M20,12 H23 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
        IconKind.Moon=>"M18,3 A9,9 0 1 0 21,17 A10,10 0 0 1 18,3 Z",
        _=>"M9,5 H4 V20 H19 V15 M11,3 H21 V13 M21,3 L10,14" };

    internal static Control Glyph(IconKind icon, IBrush color, double size=16)
    {
        var canvas=new Canvas {Width=24,Height=24};
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path {Data=Geometry.Parse(Path(icon)),Stroke=color,StrokeThickness=1.6,Fill=icon==IconKind.More?color:null});
        return new Viewbox {Width=size,Height=size,Child=canvas,VerticalAlignment=VerticalAlignment.Center};
    }
    internal static IconKind EventIcon(string kind)=>kind switch {
        "play"=>IconKind.Play, "stop" or "stop-request"=>IconKind.Stop,
        "aisac"=>IconKind.Controls,"selector"=>IconKind.Selector,"block"=>IconKind.Block,
        "beat"=>IconKind.Beat,"sequence"=>IconKind.Sequence,_=>IconKind.Log };
    internal static Control EventBadge(WireEvent item,string text,Palette palette)
    {
        var color=palette.Semantic(EventColor(item));
        var panel=new StackPanel {Orientation=Orientation.Horizontal,Spacing=5};
        panel.Children.Add(Glyph(EventIcon(EventSemantics.IsPlaybackEnd(item)?"stop":item.kind),color,13));
        panel.Children.Add(new TextBlock {Text=text,Foreground=color,FontSize=11,VerticalAlignment=VerticalAlignment.Center});
        return panel;
    }
    internal static SemanticColor EventColor(WireEvent item)=>item.kind switch {
        "play"=>SemanticColor.Started,"stop" or "stop-request"=>SemanticColor.Ended,
        "aisac"=>SemanticColor.Aisac,"selector"=>SemanticColor.Selector,"block"=>SemanticColor.Block,
        "beat"=>SemanticColor.Beat,"sequence"=>SemanticColor.Sequence,
        "error" or "warning" or "gap"=>SemanticColor.Warning,
        _=>EventSemantics.IsPlaybackEnd(item)?SemanticColor.Ended:SemanticColor.Muted };
}
