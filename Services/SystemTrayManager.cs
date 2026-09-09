using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace OpenWrtStudio.Services;

public interface ISystemTrayManager
{
    void Initialize(Window mainWindow);
    void UpdateStatusText(string status);
    void Dispose();
}

public class SystemTrayManager : ISystemTrayManager, IDisposable
{
    private TaskbarIcon? _taskbarIcon;
    private Window? _mainWindow;
    private bool _isExplicitExit = false;
    private bool _hasShownMinimizeNotice = false;

    private readonly ISshService _ssh;
    private readonly INotificationService _notifications;

    public SystemTrayManager(ISshService ssh, INotificationService notifications)
    {
        _ssh = ssh;
        _notifications = notifications;
    }

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;

        _taskbarIcon = new TaskbarIcon();

        // Load Icon
        try
        {
            var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                _taskbarIcon.Icon = new System.Drawing.Icon(icoPath);
            }
            else
            {
                _taskbarIcon.Icon = System.Drawing.SystemIcons.Application;
            }
        }
        catch
        {
            _taskbarIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        _taskbarIcon.ToolTipText = "OpenWrt Studio - Мониторинг роутера";

        // Build WPF ContextMenu
        var contextMenu = new ContextMenu();

        var itemOpen = new MenuItem
        {
            Header = "🖥️ Открыть OpenWrt Studio",
            FontWeight = FontWeights.Bold
        };
        itemOpen.Click += (s, e) => RestoreMainWindow();
        contextMenu.Items.Add(itemOpen);

        contextMenu.Items.Add(new Separator());

        var itemRestartNet = new MenuItem { Header = "🔄 Перезапустить сеть" };
        itemRestartNet.Click += async (s, e) =>
        {
            if (_ssh.IsConnected)
            {
                await _ssh.ExecuteCommandAsync("/etc/init.d/network restart", 10);
                _notifications.ShowNotification("Сеть роутера", "Команда перезапуска сети отправлена на роутер.", NotificationSeverity.Info);
            }
        };
        contextMenu.Items.Add(itemRestartNet);

        var itemFlushDns = new MenuItem { Header = "🌐 Очистить кэш DNS" };
        itemFlushDns.Click += async (s, e) =>
        {
            if (_ssh.IsConnected)
            {
                await _ssh.ExecuteCommandAsync("/etc/init.d/dnsmasq restart", 10);
                _notifications.ShowNotification("DNS роутера", "Кэш DNS очищен, служба dnsmasq перезапущена.", NotificationSeverity.Info);
            }
        };
        contextMenu.Items.Add(itemFlushDns);

        contextMenu.Items.Add(new Separator());

        var itemExit = new MenuItem { Header = "❌ Выход" };
        itemExit.Click += (s, e) =>
        {
            _isExplicitExit = true;
            if (_taskbarIcon != null)
            {
                _taskbarIcon.Visibility = Visibility.Collapsed;
            }
            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainWindow?.Close();
                Application.Current.Shutdown();
            });
        };
        contextMenu.Items.Add(itemExit);

        _taskbarIcon.ContextMenu = contextMenu;
        _taskbarIcon.TrayMouseDoubleClick += (s, e) => RestoreMainWindow();

        // Listen for notification dispatches
        _notifications.OnNotificationRequested += (title, message, severity) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var iconType = severity switch
                {
                    NotificationSeverity.Critical => BalloonIcon.Error,
                    NotificationSeverity.Warning => BalloonIcon.Warning,
                    _ => BalloonIcon.Info
                };

                _taskbarIcon?.ShowBalloonTip(title, message, iconType);
            });
        };

        // Window closing behavior: minimize to tray instead of quitting
        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.StateChanged += OnMainWindowStateChanged;
    }

    public void UpdateStatusText(string status)
    {
        if (_taskbarIcon == null) return;
        var text = $"OpenWrt Studio: {status}";
        if (text.Length > 63) text = text.Substring(0, 60) + "...";
        Application.Current.Dispatcher.Invoke(() =>
        {
            _taskbarIcon.ToolTipText = text;
        });
    }

    private void RestoreMainWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            e.Cancel = true;
            _mainWindow?.Hide();

            if (!_hasShownMinimizeNotice)
            {
                _hasShownMinimizeNotice = true;
                _taskbarIcon?.ShowBalloonTip(
                    "OpenWrt Studio",
                    "Приложение свернуто в системный трей и продолжает отслеживать роутер в фоновом режиме.",
                    BalloonIcon.Info);
            }
        }
    }

    private void OnMainWindowStateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow?.WindowState == WindowState.Minimized)
        {
            _mainWindow.Hide();
        }
    }

    public void Dispose()
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.Visibility = Visibility.Collapsed;
            _taskbarIcon.Dispose();
            _taskbarIcon = null;
        }
    }
}
