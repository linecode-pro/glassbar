using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using TranslucentTabBar.NativeMethods;

namespace TranslucentTabBar.Services;

/// <summary>
/// Monitors window state changes (maximized, visible, start menu, search, task view, battery saver)
/// using WinEvent hooks and other Windows APIs.
/// </summary>
public class WindowMonitorService : IDisposable
{
    /// <summary>
    /// Window classes to ignore when tracking visible/maximized windows.
    /// These are system or framework windows that shouldn't trigger state changes.
    /// </summary>
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Composition.DesktopWindowContentBridge",
        "SysListView32",
        "WorkerW",
        "Progman",
        "DesktopWindowXamlSource",
        "ApplicationFrameWindow",
        "Windows.UI.Core.CoreWindow",
        "ImmersiveLauncher",
        "SearchPane",
        "Cortana",
        "MultitaskingViewFrame",
        "Xaml_Window",
        "PopupHost",
        "WindowsInternal.ComposableShell.Experiences.Window",
        "SystemSettingsFrame",
        "NotifyIconOverflowWindow",
        "NativeHWNDHost",
        "WindowsTaskbarShadow",
        "TaskbarFrame",
        "StartMenuFrame",
        "SearchFrame",
        "Windows.UI.Input.InputHostWindow",
        "ApplicationManagerFrame",
        "NarratorHost",
        "Net UI Tooltip Window",
        "Windows.UI.Input.SystemTextWindow",
    };

    private readonly LoggerService _logger;
    private AppVisibilityService? _appVisibility;
    private readonly List<nint> _hookHandles = new();
    private readonly ConcurrentDictionary<nint, WindowInfo> _trackedWindows = new();
    private readonly ConcurrentDictionary<uint, string> _processNameCache = new();
    private readonly HashSet<nint> _maximizedWindows = new();
    private readonly HashSet<nint> _visibleWindows = new();
    private readonly object _lock = new();
    private bool _disposed;

    private nint _foregroundWindow;
    private nint _lastForegroundWindow;

    // State flags
    private bool _isStartOpened;
    private bool _isSearchOpened;
    private bool _isTaskViewOpened;
    private bool _isBatterySaverActive;
    private bool _hasMaximizedWindow;
    private bool _hasVisibleWindow;

    private bool _lastFiredStart;
    private bool _lastFiredSearch;
    private bool _lastFiredTaskView;
    private bool _lastFiredBattery;

    private readonly WinEventProc _foregroundProc;
    private readonly WinEventProc _minimizeProc;
    private readonly WinEventProc _locationProc;
    private readonly WinEventProc _showHideProc;
    private readonly WinEventProc _createDestroyProc;
    private readonly WinEventProc _cloakProc;

    // GCHandle to keep delegates alive
    private readonly List<GCHandle> _delegateHandles = new();

    /// <summary>
    /// Raised when any window state changes that could affect the taskbar appearance.
    /// </summary>
    public event Action? StateChanged;

    public bool IsStartOpened => _isStartOpened;
    public bool IsSearchOpened => _isSearchOpened;
    public bool IsTaskViewOpened => _isTaskViewOpened;
    public bool IsBatterySaverActive => _isBatterySaverActive;
    public bool HasMaximizedWindow => _hasMaximizedWindow;
    public bool HasVisibleWindow => _hasVisibleWindow;

    /// <summary>
    /// Sets the AppVisibilityService for reliable Start Menu detection.
    /// </summary>
    public void SetAppVisibilityService(AppVisibilityService appVisibility)
    {
        _appVisibility = appVisibility;
    }

    /// <summary>
    /// Refreshes the start menu state from the AppVisibility service.
    /// </summary>
    public void RefreshStartMenuState()
    {
        if (_appVisibility != null)
        {
            _appVisibility.Refresh();
            _isStartOpened = _appVisibility.IsLauncherVisible;
        }
    }

    public WindowMonitorService(LoggerService logger)
    {
        _logger = logger;
        _logger.Debug("WindowMonitorService initializing");

        // Create delegate instances and keep them alive via GCHandle
        _foregroundProc = OnForegroundChanged;
        _locationProc = OnLocationChanged;
        _minimizeProc = OnMinimizeStateChanged;
        _showHideProc = OnShowHideChanged;
        _createDestroyProc = OnCreateDestroyChanged;
        _cloakProc = OnCloakChanged;

        _delegateHandles.Add(GCHandle.Alloc(_foregroundProc));
        _delegateHandles.Add(GCHandle.Alloc(_locationProc));
        _delegateHandles.Add(GCHandle.Alloc(_minimizeProc));
        _delegateHandles.Add(GCHandle.Alloc(_showHideProc));
        _delegateHandles.Add(GCHandle.Alloc(_createDestroyProc));
        _delegateHandles.Add(GCHandle.Alloc(_cloakProc));

        InstallHooks();
        EnumerateExistingWindows();
        CheckBatterySaver();
    }

    private void InstallHooks()
    {
        var hook = User32.SetWinEventHook(
            WinEventConstants.EVENT_SYSTEM_FOREGROUND,
            WinEventConstants.EVENT_SYSTEM_FOREGROUND,
            nint.Zero,
            _foregroundProc,
            0, 0,
            WinEventConstants.WINEVENT_OUTOFCONTEXT | WinEventConstants.WINEVENT_SKIPOWNPROCESS);
        if (hook != nint.Zero) _hookHandles.Add(hook);

        hook = User32.SetWinEventHook(
            WinEventConstants.EVENT_OBJECT_LOCATIONCHANGE,
            WinEventConstants.EVENT_OBJECT_LOCATIONCHANGE,
            nint.Zero,
            _locationProc,
            0, 0,
            WinEventConstants.WINEVENT_OUTOFCONTEXT | WinEventConstants.WINEVENT_SKIPOWNPROCESS);
        if (hook != nint.Zero) _hookHandles.Add(hook);

        hook = User32.SetWinEventHook(
            WinEventConstants.EVENT_SYSTEM_MINIMIZESTART,
            WinEventConstants.EVENT_SYSTEM_MINIMIZEEND,
            nint.Zero,
            _minimizeProc,
            0, 0,
            WinEventConstants.WINEVENT_OUTOFCONTEXT | WinEventConstants.WINEVENT_SKIPOWNPROCESS);
        if (hook != nint.Zero) _hookHandles.Add(hook);

        hook = User32.SetWinEventHook(
            WinEventConstants.EVENT_OBJECT_SHOW,
            WinEventConstants.EVENT_OBJECT_HIDE,
            nint.Zero,
            _showHideProc,
            0, 0,
            WinEventConstants.WINEVENT_OUTOFCONTEXT | WinEventConstants.WINEVENT_SKIPOWNPROCESS);
        if (hook != nint.Zero) _hookHandles.Add(hook);

        hook = User32.SetWinEventHook(
            WinEventConstants.EVENT_OBJECT_CREATE,
            WinEventConstants.EVENT_OBJECT_DESTROY,
            nint.Zero,
            _createDestroyProc,
            0, 0,
            WinEventConstants.WINEVENT_OUTOFCONTEXT | WinEventConstants.WINEVENT_SKIPOWNPROCESS);
        if (hook != nint.Zero) _hookHandles.Add(hook);

        hook = User32.SetWinEventHook(
            WinEventConstants.EVENT_OBJECT_CLOAKED,
            WinEventConstants.EVENT_OBJECT_UNCLOAKED,
            nint.Zero,
            _cloakProc,
            0, 0,
            WinEventConstants.WINEVENT_OUTOFCONTEXT | WinEventConstants.WINEVENT_SKIPOWNPROCESS);
        if (hook != nint.Zero) _hookHandles.Add(hook);
    }

    private void EnumerateExistingWindows()
    {
        User32.EnumWindows((hwnd, _) =>
        {
            TrackWindow(hwnd);
            return true;
        }, nint.Zero);
    }

    private void TrackWindow(nint hwnd)
    {
        if (hwnd == nint.Zero) return;
        if (!User32.IsWindowVisible(hwnd)) return;

        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId) return;

        var className = GetClassName(hwnd);
        if (string.IsNullOrEmpty(className)) return;

        // Skip ignored system windows
        if (IgnoredClasses.Contains(className))
            return;

        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        User32.GetWindowPlacement(hwnd, ref placement);

        var info = new WindowInfo
        {
            Handle = hwnd,
            ClassName = className,
            IsMinimized = placement.showCmd == ShowWindowCommands.SW_SHOWMINIMIZED,
            IsMaximized = placement.showCmd == ShowWindowCommands.SW_SHOWMAXIMIZED,
        };

        _trackedWindows[hwnd] = info;

        lock (_lock)
        {
            if (!info.IsMinimized && info.IsMaximized)
                _maximizedWindows.Add(hwnd);

            if (!info.IsMinimized)
                _visibleWindows.Add(hwnd);
        }
    }

    private void OnForegroundChanged(nint hWinEventHook, uint eventType, nint hwnd, long idObject, long idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == nint.Zero || idObject != 0) return;

        _lastForegroundWindow = _foregroundWindow;
        _foregroundWindow = hwnd;

        // Detect shell states (Search, Start, Task View)
        UpdateShellStates(hwnd);

        // Re-evaluate maximized/visible state
        RecalculateStates();
    }

    private void OnLocationChanged(nint hWinEventHook, uint eventType, nint hwnd, long idObject, long idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == nint.Zero || idObject != 0) return;

        // Check if a window was maximized/restored
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!User32.GetWindowPlacement(hwnd, ref placement)) return;

        bool changed = false;
        lock (_lock)
        {
            bool wasMaximized = _maximizedWindows.Contains(hwnd);
            bool isNowMaximized = placement.showCmd == ShowWindowCommands.SW_SHOWMAXIMIZED;

            if (isNowMaximized && !wasMaximized)
            {
                _maximizedWindows.Add(hwnd);
                changed = true;
            }
            else if (!isNowMaximized && wasMaximized)
            {
                _maximizedWindows.Remove(hwnd);
                changed = true;
            }

            bool wasVisible = _visibleWindows.Contains(hwnd);
            bool isNowVisible = placement.showCmd != ShowWindowCommands.SW_SHOWMINIMIZED
                                && User32.IsWindowVisible(hwnd);

            if (isNowVisible && !wasVisible)
            {
                _visibleWindows.Add(hwnd);
                changed = true;
            }
            else if (!isNowVisible && wasVisible)
            {
                _visibleWindows.Remove(hwnd);
                changed = true;
            }
        }

        if (changed)
        {
            RecalculateStates();
        }
    }

    private void OnMinimizeStateChanged(nint hWinEventHook, uint eventType, nint hwnd, long idObject, long idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == nint.Zero || idObject != 0) return;

        bool isMinimizeStart = eventType == WinEventConstants.EVENT_SYSTEM_MINIMIZESTART;

        lock (_lock)
        {
            if (isMinimizeStart)
            {
                _visibleWindows.Remove(hwnd);
                _maximizedWindows.Remove(hwnd);
            }
            else
            {
                // Window was restored - check its state
                var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (User32.GetWindowPlacement(hwnd, ref placement))
                {
                    if (placement.showCmd == ShowWindowCommands.SW_SHOWMAXIMIZED)
                        _maximizedWindows.Add(hwnd);
                    _visibleWindows.Add(hwnd);
                }
            }
        }

        RecalculateStates();
    }

    private void OnShowHideChanged(nint hWinEventHook, uint eventType, nint hwnd, long idObject, long idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == nint.Zero || idObject != 0) return;

        bool isShown = eventType == WinEventConstants.EVENT_OBJECT_SHOW;
        var procName = GetProcessName(hwnd);
        var className = GetClassName(hwnd);

        if (procName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
            procName.Equals("SearchApp", StringComparison.OrdinalIgnoreCase))
        {
            _isSearchOpened = isShown;
            RecalculateStates();
            return;
        }

        if (procName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase))
        {
            _isStartOpened = isShown;
            RecalculateStates();
            return;
        }

        if (className.Equals("MultitaskingViewFrame", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase))
        {
            _isTaskViewOpened = isShown;
            RecalculateStates();
            return;
        }

        lock (_lock)
        {
            if (isShown)
            {
                if (User32.IsWindowVisible(hwnd))
                    _visibleWindows.Add(hwnd);
            }
            else
            {
                _visibleWindows.Remove(hwnd);
                _maximizedWindows.Remove(hwnd);
            }
        }

        RecalculateStates();
    }

    private void OnCreateDestroyChanged(nint hWinEventHook, uint eventType, nint hwnd, long idObject, long idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == nint.Zero || idObject != 0) return;

        if (eventType == WinEventConstants.EVENT_OBJECT_DESTROY)
        {
            lock (_lock)
            {
                _visibleWindows.Remove(hwnd);
                _maximizedWindows.Remove(hwnd);
            }
            _trackedWindows.TryRemove(hwnd, out _);
            RecalculateStates();
        }
        else if (eventType == WinEventConstants.EVENT_OBJECT_CREATE)
        {
            TrackWindow(hwnd);
        }
    }

    private void OnCloakChanged(nint hWinEventHook, uint eventType, nint hwnd, long idObject, long idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == nint.Zero || idObject != 0) return;

        bool isCloaked = eventType == WinEventConstants.EVENT_OBJECT_CLOAKED;
        var procName = GetProcessName(hwnd);
        var className = GetClassName(hwnd);

        if (procName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
            procName.Equals("SearchApp", StringComparison.OrdinalIgnoreCase))
        {
            _isSearchOpened = !isCloaked;
            RecalculateStates();
            return;
        }

        if (procName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase))
        {
            _isStartOpened = !isCloaked;
            RecalculateStates();
            return;
        }

        if (className.Equals("MultitaskingViewFrame", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase))
        {
            _isTaskViewOpened = !isCloaked;
            RecalculateStates();
            return;
        }

        lock (_lock)
        {
            if (isCloaked)
            {
                _visibleWindows.Remove(hwnd);
                _maximizedWindows.Remove(hwnd);
            }
            else
            {
                if (User32.IsWindowVisible(hwnd))
                    _visibleWindows.Add(hwnd);

                var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (User32.GetWindowPlacement(hwnd, ref placement) && placement.showCmd == ShowWindowCommands.SW_SHOWMAXIMIZED)
                    _maximizedWindows.Add(hwnd);
            }
        }

        RecalculateStates();
    }

    private void UpdateShellStates(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            _isSearchOpened = false;
            _isTaskViewOpened = false;
            _isStartOpened = _appVisibility?.IsLauncherVisible ?? false;
            return;
        }

        var procName = GetProcessName(hwnd);
        var className = GetClassName(hwnd);
        var title = GetWindowTitle(hwnd);

        // Search detection:
        // Windows 10 & 11: SearchHost.exe or SearchApp.exe, or classes SearchPane, Cortana
        bool isSearch = procName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                        procName.Equals("SearchApp", StringComparison.OrdinalIgnoreCase) ||
                        className.Equals("SearchPane", StringComparison.OrdinalIgnoreCase) ||
                        className.Equals("Cortana", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Search", StringComparison.OrdinalIgnoreCase) ||
                        title.Equals("Поиск", StringComparison.OrdinalIgnoreCase);

        // Start menu detection:
        // StartMenuExperienceHost.exe, ImmersiveLauncher, or IAppVisibility
        bool isStart = procName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                       className.Equals("ImmersiveLauncher", StringComparison.OrdinalIgnoreCase) ||
                       (_appVisibility != null && _appVisibility.IsLauncherVisible);

        // Task View detection:
        // MultitaskingViewFrame (Win10) or XamlExplorerHostIslandWindow (Win11),
        // or explorer.exe with "Task View" / "Представление задач" in title.
        bool isTaskView = className.Equals("MultitaskingViewFrame", StringComparison.OrdinalIgnoreCase) ||
                          className.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase) ||
                          ((procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(procName)) &&
                           (title.Contains("Task View", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Представление задач", StringComparison.OrdinalIgnoreCase)));

        _isSearchOpened = isSearch;
        _isStartOpened = isStart;
        _isTaskViewOpened = isTaskView;

        _logger.Debug($"Shell states: Start={_isStartOpened}, Search={_isSearchOpened}, TaskView={_isTaskViewOpened} (hwnd=0x{hwnd:X}, proc={procName}, class={className}, title={title})");
    }

    private void RecalculateStates()
    {
        bool prevMaximized, prevVisible, prevStart, prevSearch, prevTaskView, prevBattery;

        lock (_lock)
        {
            prevMaximized = _hasMaximizedWindow;
            prevVisible = _hasVisibleWindow;
            prevStart = _lastFiredStart;
            prevSearch = _lastFiredSearch;
            prevTaskView = _lastFiredTaskView;
            prevBattery = _lastFiredBattery;

            _hasMaximizedWindow = _maximizedWindows.Count > 0;
            _hasVisibleWindow = _visibleWindows.Count > 0;
            _lastFiredStart = _isStartOpened;
            _lastFiredSearch = _isSearchOpened;
            _lastFiredTaskView = _isTaskViewOpened;
            _lastFiredBattery = _isBatterySaverActive;
        }

        // Only fire if something actually changed
        if (prevMaximized != _hasMaximizedWindow ||
            prevVisible != _hasVisibleWindow ||
            prevStart != _lastFiredStart ||
            prevSearch != _lastFiredSearch ||
            prevTaskView != _lastFiredTaskView ||
            prevBattery != _lastFiredBattery)
        {
            _logger.Debug($"StateChanged firing: Start={_lastFiredStart}, Search={_lastFiredSearch}, TaskView={_lastFiredTaskView}, Max={_hasMaximizedWindow}, Vis={_hasVisibleWindow}, Battery={_lastFiredBattery}");
            StateChanged?.Invoke();
        }
    }

    private void CheckBatterySaver()
    {
        // Check power status for battery saver using Windows.System.Power
        try
        {
            var status = Windows.System.Power.PowerManager.EnergySaverStatus;
            _isBatterySaverActive = status == Windows.System.Power.EnergySaverStatus.On;
        }
        catch
        {
            _isBatterySaverActive = false;
        }
    }

    /// <summary>
    /// Updates the battery saver state (should be called periodically or on power change).
    /// </summary>
    public void UpdateBatterySaverState()
    {
        CheckBatterySaver();
        RecalculateStates();
    }

    /// <summary>
    /// Sets the start menu opened state manually (e.g., from IAppVisibility).
    /// </summary>
    public void SetStartOpened(bool opened)
    {
        _isStartOpened = opened;
        RecalculateStates();
    }

    /// <summary>
    /// Sets the search opened state manually.
    /// </summary>
    public void SetSearchOpened(bool opened)
    {
        _isSearchOpened = opened;
        RecalculateStates();
    }

    /// <summary>
    /// Sets the task view opened state manually.
    /// </summary>
    public void SetTaskViewOpened(bool opened)
    {
        _isTaskViewOpened = opened;
        RecalculateStates();
    }

    /// <summary>
    /// Returns the number of tracked visible windows.
    /// </summary>
    public int GetVisibleWindowCount()
    {
        lock (_lock) { return _visibleWindows.Count; }
    }

    /// <summary>
    /// Returns the number of tracked maximized windows.
    /// </summary>
    public int GetMaximizedWindowCount()
    {
        lock (_lock) { return _maximizedWindows.Count; }
    }

    private string GetProcessName(nint hwnd)
    {
        if (hwnd == nint.Zero) return string.Empty;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return string.Empty;

        if (_processNameCache.TryGetValue(pid, out var cachedName))
            return cachedName;

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = proc.ProcessName;
            _processNameCache[pid] = name;
            return name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowTitle(nint hwnd)
    {
        if (hwnd == nint.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        User32.GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        User32.GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var hook in _hookHandles)
        {
            if (hook != nint.Zero)
                User32.UnhookWinEvent(hook);
        }
        _hookHandles.Clear();

        foreach (var handle in _delegateHandles)
            handle.Free();
        _delegateHandles.Clear();

        lock (_lock)
        {
            _maximizedWindows.Clear();
            _visibleWindows.Clear();
        }
        _trackedWindows.Clear();
        _processNameCache.Clear();
    }

    internal class WindowInfo
    {
        public nint Handle { get; set; }
        public string? ClassName { get; set; }
        public bool IsMinimized { get; set; }
        public bool IsMaximized { get; set; }
    }
}
