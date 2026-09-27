using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace CriScope.App;

internal sealed class Palette(bool light)
{
    private readonly Dictionary<string,IBrush> _categoryBrushes=new(StringComparer.Ordinal);
    private readonly Dictionary<int,IBrush> _indexedCategoryBrushes=[];
    private static readonly string[] LightCategoryColors=["#B35D44", "#477C99", "#8C678F", "#5E8357", "#AD7D33", "#538282", "#A55B72", "#6B76A2", "#877747", "#63866C", "#A06257", "#6C7E9A"];
    private static readonly string[] DarkCategoryColors=["#E6A18B", "#83BEDB", "#C0A2CE", "#9EC58B", "#E0BB73", "#8DCBC9", "#DCA0B6", "#A4B0DA", "#C9BF82", "#A2C9AE", "#D7A69B", "#A6BDDE"];
    public bool Light { get; } = light;
    public IBrush Shell => B(Light ? "#E8E3D9" : "#0A0D10");
    public IBrush Panel => B(Light ? "#F1EEE7" : "#11161C");
    public IBrush Canvas => B(Light ? "#FBFAF6" : "#0C1015");
    public IBrush Alternate => B(Light ? "#F5F2EB" : "#10161D");
    public IBrush Border => B(Light ? "#D6D2CA" : "#29313B");
    public IBrush TimelineGrid => B(Light ? "#E9E6DF" : "#1A222B");
    public IBrush Text => B(Light ? "#282D34" : "#E0E5EA");
    public IBrush Muted => B(Light ? "#69717A" : "#909CA9");
    public IBrush Signal => B(Light ? "#985716" : "#D8A56D");
    public IBrush Selection => B(Light ? "#7255A0" : "#AD96D6");
    public IBrush Error => B(Light ? "#B73C45" : "#EC8790");
    public IBrush Good => B(Light ? "#357B69" : "#83BDA8");
    public IBrush Voice => B(Light ? "#267E85" : "#74CBC2");
    public IBrush Request => B(Light ? "#A36D20" : "#E6BA78");
    public IBrush Listener => B(Light ? "#386FB5" : "#88B5F6");
    public IBrush Hover => B(Light ? "#E6E0D5" : "#25303D");
    public IBrush WorkspaceActive => B(Light ? "#E2DDD3" : "#26323F");
    public IBrush WorkspaceActiveBorder => B(Light ? "#B1AAA0" : "#536374");
    public IBrush WorkspaceHover => B(Light ? "#F3EFE7" : "#18222D");
    public IBrush Control(string kind) => ControlPresentation.Type(kind) switch
    {
        ControlKind.Aisac => B(Light ? "#7550AF" : "#BDA2E8"),
        ControlKind.Selector => B(Light ? "#946718" : "#D8B76A"),
        ControlKind.Block => B(Light ? "#B04E32" : "#EB987C"),
        ControlKind.BeatSync => B(Light ? "#237C71" : "#75C8B4"),
        ControlKind.Sequence => B(Light ? "#3B6FAF" : "#8FB9F0"), _ => Muted
    };
    public IBrush Semantic(SemanticColor color)=>color switch {
        SemanticColor.Selected=>Selection,SemanticColor.Started=>Good,SemanticColor.Ended=>Error,
        SemanticColor.Warning=>Signal,SemanticColor.Source=>Voice,SemanticColor.Listener=>Listener,
        SemanticColor.Aisac=>Control("aisac"),SemanticColor.Selector=>Control("selector"),
        SemanticColor.Block=>Control("block"),SemanticColor.Beat=>Control("beat"),
        SemanticColor.Sequence=>Control("sequence"),SemanticColor.Text=>Text,_=>Muted };
    public IBrush SequenceTag(string tag)
    {
        // FNV-1a is stable across processes, unlike string.GetHashCode().
        uint hash = 2166136261; foreach (char c in tag) { hash ^= c; hash *= 16777619; }
        string[] colors = Light ? ["#8D536E", "#6E7141", "#427D8E", "#895C40", "#67588F", "#3D7760", "#90633F", "#6B6987"]
            : ["#D9A0BC", "#B8BE83", "#8EC1CC", "#D6AC8E", "#B5A6D3", "#9FC3AD", "#D1B58E", "#ADAFCB"];
        return B(colors[hash % colors.Length]);
    }
    public IBrush Category(string id)
    {
        if(_categoryBrushes.TryGetValue(id,out var cached))return cached;
        // Use the visible Category name when available to keep its hue across sessions and filters.
        uint hash = 2166136261; foreach(char c in id){hash ^= c;hash *= 16777619;}
        var colors=Light?LightCategoryColors:DarkCategoryColors;
        return _categoryBrushes[id]=B(colors[hash % colors.Length]);
    }
    public IBrush Category(int groupOrdinal)
    {
        if(groupOrdinal<0)return Muted;
        if(_indexedCategoryBrushes.TryGetValue(groupOrdinal,out var cached))return cached;
        // Use the order within the selected Category Group, not a name hash.
        // Every member has a distinct hue, including groups larger than the curated set.
        var colors=Light?LightCategoryColors:DarkCategoryColors;
        return _indexedCategoryBrushes[groupOrdinal]=groupOrdinal<colors.Length
            ? B(colors[groupOrdinal]) : ExtendedCategory(groupOrdinal);
    }
    private IBrush ExtendedCategory(int ordinal)
    {
        var hue=(ordinal*137.50776405)%360;
        var saturation=Light ? 0.58 : 0.47;
        var value=Light ? 0.68 : 0.84;
        var chroma=saturation*value;
        var secondary=chroma*(1-Math.Abs((hue/60)%2-1));
        var offset=value-chroma;
        var (red,green,blue)=hue switch {
            <60=>(chroma,secondary,0d),<120=>(secondary,chroma,0d),
            <180=>(0d,chroma,secondary),<240=>(0d,secondary,chroma),
            <300=>(secondary,0d,chroma),_=>(chroma,0d,secondary) };
        return new SolidColorBrush(Color.FromRgb((byte)Math.Round((red+offset)*255),
            (byte)Math.Round((green+offset)*255),(byte)Math.Round((blue+offset)*255)));
    }
    private static IBrush B(string color) => Brush.Parse(color);
}
