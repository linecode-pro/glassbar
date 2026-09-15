using System.Text.Json;
using TranslucentTabBar.Models;
using Windows.Storage;

namespace TranslucentTabBar.Services;

/// <summary>
/// Manages loading, saving, and watching the application configuration (settings.json).
/// </summary>
public class ConfigService : IDisposable
{
    private const string ConfigFileName = "settings.json";

    private readonly string _configPath;
    private readonly FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private DateTime _lastSaveUtc;
    private bool _disposed;

    /// <summary>
    /// Gets the current application configuration.
    /// </summary>
    public AppConfig Config { get; private set; } = new();

    /// <summary>
    /// Gets whether this is the first run (config file did not exist).
    /// </summary>
    public bool IsFirstRun { get; private set; }

    /// <summary>
    /// Raised when the configuration is reloaded from disk.
    /// </summary>
    public event Action<AppConfig>? ConfigReloaded;

    /// <summary>
    /// Raised when a specific value changes.
    /// </summary>
    public event Action? ConfigChanged;

    public ConfigService()
    {
        // Determine config path: use local folder if packaged, otherwise next to the executable
        try
        {
            _configPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, ConfigFileName);
        }
        catch
        {
            // Fallback for unpackaged scenarios
            _configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        }

        Load();

        // Set up file watcher to detect external changes
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null && Directory.Exists(dir))
            {
                _watcher = new FileSystemWatcher(dir)
                {
                    Filter = ConfigFileName,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnConfigFileChanged;
                _watcher.Created += OnConfigFileChanged;
            }
        }
        catch
        {
            // File watching may fail in some environments - that's ok
        }
    }

    /// <summary>
    /// Loads configuration from disk. Creates a default config if the file doesn't exist.
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                Config = AppConfig.FromJson(json);
            }
            else
            {
                IsFirstRun = true;
                Config = new AppConfig();
                if (!Config.DisableSaving)
                {
                    Save();
                }
            }
        }
        catch
        {
            Config = new AppConfig();
        }
    }

    /// <summary>
    /// Resets configuration to defaults, saving to disk and notifying listeners.
    /// </summary>
    public void ResetToDefaults()
    {
        var currentLang = Config.Language;
        Config = new AppConfig();
        if (!string.IsNullOrEmpty(currentLang))
        {
            Config.Language = currentLang;
        }

        Save();
        ConfigReloaded?.Invoke(Config);
        ConfigChanged?.Invoke();
    }

    /// <summary>
    /// Saves the current configuration to disk.
    /// </summary>
    public void Save()
    {
        if (Config.DisableSaving)
            return;

        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = Config.ToJson();
            _lastSaveUtc = DateTime.UtcNow;
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the settings file in the default editor.
    /// </summary>
    public void OpenConfigFile()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _configPath,
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open config file: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public string GetConfigPath() => _configPath;

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore change events caused by our own Save()
        if ((DateTime.UtcNow - _lastSaveUtc).TotalMilliseconds < 750)
            return;

        // Debounce: file system events can fire multiple times
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            try
            {
                Load();
                ConfigReloaded?.Invoke(Config);
                ConfigChanged?.Invoke();
            }
            catch { }
        }, null, 500, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
