using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using OpenWrtStudio.Converters;
using OpenWrtStudio.Services;
using Wpf.Ui.Appearance;
using Xunit;

namespace OpenWrtStudio.Tests;

public class ThemeTests
{
    [Fact]
    public void Theme_CanInspectThemeManagerAndResources()
    {
        var methods = typeof(ApplicationThemeManager).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == "Apply").ToList();
        Assert.True(methods.Count > 0);

        var asm = typeof(Wpf.Ui.Controls.FluentWindow).Assembly;
        var stream = asm.GetManifestResourceStream("Wpf.Ui.g.resources");
        Assert.NotNull(stream);

        var reader = new System.Resources.ResourceReader(stream);
        reader.GetResourceData("resources/theme/light.baml", out _, out byte[] data);
        Assert.True(data.Length > 0);

        var ms = new System.IO.MemoryStream(data);
        var binReader = new System.IO.BinaryReader(ms);
        int len = binReader.ReadInt32();
        var bamlReader = new System.Windows.Baml2006.Baml2006Reader(new System.IO.MemoryStream(binReader.ReadBytes(len)));
        var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(bamlReader);

        Assert.True(dict.Contains("SolidBackgroundFillColorBaseBrush"));
        Assert.True(dict.Contains("CardBackgroundFillColorDefaultBrush"));
        Assert.True(dict.Contains("TextFillColorPrimaryBrush"));
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

    [Fact]
    public void ThemeService_ApplyTheme_LightModeSetsLightBrushes()
    {
        if (Application.Current == null) new Application();

        var service = new ThemeService();
        service.ApplyTheme("amber", false);

        Assert.Equal("amber", service.CurrentTheme.Id);
        Assert.False(service.IsDarkMode);

        var baseBg = Application.Current!.Resources["SolidBackgroundFillColorBaseBrush"] as SolidColorBrush;
        Assert.NotNull(baseBg);
        Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), baseBg.Color);

        var cardBg = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as SolidColorBrush;
        Assert.NotNull(cardBg);
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), cardBg.Color);

        var textPrimary = Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush;
        Assert.NotNull(textPrimary);
        Assert.Equal(Color.FromRgb(0x1C, 0x1C, 0x1C), textPrimary.Color);
    }

    [Fact]
    public void ThemeService_ApplyTheme_DarkModeSetsDarkBrushes()
    {
        if (Application.Current == null) new Application();

        var service = new ThemeService();
        service.ApplyTheme("cyan", true);

        Assert.Equal("cyan", service.CurrentTheme.Id);
        Assert.True(service.IsDarkMode);

        var baseBg = Application.Current!.Resources["SolidBackgroundFillColorBaseBrush"] as SolidColorBrush;
        Assert.NotNull(baseBg);
        Assert.Equal(Color.FromRgb(0x20, 0x20, 0x20), baseBg.Color);

        var cardBg = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as SolidColorBrush;
        Assert.NotNull(cardBg);
        Assert.Equal(Color.FromRgb(0x2B, 0x2B, 0x2B), cardBg.Color);

        var textPrimary = Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush;
        Assert.NotNull(textPrimary);
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), textPrimary.Color);
    }
}
