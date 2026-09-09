using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OpenWrtStudio.Models;
using Wpf.Ui.Controls;

namespace OpenWrtStudio.Converters;

public class HealthStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is HealthStatus status)
        {
            return status switch
            {
                HealthStatus.Good => new SolidColorBrush(Color.FromRgb(16, 185, 129)),     // Green
                HealthStatus.Warning => new SolidColorBrush(Color.FromRgb(245, 158, 11)),  // Amber
                HealthStatus.Danger => new SolidColorBrush(Color.FromRgb(239, 68, 68)),    // Red
                _ => new SolidColorBrush(Color.FromRgb(59, 130, 246))                      // Blue
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class HealthStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is HealthStatus status)
        {
            return status switch
            {
                HealthStatus.Good => SymbolRegular.CheckmarkCircle24,
                HealthStatus.Warning => SymbolRegular.Warning24,
                HealthStatus.Danger => SymbolRegular.DismissCircle24,
                _ => SymbolRegular.Info24
            };
        }
        return SymbolRegular.Info24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class IndexToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && int.TryParse(parameter?.ToString(), out int target))
        {
            return current == target;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && int.TryParse(parameter?.ToString(), out int target))
        {
            return target;
        }
        return Binding.DoNothing;
    }
}

public class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && int.TryParse(parameter?.ToString(), out int target))
        {
            return current == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        return b ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(107, 114, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        var parts = parameter?.ToString()?.Split('|');
        if (parts != null && parts.Length >= 2)
        {
            return b ? parts[0] : parts[1];
        }
        return b ? "Да" : "Нет";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

