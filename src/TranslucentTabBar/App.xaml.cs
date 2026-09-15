using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TranslucentTabBar.Models;
using TranslucentTabBar.Services;
using TranslucentTabBar.Views;
using Windows.UI;

namespace TranslucentTabBar;

/// <summary>
/// Provides application-specific behavior and manages the application lifecycle.
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private Window? _window;
    private MainWindow? _mainWindow;

    // Services
    private LoggerService? _logger;
    private ConfigService? _configService;
    private TaskbarService? _taskbarService;
    private WindowMonitorService? _windowMonitorService;
    private AppVisibilityService? _appVisibilityService;
    private StateResolverService? _stateResolverService;
    private TrayIconService? _trayIconService;
    private TrayContextMenuService? _trayContextMenuService;
    private LocalizationService? _localizationService;
    private StartupManager? _startupManager;

    // UI Windows
    private Window? _settingsWindow;
    private Views.SettingsPage? _settingsPage;
    private Window? _colorPickerWindow;
    private Window? _welcomeWindow;

    // DI Container
    private IServiceProvider? _services;

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public new static App Current => (App)Application.Current;

    /// <summary>
    /// Gets the localization service.
    /// </summary>
    public LocalizationService Localization => _localizationService!;

    /// <summary>
    /// Gets the config service.
    /// </summary>
    public ConfigService Config => _configService!;

    /// <summary>
    /// Gets the taskbar service.
    /// </summary>
    public TaskbarService Taskbar => _taskbarService!;

    /// <summary>
    /// Gets the state resolver service.
    /// </summary>
    public App()
    {
        InitializeComponent();

        UnhandledException += (sender, e) =>
        {
            _logger?.Error($"WinUI UnhandledException: {e.Message} (HR: 0x{e.Exception?.HResult:X8})\n{e.Exception}");
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            _logger?.Error($"AppDomain UnhandledException: {e.ExceptionObject}");
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // ── Single-instance check ─────────────────────────────────
        try
        {
            _mutex = new Mutex(true, "TranslucentTabBar_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running - notify it
                NotifyExistingInstance();
                Exit();
                return;
            }
        }
        catch
        {
            // Mutex creation failed - continue anyway
        }

        // ── Initialize services via DI ────────────────────────────
        var services = new ServiceCollection();

        // Core services (singleton)
        services.AddSingleton<LoggerService>();
        services.AddSingleton<ConfigService>();
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<ConfigService>();
            return new LocalizationService(config.Config.Language);
        });
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<LoggerService>();
            return new TaskbarService(logger);
        });
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<LoggerService>();
            return new StartupManager(logger);
        });

        // Window monitoring
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<LoggerService>();
            return new WindowMonitorService(logger);
        });
        services.AddSingleton<AppVisibilityService>();

        // State resolver
        services.AddSingleton(sp =>
        {
            var windowMonitor = sp.GetRequiredService<WindowMonitorService>();
            var config = sp.GetRequiredService<ConfigService>();
            var logger = sp.GetRequiredService<LoggerService>();
            return new StateResolverService(windowMonitor, config, logger);
        });

        // Tray services
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<LoggerService>();
            return new TrayIconService(logger);
        });
        services.AddSingleton(sp =>
        {
            var trayIcon = sp.GetRequiredService<TrayIconService>();
            var config = sp.GetRequiredService<ConfigService>();
            var stateResolver = sp.GetRequiredService<StateResolverService>();
            var taskbar = sp.GetRequiredService<TaskbarService>();
            var startup = sp.GetRequiredService<StartupManager>();
            var localization = sp.GetRequiredService<LocalizationService>();
            var logger = sp.GetRequiredService<LoggerService>();
            return new TrayContextMenuService(
                trayIcon.WindowHandle, config, stateResolver, taskbar, startup, localization, logger);
        });

        _services = services.BuildServiceProvider();

        // Resolve services for use in this class
        _logger = _services.GetRequiredService<LoggerService>();
        _logger.Info("Application starting...");

        _configService = _services.GetRequiredService<ConfigService>();
        _taskbarService = _services.GetRequiredService<TaskbarService>();
        _localizationService = _services.GetRequiredService<LocalizationService>();
        _startupManager = _services.GetRequiredService<StartupManager>();
        _windowMonitorService = _services.GetRequiredService<WindowMonitorService>();
        _appVisibilityService = _services.GetRequiredService<AppVisibilityService>();
        _windowMonitorService.SetAppVisibilityService(_appVisibilityService);
        _stateResolverService = _services.GetRequiredService<StateResolverService>();
        _trayIconService = _services.GetRequiredService<TrayIconService>();
        _trayContextMenuService = _services.GetRequiredService<TrayContextMenuService>();

        // Async initialization
        await _startupManager.InitializeAsync();

        // Wire up event subscriptions
        _appVisibilityService.LauncherVisibilityChanged += OnLauncherVisibilityChanged;

        // ── Set up the main hidden window ─────────────────────────
        _mainWindow = new MainWindow();
        _window = _mainWindow;
        _mainWindow.AppWindow.IsShownInSwitchers = false;
        _mainWindow.Activate();
        _mainWindow.AppWindow.Hide();

        // ── Wire up tray icon events ──────────────────────────────
        _trayIconService.TrayRightClick += OnTrayRightClick;
        _trayIconService.TrayLeftClick += OnTrayLeftClick;
        _trayIconService.ExplorerRestarted += OnExplorerRestarted;

        // ── Wire up tray context menu events ──────────────────────
        _trayContextMenuService.ColorRequested += OnColorRequested;
        _trayContextMenuService.SettingsRequested += OnSettingsRequested;
        _trayContextMenuService.AboutRequested += OnAboutRequested;
        _trayContextMenuService.ExitRequested += OnExitRequested;

        // Handle menu commands from the tray window
        _trayIconService.MenuCommand += OnTrayMenuCommand;

        // ── Wire up state resolver to taskbar ─────────────────────
        _stateResolverService.AppearanceChanged += OnAppearanceChanged;
        _taskbarService.TapBecameAvailable += OnTapBecameAvailable;

        // ── Apply language from config ────────────────────────────
        if (!string.IsNullOrEmpty(_configService.Config.Language))
        {
            _localizationService.SetLanguage(_configService.Config.Language);
        }

        // ── Apply initial appearance ──────────────────────────────
        // Forced: the window monitor may already have resolved a state before
        // AppearanceChanged was subscribed above.
        _stateResolverService.Refresh(force: true);

        // ── Show welcome page on first launch ─────────────────────
        ShowWelcomePageIfNeeded();

        _logger.Info("Application started successfully");
    }

    private static void NotifyExistingInstance()
    {
        // Find the existing instance's hidden window and notify it
        var hwnd = NativeMethods.User32.FindWindowW("TranslucentTabBar_TrayWindow", null);
        if (hwnd != nint.Zero)
        {
            var msg = NativeMethods.User32.RegisterWindowMessageW("TranslucentTabBar_ShowSettings");
            NativeMethods.User32.PostMessageW(hwnd, msg, nint.Zero, nint.Zero);
        }
    }

    private void OnLauncherVisibilityChanged(bool visible)
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            _windowMonitorService?.SetStartOpened(visible);
        });
    }

    private void OnAppearanceChanged(TaskbarAppearance appearance)
    {
        _taskbarService?.ApplyAppearance(appearance);
        _logger?.Debug($"Appearance applied: {appearance.Accent}");
    }

    private void OnTapBecameAvailable()
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            _logger?.Info("ExplorerTAP attached - reapplying appearance");
            _stateResolverService?.Refresh(force: true);
        });
    }

    private void OnTrayLeftClick()
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            ToggleSettingsWindow();
        });
    }

    private void OnTrayRightClick()
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            ShowContextMenu();
        });
    }

    private void OnTrayMenuCommand(int commandId)
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            _trayContextMenuService?.HandleCommand(commandId);
        });
    }

    private void OnExplorerRestarted()
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            _logger?.Info("Explorer restarted - reapplying appearance");
            _taskbarService?.ResetExplorerTap();
            _taskbarService?.InvalidateCache();
            _stateResolverService?.Refresh(force: true);
        });
    }

    private void ShowContextMenu()
    {
        // Get cursor position
        if (NativeMethods.User32.GetCursorPos(out var pt))
        {
            _trayContextMenuService?.Show(pt.x, pt.y);
        }
    }

    private void ToggleSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        ShowSettingsWindow();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        try
        {
            _settingsPage = new Views.SettingsPage(
                new ViewModels.SettingsViewModel(_configService!, _stateResolverService!, _localizationService!),
                _configService!,
                _localizationService!,
                _taskbarService!,
                _stateResolverService!,
                _startupManager!);

            _settingsPage.ColorRequested += (state, color) =>
            {
                OnColorRequested(state, color);
            };
            _settingsPage.AboutRequested += OnAboutRequested;
            _settingsPage.ExitRequested += OnExitRequested;

            _settingsWindow = new Window
            {
                Title = "Glassbar",
                Content = new Grid
                {
                    Children = { _settingsPage }
                },
            };

            ConfigureWindowSize(_settingsWindow, 480, 720);

            _settingsWindow.Closed += (s, e) =>
            {
                _settingsWindow = null;
                _settingsPage = null;
            };

            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to show settings window: {ex}");
        }
    }

    private void OnColorRequested(Models.DynamicState state, Color initialColor)
    {
        var colorPicker = new ColorPickerPage(initialColor, _localizationService);
        bool confirmed = false;

        // Live preview: apply color on change
        colorPicker.ColorPreviewChanged += (color) =>
        {
            ApplyColorPreview(state, color);
            _settingsPage?.SetStateColor(state, color);
        };

        colorPicker.ColorConfirmed += (color) =>
        {
            confirmed = true;
            UpdateStateColor(state, color);
            _settingsPage?.SetStateColor(state, color);
            _settingsPage?.LoadSettings();
            _colorPickerWindow?.Close();
        };

        colorPicker.Closed += () =>
        {
            if (!confirmed)
            {
                ApplyColorPreview(state, initialColor);
                _settingsPage?.SetStateColor(state, initialColor);
                _settingsPage?.LoadSettings();
                _stateResolverService?.Refresh();
            }
            _colorPickerWindow?.Close();
        };

        string title = _localizationService?.GetString("ColorPicker_Title") ?? "Taskbar Color";

        _colorPickerWindow = new Window
        {
            Title = title,
            Content = new Grid
            {
                Children = { colorPicker }
            },
        };

        ConfigureWindowSize(_colorPickerWindow, 540, 840);

        _colorPickerWindow.Closed += (s, e) =>
        {
            if (!confirmed)
            {
                ApplyColorPreview(state, initialColor);
                _settingsPage?.SetStateColor(state, initialColor);
                _settingsPage?.LoadSettings();
                _stateResolverService?.Refresh();
            }
            _colorPickerWindow = null;
        };

        _colorPickerWindow.Activate();
    }

    private void ApplyColorPreview(Models.DynamicState state, Color color)
    {
        var config = _configService!.Config;
        var appearance = GetAppearanceForState(state, config);
        if (appearance != null)
        {
            var previewAppearance = new TaskbarAppearance(
                appearance.Accent, color, appearance.ShowPeek, appearance.ShowLine, appearance.BlurRadius);
            _taskbarService?.ApplyAppearance(previewAppearance);
        }
    }

    private void UpdateStateColor(Models.DynamicState state, Color color)
    {
        var config = _configService!.Config;
        var appearance = GetAppearanceForState(state, config);
        if (appearance != null)
        {
            appearance.Color = color;
        }

        _configService.Save();
        _stateResolverService?.Refresh();
    }

    private static TaskbarAppearance? GetAppearanceForState(Models.DynamicState state, AppConfig config) => state switch
    {
        Models.DynamicState.Desktop => config.DesktopAppearance,
        Models.DynamicState.VisibleWindow => config.VisibleWindowAppearance,
        Models.DynamicState.MaximizedWindow => config.MaximizedWindowAppearance,
        Models.DynamicState.StartOpened => config.StartOpenedAppearance,
        Models.DynamicState.SearchOpened => config.SearchOpenedAppearance,
        Models.DynamicState.TaskViewOpened => config.TaskViewOpenedAppearance,
        Models.DynamicState.BatterySaver => config.BatterySaverAppearance,
        _ => null,
    };

    private void OnSettingsRequested()
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            ShowSettingsWindow();
        });
    }

    private void OnAboutRequested()
    {
        ShowWelcomePage();
    }

    private void OnExitRequested()
    {
        ExitApp();
    }

    private void ShowWelcomePageIfNeeded()
    {
        if (_configService!.IsFirstRun)
        {
            ShowWelcomePage();
        }
    }

    private void ShowWelcomePage()
    {
        if (_welcomeWindow != null)
        {
            _welcomeWindow.Activate();
            return;
        }

        var welcomePage = new WelcomePage(_localizationService!);
        welcomePage.OkRequested += () =>
        {
            _welcomeWindow?.Close();
        };

        _welcomeWindow = new Window
        {
            Title = _localizationService!.GetString("Welcome_Title.Text"),
            Content = new Grid
            {
                Children = { welcomePage }
            },
        };

        ConfigureWindowSize(_welcomeWindow, 560, 520);

        _welcomeWindow.Closed += (s, e) =>
        {
            _welcomeWindow = null;
        };

        _welcomeWindow.Activate();
    }

    private static void ConfigureWindowSize(Window window, int width, int height, bool center = true)
    {
        try
        {
            var appWindow = window.AppWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            uint dpi = NativeMethods.User32.GetDpiForWindow(hWnd);
            if (dpi == 0) dpi = 96;
            float scale = dpi / 96.0f;
            int physWidth = (int)Math.Round(width * scale);
            int physHeight = (int)Math.Round(height * scale);

            appWindow.Resize(new Windows.Graphics.SizeInt32(physWidth, physHeight));
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
            }

            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
            catch { }

            if (center)
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var centeredPosition = appWindow.Position;
                    centeredPosition.X = ((displayArea.WorkArea.Width - physWidth) / 2) + displayArea.WorkArea.X;
                    centeredPosition.Y = ((displayArea.WorkArea.Height - physHeight) / 2) + displayArea.WorkArea.Y;
                    appWindow.Move(centeredPosition);
                }
            }
        }
        catch { }
    }

    private void ExitApp()
    {
        _logger?.Info("Application shutting down");

        _trayContextMenuService?.Dispose();
        _trayIconService?.Dispose();
        _appVisibilityService?.Dispose();
        _windowMonitorService?.Dispose();
        _taskbarService?.Dispose();
        _configService?.Dispose();
        _logger?.Dispose();

        _mutex?.Dispose();

        Exit();
    }
}
