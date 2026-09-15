using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel;

namespace TranslucentTabBar.Services;

/// <summary>
/// Manages auto-start registration for the application.
/// Supports both MSIX StartupTask and registry-based startup for portable mode.
/// </summary>
public class StartupManager
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "TranslucentTabBar";
    private const string TaskId = "TranslucentTabBarStartup";

    private readonly LoggerService _logger;
    private bool _cachedIsRegistered;

    public StartupManager(LoggerService logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes the cached startup state asynchronously.
    /// Must be called once during app startup before reading <see cref="IsRegistered"/>.
    /// </summary>
    public async Task InitializeAsync()
    {
        _cachedIsRegistered = await CheckRegisteredAsync();
        _logger.Info($"Startup state initialized: registered={_cachedIsRegistered}");
    }

    /// <summary>
    /// Checks if the app is registered to start at boot (returns cached value).
    /// </summary>
    public bool IsRegistered => _cachedIsRegistered;

    /// <summary>
    /// Registers or unregisters the app for auto-start.
    /// </summary>
    public async Task SetRegisteredAsync(bool enable)
    {
        try
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                _logger.Info($"StartupTask {TaskId}: current state={task.State} before {(enable ? "enable" : "disable")}");
                if (enable)
                {
                    var state = await task.RequestEnableAsync();
                    _logger.Info($"StartupTask requested enable: state={state}");
                }
                else
                {
                    task.Disable();
                    _logger.Info($"StartupTask disabled: state={task.State}");
                }
            }
            else
            {
                SetRegistryStartup(enable);
                _logger.Info($"Registry startup set to {enable}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"StartupTask API failed ({ex.Message}), using registry fallback");
            SetRegistryStartup(enable);
        }

        _cachedIsRegistered = await CheckRegisteredAsync();
    }

    private async Task<bool> CheckRegisteredAsync()
    {
        try
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                _logger.Debug($"StartupTask state check: {task.State} (Enabled=2, EnabledByPolicy=4)");
                return task.State == StartupTaskState.Enabled || task.State == StartupTaskState.EnabledByPolicy;
            }

            return CheckRegistryStartup();
        }
        catch (Exception ex)
        {
            _logger.Warn($"StartupTask check failed ({ex.Message}), falling back to registry");
            return CheckRegistryStartup();
        }
    }

    /// <summary>
    /// Whether the app is running as a packaged MSIX app.
    /// </summary>
    private static bool IsPackaged
    {
        get
        {
            try
            {
                return Package.Current != null;
            }
            catch
            {
                return false;
            }
        }
    }

    private bool CheckRegistryStartup()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryRunPath);
            if (key == null) return false;
            var value = key.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    private void SetRegistryStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                var command = BuildRegistryCommand();
                if (command != null)
                {
                    key.SetValue(AppName, command);
                }
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set registry startup: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the Run-key command. A packaged app must be started through its AUMID:
    /// a direct WindowsApps exe path breaks on every package update.
    /// </summary>
    private string? BuildRegistryCommand()
    {
        try
        {
            var family = Package.Current.Id.FamilyName;
            if (!string.IsNullOrEmpty(family))
            {
                return $"explorer.exe shell:AppsFolder\\{family}!App";
            }
        }
        catch
        {
            // not packaged
        }

        var exePath = Environment.ProcessPath;
        return exePath == null ? null : $"\"{exePath}\"";
    }
}
