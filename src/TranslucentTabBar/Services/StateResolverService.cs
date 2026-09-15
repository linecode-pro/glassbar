using TranslucentTabBar.Models;

namespace TranslucentTabBar.Services;

/// <summary>
/// Resolves which taskbar state is currently active based on window conditions
/// and determines which appearance to apply.
/// </summary>
public class StateResolverService
{
    private readonly WindowMonitorService _windowMonitor;
    private readonly ConfigService _configService;
    private readonly LoggerService? _logger;

    private TaskbarAccentMode _lastAccent;
    private Windows.UI.Color _lastColor;
    private float _lastBlurRadius;
    private bool _lastShowPeek;
    private bool _lastShowLine;
    private bool _hasLastAppearance;

    // Priority order: BatterySaver > Start/Search/TaskView > Maximized > Visible > Desktop
    private static readonly DynamicState[] PriorityOrder =
    [
        DynamicState.BatterySaver,
        DynamicState.StartOpened,
        DynamicState.SearchOpened,
        DynamicState.TaskViewOpened,
        DynamicState.MaximizedWindow,
        DynamicState.VisibleWindow,
        DynamicState.Desktop,
    ];

    /// <summary>
    /// Raised when the active state changes and the appearance should be updated.
    /// </summary>
    public event Action<TaskbarAppearance>? AppearanceChanged;

    public StateResolverService(WindowMonitorService windowMonitor, ConfigService configService, LoggerService? logger = null)
    {
        _windowMonitor = windowMonitor;
        _configService = configService;
        _logger = logger;

        _windowMonitor.StateChanged += OnWindowStateChanged;
        _configService.ConfigReloaded += _ => Refresh();
        _configService.ConfigChanged += () => Refresh();
    }

    /// <summary>
    /// Gets the currently active dynamic state.
    /// </summary>
    public DynamicState GetActiveState()
    {
        var config = _configService.Config;

        // Check in priority order
        if (config.BatterySaverAppearance.Enabled && _windowMonitor.IsBatterySaverActive)
            return DynamicState.BatterySaver;

        if (config.StartOpenedAppearance.Enabled && _windowMonitor.IsStartOpened)
            return DynamicState.StartOpened;

        if (config.SearchOpenedAppearance.Enabled && _windowMonitor.IsSearchOpened)
            return DynamicState.SearchOpened;

        if (config.TaskViewOpenedAppearance.Enabled && _windowMonitor.IsTaskViewOpened)
            return DynamicState.TaskViewOpened;

        if (config.MaximizedWindowAppearance.Enabled && _windowMonitor.HasMaximizedWindow)
            return DynamicState.MaximizedWindow;

        if (config.VisibleWindowAppearance.Enabled && _windowMonitor.HasVisibleWindow)
            return DynamicState.VisibleWindow;

        return DynamicState.Desktop;
    }

    /// <summary>
    /// Gets the appearance for the currently active state.
    /// </summary>
    public TaskbarAppearance GetActiveAppearance()
    {
        var config = _configService.Config;
        var state = GetActiveState();

        return state switch
        {
            DynamicState.Desktop => config.DesktopAppearance,
            DynamicState.VisibleWindow => config.VisibleWindowAppearance,
            DynamicState.MaximizedWindow => config.MaximizedWindowAppearance,
            DynamicState.StartOpened => config.StartOpenedAppearance,
            DynamicState.SearchOpened => config.SearchOpenedAppearance,
            DynamicState.TaskViewOpened => config.TaskViewOpenedAppearance,
            DynamicState.BatterySaver => config.BatterySaverAppearance,
            _ => config.DesktopAppearance,
        };
    }

    /// <summary>
    /// Forces a re-evaluation and raises AppearanceChanged if the state changed.
    /// When <paramref name="force"/> is set, the dedup cache is dropped so the
    /// appearance is raised even if it is identical to the last one — needed after
    /// startup or an Explorer restart, when nothing has actually been applied yet.
    /// </summary>
    public void Refresh(bool force = false)
    {
        if (force)
        {
            _hasLastAppearance = false;
        }

        OnWindowStateChanged();
    }

    private void OnWindowStateChanged()
    {
        var state = GetActiveState();
        var appearance = GetActiveAppearance();

        if (_hasLastAppearance &&
            appearance.Accent == _lastAccent &&
            appearance.Color.Equals(_lastColor) &&
            Math.Abs(appearance.BlurRadius - _lastBlurRadius) < 0.01f &&
            appearance.ShowPeek == _lastShowPeek &&
            appearance.ShowLine == _lastShowLine)
        {
            return;
        }

        _lastAccent = appearance.Accent;
        _lastColor = appearance.Color;
        _lastBlurRadius = appearance.BlurRadius;
        _lastShowPeek = appearance.ShowPeek;
        _lastShowLine = appearance.ShowLine;
        _hasLastAppearance = true;

        _logger?.Debug($"State resolved: {state}, Accent={appearance.Accent}, Color=0x{appearance.Color.A:X2}{appearance.Color.R:X2}{appearance.Color.G:X2}{appearance.Color.B:X2}");
        AppearanceChanged?.Invoke(appearance);
    }
}
