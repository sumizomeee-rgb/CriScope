using Avalonia.Controls;
namespace CriScope.App;

// The caller declares the destination and optional identity. Labels never determine behavior.
internal sealed class NavigationButton : Button
{
    public string Caption { get; init; } = "定位";
    public string RelatedName { get; init; } = "";
}
