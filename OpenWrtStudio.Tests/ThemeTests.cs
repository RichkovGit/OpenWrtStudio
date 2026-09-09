using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using OpenWrtStudio.Converters;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class ThemeTests
{
    [Fact]
    public void Theme_CanParseHexColors()
    {
        var color = (Color)ColorConverter.ConvertFromString("#00D2FF");
        Assert.Equal(0x00, color.R);
        Assert.Equal(0xD2, color.G);
        Assert.Equal(0xFF, color.B);
    }

    [Fact]
    public void ThemeService_ContainsAllRequiredPalettes()
    {
        var service = new ThemeService();
        Assert.NotNull(service.AvailableThemes);
        Assert.True(service.AvailableThemes.Count >= 6);

        var ids = service.AvailableThemes.Select(t => t.Id).ToList();
        Assert.Contains("cyan", ids);
        Assert.Contains("emerald", ids);
        Assert.Contains("purple", ids);
        Assert.Contains("sapphire", ids);
        Assert.Contains("amber", ids);
        Assert.Contains("ruby", ids);

        foreach (var theme in service.AvailableThemes)
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.Name));
            Assert.StartsWith("#", theme.ColorHex);
            Assert.NotNull(theme.Brush);
        }
    }

    [Fact]
    public void ThemeService_ApplyTheme_UpdatesCurrentAndSelection()
    {
        var service = new ThemeService();
        service.ApplyTheme("emerald", false);

        Assert.Equal("emerald", service.CurrentTheme.Id);
        Assert.False(service.IsDarkMode);

        var emerald = service.AvailableThemes.First(t => t.Id == "emerald");
        var cyan = service.AvailableThemes.First(t => t.Id == "cyan");

        Assert.True(emerald.IsSelected);
        Assert.False(cyan.IsSelected);
    }

    [Fact]
    public void Converters_ThemeConverters_FormatCorrectly()
    {
        var thicknessConv = new BoolToThicknessConverter();
        Assert.Equal(new Thickness(2), thicknessConv.Convert(true, typeof(Thickness), null!, null!));
        Assert.Equal(new Thickness(1), thicknessConv.Convert(false, typeof(Thickness), null!, null!));

        var textConv = new BoolToSelectedTextConverter();
        Assert.Equal("✓ Выбрано", textConv.Convert(true, typeof(string), null!, null!));
        Assert.Equal("Выбрать", textConv.Convert(false, typeof(string), null!, null!));

        var stringVisConv = new StringToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, stringVisConv.Convert("", typeof(Visibility), null!, null!));
        Assert.Equal(Visibility.Collapsed, stringVisConv.Convert(null!, typeof(Visibility), null!, null!));
        Assert.Equal(Visibility.Visible, stringVisConv.Convert("Theme applied", typeof(Visibility), null!, null!));
    }
}
