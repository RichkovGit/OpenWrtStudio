using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace OpenWrtStudio;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        WindowBackdropType = WindowBackdropType.None;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.RelativeOrAbsolute);
            var resStream = Application.GetResourceStream(iconUri);
            if (resStream != null)
            {
                Icon = BitmapFrame.Create(resStream.Stream);
            }
        }
        catch
        {
            // Fallback gracefully if pack URI is not available
        }
    }
}