using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using OpenWrtStudio.Services;
using OpenWrtStudio.ViewModels;
using Wpf.Ui.Appearance;

namespace OpenWrtStudio;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<ISshService, SshService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IRouterService, RouterService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<IPackageManagerService, PackageManagerService>();
        services.AddSingleton<IOnlinePackageSearchService, OnlinePackageSearchService>();
        services.AddSingleton<IMihomoConfigService, MihomoConfigService>();
        services.AddSingleton<ISentinelService, SentinelService>();
        services.AddSingleton<IForkopService, ForkopService>();
        services.AddSingleton<IRouterCapabilityService, RouterCapabilityService>();
        services.AddSingleton<IVpnProtocolService, VpnProtocolService>();
        services.AddSingleton<IRouterConfigService, RouterConfigService>();
        services.AddSingleton<ICronSchedulerService, CronSchedulerService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IBackgroundMonitorService, BackgroundMonitorService>();
        services.AddSingleton<ISystemTrayManager, SystemTrayManager>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IClientManagerService, ClientManagerService>();

        // ViewModels
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ClientsViewModel>();
        services.AddSingleton<RouterSettingsViewModel>();
        services.AddSingleton<VpnProtocolsViewModel>();
        services.AddSingleton<MihomoBuilderViewModel>();
        services.AddSingleton<ForkopViewModel>();
        services.AddSingleton<SchedulerViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<PackagesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    private const string MutexName = @"Global\OpenWrtStudio_SingleInstance_Mutex_98f12a";
    private const string EventName = @"Global\OpenWrtStudio_ActivateInstance_Event_98f12a";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registeredWait;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check if an instance is already running
        bool isFirstInstance;
        try
        {
            _instanceMutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch (AbandonedMutexException)
        {
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // Another instance is already running! Signal it to bring window to front and exit
            try
            {
                using var evt = EventWaitHandle.OpenExisting(EventName);
                evt.Set();
            }
            catch { }

            Shutdown();
            return;
        }

        // Setup event listener so future shortcut clicks activate this instance
        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent, (state, timedOut) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var mainWindow = _serviceProvider.GetService<MainWindow>();
                        if (mainWindow != null)
                        {
                            mainWindow.Show();
                            if (mainWindow.WindowState == WindowState.Minimized)
                            {
                                mainWindow.WindowState = WindowState.Normal;
                            }
                            mainWindow.Activate();
                            mainWindow.Focus();

                            var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                            if (hwnd != IntPtr.Zero)
                            {
                                ShowWindow(hwnd, SW_RESTORE);
                                SetForegroundWindow(hwnd);
                            }
                        }
                    }
                    catch { }
                });
            }, null, -1, false);
        }
        catch { }

        try
        {
            var themeService = _serviceProvider.GetRequiredService<IThemeService>();
            var (savedThemeId, isDark) = themeService.LoadSavedPreferences();
            themeService.ApplyTheme(savedThemeId, isDark);

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
            var packagesViewModel = _serviceProvider.GetRequiredService<PackagesViewModel>();
            var trayManager = _serviceProvider.GetRequiredService<ISystemTrayManager>();
            var bgMonitor = _serviceProvider.GetRequiredService<IBackgroundMonitorService>();

            mainWindow.DataContext = mainViewModel;
            mainWindow.Show();

            // Initialize System Tray and Background Monitor
            trayManager.Initialize(mainWindow);
            bgMonitor.StartMonitoring(25);
            bgMonitor.OnRouterStatePolled += info =>
            {
                if (info != null)
                {
                    trayManager.UpdateStatusText($"В сети (RAM {info.RamUsagePercent:F0}%)");
                }
                else
                {
                    trayManager.UpdateStatusText("Подключено");
                }
            };

            await mainViewModel.InitializeAsync();
            await settingsViewModel.InitializeAsync();
            await packagesViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show($"Ошибка инициализации приложения: {ex.Message}\n\nСтек:\n{ex.StackTrace}", "OpenWrt Studio - Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _registeredWait?.Unregister(null);
            _activateEvent?.Dispose();
            if (_instanceMutex != null)
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
            }
        }
        catch { }

        try
        {
            _serviceProvider.GetService<ISystemTrayManager>()?.Dispose();
            _serviceProvider.GetService<IBackgroundMonitorService>()?.StopMonitoring();
        }
        catch { }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        MessageBox.Show($"Непредвиденная ошибка в интерфейсе: {e.Exception.Message}\nПодробности записаны в crash.log", "OpenWrt Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show($"Критический сбой: {ex.Message}", "OpenWrt Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenWrtStudio");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
