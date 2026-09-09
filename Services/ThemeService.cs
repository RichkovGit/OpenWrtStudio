using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using OpenWrtStudio.Models;
using Wpf.Ui.Appearance;

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
            ApplicationThemeManager.Apply(appTheme);

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
                var brush = new SolidColorBrush(accentColor);
                brush.Freeze();
                Application.Current.Resources["AccentFillColorDefaultBrush"] = brush;
                Application.Current.Resources["AccentTextFillColorPrimaryBrush"] = brush;
                Application.Current.Resources["SystemAccentColor"] = accentColor;
                Application.Current.Resources["SystemAccentBrush"] = brush;
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
