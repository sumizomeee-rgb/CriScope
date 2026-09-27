using Avalonia.Controls;

namespace CriScope.App;

/// <summary>Refresh values without replacing the live drawer, focus, or expanded sections.</summary>
public static class InspectorPresentation
{
    public static void Update(Panel target, Panel desired)
    {
        var previous = target.Children.ToArray();
        var next = desired.Children.ToArray();
        desired.Children.Clear();
        for (int i = 0; i < next.Length; i++)
        {
            var key = Key(next[i]);
            var old = key != null ? previous.FirstOrDefault(c => Equals(Key(c), key))
                : i < previous.Length && Key(previous[i]) == null ? previous[i] : null;
            var item = Merge(old, next[i]);
            if (i < target.Children.Count && ReferenceEquals(target.Children[i], item)) continue;
            target.Children.Remove(item);
            target.Children.Insert(i, item);
        }
        while (target.Children.Count > next.Length) target.Children.RemoveAt(next.Length);
    }

    private static object? Key(Control control) => control.Tag ?? (control is Button ? ToolTip.GetTip(control) : null);

    private static Control Merge(Control? old, Control next)
    {
        if (old == null || old.GetType() != next.GetType()) return next;
        var nextTip=ToolTip.GetTip(next);
        if(!Equals(ToolTip.GetTip(old),nextTip))ToolTip.SetTip(old,nextTip);
        switch (old, next)
        {
            case (SelectableTextBlock a, SelectableTextBlock b):
                var selectedStart=a.SelectionStart;
                var selectedEnd=a.SelectionEnd;
                var sameText=a.Text==b.Text;
                if (!sameText) a.Text = b.Text;
                if(a.FontSize!=b.FontSize)a.FontSize=b.FontSize;
                if(a.FontWeight!=b.FontWeight)a.FontWeight=b.FontWeight;
                if(!Equals(a.Foreground,b.Foreground))a.Foreground=b.Foreground;
                if(sameText && selectedStart!=selectedEnd && (a.SelectionStart!=selectedStart || a.SelectionEnd!=selectedEnd))
                {a.SelectionStart=selectedStart;a.SelectionEnd=selectedEnd;}
                return a;
            case (TextBlock a, TextBlock b):
                if (a.Text != b.Text) a.Text = b.Text;
                a.Foreground = b.Foreground;
                return a;
            case (Panel a, Panel b):
                Update(a, b);
                return a;
            case (Expander a, Expander b):
                a.Header = b.Header;
                if (a.Content is Panel ap && b.Content is Panel bp) Update(ap, bp);
                return a;
            case (Button a, Button b) when Key(a) != null && Equals(Key(a), Key(b)):
                // Keys include the event identity for links: never retain a handler for another event.
                if (a.Content is Panel buttonPanel && b.Content is Panel newButtonPanel) Update(buttonPanel, newButtonPanel);
                else if (a.Content is TextBlock at && b.Content is TextBlock bt) at.Text = bt.Text;
                return a;
            case (Viewbox a, Viewbox) when Key(a) is "cue-sheet-icon":
                return a;
            case (Border a, Border):
                return a;
            default:
                return next;
        }
    }
}
