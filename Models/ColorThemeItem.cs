using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenWrtStudio.Models;

public partial class ColorThemeItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#00D2FF";
    public string Description { get; set; } = "";

    public Color Color => (Color)ColorConverter.ConvertFromString(ColorHex);
    public SolidColorBrush Brush => new(Color);

    [ObservableProperty]
    private bool _isSelected;
}
