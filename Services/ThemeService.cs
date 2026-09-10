using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using OpenWrtStudio.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace OpenWrtStudio.Services;

public interface IThemeService
{
    IReadOnlyList<ColorThemeItem> AvailableThemes { get; }
    ColorThemeItem CurrentTheme { get; }
    bool IsDarkMode { get; set; }
    void ApplyTheme(string themeId, bool isDarkMode);
    (string themeId, bool isDarkMode) LoadSavedPreferences();
}

public class ThemePreferences
{
    public string ThemeId { get; set; } = "cyan";
    public bool IsDarkMode { get; set; } = true;
}

public class ThemeService : IThemeService
{
    private readonly string _configPath;
    private readonly List<ColorThemeItem> _themes;

    public IReadOnlyList<ColorThemeItem> AvailableThemes => _themes;
    public ColorThemeItem CurrentTheme { get; private set; }
    public bool IsDarkMode { get; set; } = true;

    public ThemeService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "OpenWrtStudio");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "theme.json");

        _themes = new List<ColorThemeItem>
        {
            new() { Id = "cyan", Name = "Кибер Циан", ColorHex = "#00D2FF", Description = "Электрический неоновый акцент по умолчанию" },
            new() { Id = "emerald", Name = "Изумрудный Оазис", ColorHex = "#10B981", Description = "Свежий зеленый стиль терминала и OpenWrt" },
            new() { Id = "purple", Name = "Киберпанк Неон", ColorHex = "#8B5CF6", Description = "Глубокий неоново-фиолетовый для ночной работы" },
            new() { Id = "sapphire", Name = "Королевский Сапфир", ColorHex = "#3B82F6", Description = "Классический высокотехнологичный синий Fluent" },
            new() { Id = "amber", Name = "Солнечный Янтарь", ColorHex = "#F59E0B", Description = "Теплый янтарно-золотой оттенок для глаз" },
            new() { Id = "ruby", Name = "Рубиновый Закат", ColorHex = "#EF4444", Description = "Энергичный контрастный малиново-красный" },
        };

        CurrentTheme = _themes[0];
    }

    public (string themeId, bool isDarkMode) LoadSavedPreferences()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var pref = JsonSerializer.Deserialize<ThemePreferences>(json);
                if (pref != null)
                {
                    var found = _themes.FirstOrDefault(t => string.Equals(t.Id, pref.ThemeId, StringComparison.OrdinalIgnoreCase));
                    if (found != null) CurrentTheme = found;
                    IsDarkMode = pref.IsDarkMode;
                    return (pref.ThemeId, pref.IsDarkMode);
                }
            }
        }
        catch { }

        return ("cyan", true);
    }

    public void ApplyTheme(string themeId, bool isDarkMode)
    {
        var theme = _themes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase)) ?? _themes[0];
        CurrentTheme = theme;
        IsDarkMode = isDarkMode;

        foreach (var t in _themes)
        {
            t.IsSelected = t.Id == theme.Id;
        }

        try
        {
            var appTheme = isDarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(appTheme, WindowBackdropType.None);

            var accentColor = theme.Color;
            ApplicationAccentColorManager.Apply(
                systemAccent: accentColor,
                applicationTheme: appTheme,
                systemGlassColor: false,
                systemAccentColor: false
            );

            // Update application brushes for custom controls that bind to dynamic resources
            if (Application.Current != null)
            {
                var accentBrush = new SolidColorBrush(accentColor);
                accentBrush.Freeze();
                Application.Current.Resources["AccentFillColorDefaultBrush"] = accentBrush;
                Application.Current.Resources["AccentTextFillColorPrimaryBrush"] = accentBrush;
                Application.Current.Resources["SystemAccentColor"] = accentColor;
                Application.Current.Resources["SystemAccentBrush"] = accentBrush;

                // Base palette definitions for guaranteed solid, high-contrast themes
                var baseBg = isDarkMode ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
                var secondaryBg = isDarkMode ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xEE, 0xEE, 0xEE);
                var cardBg = isDarkMode ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xFF, 0xFF, 0xFF);
                var cardStroke = isDarkMode ? Color.FromArgb(0x19, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1E, 0x00, 0x00, 0x00);
                var textPrimary = isDarkMode ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1C, 0x1C, 0x1C);
                var textSecondary = isDarkMode ? Color.FromRgb(0x9E, 0x9E, 0x9E) : Color.FromRgb(0x61, 0x61, 0x61);

                var baseBgBrush = new SolidColorBrush(baseBg); baseBgBrush.Freeze();
                var secondaryBgBrush = new SolidColorBrush(secondaryBg); secondaryBgBrush.Freeze();
                var cardBgBrush = new SolidColorBrush(cardBg); cardBgBrush.Freeze();
                var cardStrokeBrush = new SolidColorBrush(cardStroke); cardStrokeBrush.Freeze();
                var textPrimaryBrush = new SolidColorBrush(textPrimary); textPrimaryBrush.Freeze();
                var textSecondaryBrush = new SolidColorBrush(textSecondary); textSecondaryBrush.Freeze();

                Application.Current.Resources["ApplicationBackgroundBrush"] = baseBgBrush;
                Application.Current.Resources["SolidBackgroundFillColorBaseBrush"] = baseBgBrush;
                Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"] = secondaryBgBrush;
                Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] = cardBgBrush;
                Application.Current.Resources["CardStrokeColorDefaultBrush"] = cardStrokeBrush;
                Application.Current.Resources["TextFillColorPrimaryBrush"] = textPrimaryBrush;
                Application.Current.Resources["TextFillColorSecondaryBrush"] = textSecondaryBrush;

                if (Application.Current.MainWindow is Wpf.Ui.Controls.FluentWindow fw)
                {
                    fw.WindowBackdropType = WindowBackdropType.None;
                    fw.Background = baseBgBrush;
                    fw.Foreground = textPrimaryBrush;
                }
            }

            // Save to config
            var pref = new ThemePreferences { ThemeId = theme.Id, IsDarkMode = isDarkMode };
            var json = JsonSerializer.Serialize(pref, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] Error applying theme: {ex.Message}");
        }
    }
}
