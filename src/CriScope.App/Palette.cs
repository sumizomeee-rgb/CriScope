using Avalonia.Media;

namespace CriScope.App;

internal sealed class Palette(bool light)
{
    public bool Light { get; } = light;
    public IBrush Shell => B(Light ? "#E8E3D9" : "#0A0D10");
    public IBrush Panel => B(Light ? "#F1EEE7" : "#11161C");
    public IBrush Canvas => B(Light ? "#FBFAF6" : "#0C1015");
    public IBrush Alternate => B(Light ? "#F5F2EB" : "#10161D");
    public IBrush Border => B(Light ? "#D6D2CA" : "#29313B");
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
    private static IBrush B(string color) => Brush.Parse(color);
}
