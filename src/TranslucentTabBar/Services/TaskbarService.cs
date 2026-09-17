using System.Runtime.InteropServices;
using TranslucentTabBar.Helpers;
using TranslucentTabBar.Models;
using TranslucentTabBar.NativeMethods;
using Windows.UI;

namespace TranslucentTabBar.Services;

/// <summary>
/// Core service that manages taskbar appearance by calling the undocumented
/// SetWindowCompositionAttribute API.
/// </summary>
public class TaskbarService : IDisposable
{
    private readonly Dictionary<nint, TaskbarInfo> _taskbars = new();
    private readonly LoggerService? _logger;
    private ITaskbarAppearanceService? _appearanceService;
    private bool _tapInitialized;
    private bool _disposed;
    private int _tapAttemptInProgress;
    private int _tapFailureStreak;
    private DateTime _nextTapAttemptUtc;
    private int _tapRetryLoopRunning;
    private TaskbarAppearance? _lastAppearance;
    private Timer? _reapplyTimer;
    private bool _warnedLegacyFallback;

    /// <summary>
    /// Raised when the ExplorerTAP appearance service becomes available. Fires on a
    /// thread pool thread, so subscribers have to marshal to their UI thread.
    /// </summary>
    public event Action? TapBecameAvailable;

    private void EnsureExplorerTap(nint primaryTaskbarHwnd)
    {
        if (_tapInitialized || _disposed || primaryTaskbarHwnd == nint.Zero) return;

        if (Environment.OSVersion.Version.Build < 22000)
        {
            _logger?.Info("Pre-Windows 11 build, ExplorerTAP not needed.");
            _tapInitialized = true;
            return;
        }

        // At logon we can start long before Explorer finished building the taskbar XAML, and
        // with an idle desktop no state change would ever re-trigger an attempt - so retry
        // on our own schedule instead of relying on external refresh events.
        StartTapRetryLoop();

        if (DateTime.UtcNow < _nextTapAttemptUtc) return;
        if (Interlocked.CompareExchange(ref _tapAttemptInProgress, 1, 0) != 0) return;

        // The TAP gives up waiting for Explorer after 35 s, and a cold install can take
        // longer than that (Explorer still starting, unsigned payload being scanned), so
        // this runs outside the UI thread and is retried until Explorer reports ready.
        _ = Task.Run(TryInjectExplorerTapAsync);
    }

    private void StartTapRetryLoop()
    {
        if (_tapInitialized || _disposed) return;
        if (Interlocked.CompareExchange(ref _tapRetryLoopRunning, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_tapInitialized && !_disposed)
                {
                    double seconds = Math.Max(2.0, (_nextTapAttemptUtc - DateTime.UtcNow).TotalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(seconds));

                    if (_tapInitialized || _disposed) return;

                    // Explorer not up yet (or restarting) - just keep waiting.
                    if (User32.FindWindowW("Shell_TrayWnd", null) == nint.Zero) continue;
                    if (Interlocked.CompareExchange(ref _tapAttemptInProgress, 1, 0) != 0) continue;

                    TryInjectExplorerTapAsync();
                }
            }
            finally
            {
                Volatile.Write(ref _tapRetryLoopRunning, 0);
            }
        });
    }

    private void TryInjectExplorerTapAsync()
    {
        bool succeeded = false;
        try
        {
            succeeded = TryInjectExplorerTap();
        }
        catch (Exception ex)
        {
            _logger?.Error($"Error initializing ExplorerTAP: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _tapAttemptInProgress, 0);

            if (succeeded)
            {
                _tapFailureStreak = 0;
                if (!_disposed)
                {
                    StartReapplyTimer();
                    TapBecameAvailable?.Invoke();
                }
            }
            else
            {
                _tapFailureStreak++;
                double seconds = Math.Min(15.0 * Math.Pow(2, _tapFailureStreak - 1), 300.0);
                _nextTapAttemptUtc = DateTime.UtcNow.AddSeconds(seconds);
                _logger?.Warn($"ExplorerTAP unavailable, retrying in {seconds:F0}s");
            }
        }
    }

    private bool TryInjectExplorerTap()
    {
        var primaryTaskbarHwnd = User32.FindWindowW("Shell_TrayWnd", null);
        if (primaryTaskbarHwnd == nint.Zero)
        {
            _logger?.Warn("Shell_TrayWnd not found while injecting ExplorerTAP");
            return false;
        }

        try
        {
            string sourceDll = Path.Combine(AppContext.BaseDirectory, "Assets", "ExplorerTAP.dll");
            if (!File.Exists(sourceDll))
            {
                sourceDll = Path.Combine(AppContext.BaseDirectory, "ExplorerTAP.dll");
            }

            if (!File.Exists(sourceDll))
            {
                _logger?.Error($"ExplorerTAP.dll not found at {sourceDll}");
                return false;
            }

            // A previous failed attempt may have left the named ready event set while
            // nothing inside Explorer will ever set it again for a fresh injection.
            // Reset it so the wait below reflects this attempt instead of returning
            // instantly against a stale state.
            if (_tapFailureStreak > 0)
            {
                ResetStaleTapReadyEvent();
            }

            string loadDll;
            if (IsPackaged)
            {
                // Upstream TranslucentTB (Store-proven): "copy the file over to a place
                // Explorer can read. It can't be injected from WindowsApps." A dev-registered
                // package usually lives on a normal drive where injecting from the package
                // folder accidentally works, which is why this only breaks Store installs.
                // Copy into the package LocalState\TempState and load that copy.
                // Do NOT use %LOCALAPPDATA%\<name>: such writes get virtualized into
                // Packages\<family>\LocalCache, a path invisible to Explorer's filesystem view.
                string destDll;
                try
                {
                    string tempState = Path.Combine(
                        Windows.Storage.ApplicationData.Current.LocalFolder.Path, "TempState");
                    Directory.CreateDirectory(tempState);
                    destDll = Path.Combine(tempState, "ExplorerTAP.dll");
                    if (!File.Exists(destDll) || File.GetLastWriteTimeUtc(sourceDll) > File.GetLastWriteTimeUtc(destDll))
                    {
                        File.Copy(sourceDll, destDll, true);
                    }
                }
                catch (IOException ex)
                {
                    // In use = Explorer still has the previous copy mapped; resolves itself
                    // the next time Explorer restarts.
                    destDll = string.Empty;
                    _logger?.Warn($"Could not refresh ExplorerTAP.dll in TempState (in use?): {ex.Message}");
                }
                catch (Exception ex)
                {
                    destDll = string.Empty;
                    _logger?.Warn($"Could not copy ExplorerTAP.dll to TempState: {ex.Message}");
                }

                loadDll = File.Exists(destDll) ? destDll : sourceDll;
                _logger?.Info($"ExplorerTAP will be loaded from: {loadDll}");
            }
            else
            {
                // Portable/unpackaged: keep a versioned copy in %LOCALAPPDATA% so a
                // read-only app directory (e.g. Program Files) does not get locked.
                string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslucentTabBar", "TempState");
                Directory.CreateDirectory(tempDir);
                string destDll = Path.Combine(tempDir, "ExplorerTAP.dll");

                if (!File.Exists(destDll) || File.GetLastWriteTimeUtc(sourceDll) > File.GetLastWriteTimeUtc(destDll))
                {
                    try
                    {
                        File.Copy(sourceDll, destDll, true);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"Could not overwrite {destDll}: {ex.Message}");
                    }
                }

                loadDll = File.Exists(destDll) ? destDll : sourceDll;
            }

            var hMod = Kernel32.LoadLibrary(loadDll);
            if (hMod == nint.Zero)
            {
                _logger?.Error($"Failed to LoadLibrary({loadDll}), Win32Error={Marshal.GetLastWin32Error()}");
                return false;
            }

            var proc = Kernel32.GetProcAddress(hMod, "InjectExplorerTAP");
            if (proc == nint.Zero)
            {
                _logger?.Error("GetProcAddress('InjectExplorerTAP') failed.");
                return false;
            }

            var inject = Marshal.GetDelegateForFunctionPointer<InjectExplorerTAPDelegate>(proc);
            Guid iid = new Guid("5bcf9150-c28a-4ef2-913c-4c3ea2f5ead0");
            int hr = inject(primaryTaskbarHwnd, in iid, out nint ppv);
            _logger?.Info($"InjectExplorerTAP returned hr=0x{hr:X8}, ppv=0x{ppv:X8}");

            if (hr >= 0 && ppv != nint.Zero)
            {
                var service = (ITaskbarAppearanceService)Marshal.GetObjectForIUnknown(ppv);
                service.RestoreAllTaskbarsToDefaultWhenProcessDies((uint)System.Diagnostics.Process.GetCurrentProcess().Id);
                _appearanceService = service;
                _tapInitialized = true;
                _logger?.Info("ITaskbarAppearanceService successfully obtained and registered process exit restore!");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Error initializing ExplorerTAP: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Drops the appearance service so the next apply injects into the new Explorer again.
    /// </summary>
    public void ResetExplorerTap()
    {
        _appearanceService = null;
        _tapInitialized = false;
        _tapFailureStreak = 0;
        _nextTapAttemptUtc = DateTime.MinValue;
        _warnedLegacyFallback = false;
        _reapplyTimer?.Dispose();
        _reapplyTimer = null;
    }

    /// <summary>
    /// Resets the named TAP ready event if a previous failed attempt left it set. While
    /// Explorer keeps the DLL mapped nothing will set it again, so a fresh injection would
    /// otherwise sail through the wait and fail on the missing COM object every time.
    /// </summary>
    private void ResetStaleTapReadyEvent()
    {
        try
        {
            var hEvent = Kernel32.OpenEventW(0x0002 /* EVENT_MODIFY_STATE */, false, "TTBTAP_Ready");
            if (hEvent != nint.Zero)
            {
                Kernel32.ResetEvent(hEvent);
                Kernel32.CloseHandle(hEvent);
                _logger?.Info("Reset a stale TTBTAP_Ready event before retrying injection");
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug($"ResetStaleTapReadyEvent failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The taskbar registers with the TAP only after its XAML tree finished building, which
    /// can happen long after the injection succeeded (typically at logon). Re-apply the last
    /// appearance periodically so a late registration is painted as well.
    /// </summary>
    private void StartReapplyTimer()
    {
        if (_disposed || _reapplyTimer != null) return;

        _reapplyTimer = new Timer(_ =>
        {
            try
            {
                if (_disposed || !_tapInitialized) return;

                var appearance = _lastAppearance;
                if (appearance != null)
                {
                    ApplyAppearance(appearance);
                    _logger?.Debug("Periodic appearance re-apply performed");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Periodic appearance re-apply failed: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Information about a single taskbar (one per monitor).
    /// </summary>
    public class TaskbarInfo
    {
        public nint TaskbarWindow { get; set; }
        public nint MonitorHandle { get; set; }
    }

    public TaskbarService(LoggerService? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Invalidates the taskbar cache so it will be rediscovered on the next call.
    /// </summary>
    public void InvalidateCache()
    {
        _taskbars.Clear();
    }

    /// <summary>
    /// Enumerates all taskbars (one per monitor) and discovers their window handles.
    /// </summary>
    public IReadOnlyDictionary<nint, TaskbarInfo> DiscoverTaskbars()
    {
        _taskbars.Clear();

        // 1. Primary taskbar
        var shellTray = User32.FindWindowW("Shell_TrayWnd", null);
        if (shellTray != nint.Zero)
        {
            EnsureExplorerTap(shellTray);
            var monitor = User32.MonitorFromWindow(shellTray, MonitorConstants.MONITOR_DEFAULTTOPRIMARY);
            _taskbars[shellTray] = new TaskbarInfo
            {
                TaskbarWindow = shellTray,
                MonitorHandle = monitor,
            };
            _logger?.Debug($"Discovered primary taskbar: 0x{shellTray:X8} (Monitor: 0x{monitor:X8})");
        }
        else
        {
            _logger?.Warn("Shell_TrayWnd not found");
        }

        // 2. Secondary taskbars on multi-monitor setups
        nint secondaryTray = nint.Zero;
        while ((secondaryTray = User32.FindWindowExW(nint.Zero, secondaryTray, "Shell_SecondaryTrayWnd", null)) != nint.Zero)
        {
            var monitor = User32.MonitorFromWindow(secondaryTray, MonitorConstants.MONITOR_DEFAULTTONEAREST);
            _taskbars[secondaryTray] = new TaskbarInfo
            {
                TaskbarWindow = secondaryTray,
                MonitorHandle = monitor,
            };
            _logger?.Debug($"Discovered secondary taskbar: 0x{secondaryTray:X8} (Monitor: 0x{monitor:X8})");
        }

        return _taskbars;
    }

    /// <summary>
    /// Applies the given appearance configuration to all taskbars.
    /// </summary>
    public void ApplyAppearance(TaskbarAppearance appearance)
    {
        _lastAppearance = appearance;
        var taskbars = DiscoverTaskbars();
        foreach (var (hwnd, _) in taskbars)
        {
            ApplyToTaskbar(hwnd, appearance);
        }
    }

    /// <summary>
    /// Applies the given appearance configuration to a specific taskbar window.
    /// </summary>
    public void ApplyToTaskbar(nint taskbarHwnd, TaskbarAppearance appearance)
    {
        if (taskbarHwnd == nint.Zero)
            return;

        var accentState = MapAccentState(appearance.Accent);
        var gradientColor = ColorHelper.ToAccentColor(appearance.Color);

        // For acrylic, alpha must be >= 1, and flags = 0
        // For clear/gradient, flags = 2 (indicates gradient color is used)
        uint accentFlags = (accentState == ACCENT_STATE.ACCENT_ENABLE_ACRYLICBLURBEHIND) ? 0u : 2u;
        if (accentState == ACCENT_STATE.ACCENT_ENABLE_ACRYLICBLURBEHIND && appearance.Color.A == 0)
        {
            // Acrylic mode doesn't like a completely 0 opacity
            gradientColor = (gradientColor & 0x00FFFFFF) | 0x01000000;
        }

        // 1. Windows 11 XAML Appearance Service (ExplorerTAP)
        if (_appearanceService != null)
        {
            try
            {
                uint tapColor = ColorHelper.ToAccentColor(appearance.Color);

                if (appearance.Accent == TaskbarAccentMode.Normal)
                {
                    _appearanceService.ReturnTaskbarToDefaultAppearance(taskbarHwnd);
                    _logger?.Debug($"ReturnTaskbarToDefaultAppearance(0x{taskbarHwnd:X8})");
                }
                else if (appearance.Accent == TaskbarAccentMode.Blur)
                {
                    uint blurColor = appearance.Color.A == 0 ? 0x00000000 : tapColor;
                    _appearanceService.SetTaskbarBlur(taskbarHwnd, blurColor, (float)appearance.BlurRadius / 3f);
                    _logger?.Debug($"SetTaskbarBlur(0x{taskbarHwnd:X8}, Color=0x{blurColor:X8}, Radius={appearance.BlurRadius})");
                }
                else if (appearance.Accent == TaskbarAccentMode.Acrylic)
                {
                    uint acColor = appearance.Color.A == 0 ? 0x01000000 : tapColor;
                    _appearanceService.SetTaskbarAppearance(taskbarHwnd, TaskbarBrush.Acrylic, acColor);
                    _logger?.Debug($"SetTaskbarAppearance(0x{taskbarHwnd:X8}, Acrylic, Color=0x{acColor:X8})");
                }
                else if (appearance.Accent == TaskbarAccentMode.Opaque)
                {
                    uint opColor = appearance.Color.A == 0 ? 0xFF202020 : (tapColor | 0xFF000000);
                    _appearanceService.SetTaskbarAppearance(taskbarHwnd, TaskbarBrush.SolidColor, opColor);
                    _logger?.Debug($"SetTaskbarAppearance(0x{taskbarHwnd:X8}, SolidColor, Color=0x{opColor:X8})");
                }
                else // Clear
                {
                    uint clColor = appearance.Color.A == 0 ? 0x00000000 : tapColor;
                    _appearanceService.SetTaskbarAppearance(taskbarHwnd, TaskbarBrush.SolidColor, clColor);
                    _logger?.Debug($"SetTaskbarAppearance(0x{taskbarHwnd:X8}, SolidColor (Clear), Color=0x{clColor:X8})");
                }

                // Apply taskbar line / border visibility on Windows 11
                _appearanceService.SetTaskbarBorderVisibility(taskbarHwnd, appearance.ShowLine);
                _logger?.Debug($"SetTaskbarBorderVisibility(0x{taskbarHwnd:X8}, Visible={appearance.ShowLine})");

                // If ExplorerTAP successfully handled it, return without calling SetWindowCompositionAttribute
                return;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Failed calling ITaskbarAppearanceService: {ex.Message}");
            }
        }

        // 2. ACCENT_POLICY structure for DWM / legacy taskbars (Windows 10 or fallback)
        if (Environment.OSVersion.Version.Build >= 22000 && _appearanceService == null && !_warnedLegacyFallback)
        {
            _warnedLegacyFallback = true;
            _logger?.Warn("TaskbarAppearanceService is not attached - applied via the legacy composition API, which has no visible effect on this Windows 11 build. Injection will keep retrying in the background.");
        }

        var policy = new ACCENT_POLICY
        {
            AccentState = accentState,
            AccentFlags = accentFlags,
            GradientColor = gradientColor,
            AnimationId = 0,
        };

        var policyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
        try
        {
            Marshal.StructureToPtr(policy, policyPtr, false);

            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
                pvData = policyPtr,
                cbData = (uint)Marshal.SizeOf<ACCENT_POLICY>(),
            };

            bool success = User32.SetWindowCompositionAttribute(taskbarHwnd, ref data);
            _logger?.Debug($"SetWindowCompositionAttribute(0x{taskbarHwnd:X8}, State={accentState}, Flags={accentFlags}, Color=0x{gradientColor:X8}) => {success} (Win32Error={Marshal.GetLastWin32Error()})");

            // Only notify Explorer / DWM if reverting to normal (disabled), otherwise Explorer will reset our custom composition
            if (accentState == ACCENT_STATE.ACCENT_DISABLED)
            {
                User32.SendMessageW(taskbarHwnd, 0x031E /* WM_DWMCOMPOSITIONCHANGED */, (nint)1, nint.Zero);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"Exception in ApplyToTaskbar: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(policyPtr);
        }
    }

    /// <summary>
    /// Resets all taskbars to their default Windows appearance.
    /// </summary>
    public void ResetToDefault()
    {
        if (_appearanceService != null)
        {
            try
            {
                _appearanceService.RestoreAllTaskbarsToDefault();
            }
            catch { }
        }

        var taskbars = DiscoverTaskbars();
        foreach (var (hwnd, _) in taskbars)
        {
            var policy = new ACCENT_POLICY
            {
                AccentState = ACCENT_STATE.ACCENT_DISABLED,
                AccentFlags = 0,
                GradientColor = 0,
                AnimationId = 0,
            };

            var policyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
            try
            {
                Marshal.StructureToPtr(policy, policyPtr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attrib = WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
                    pvData = policyPtr,
                    cbData = (uint)Marshal.SizeOf<ACCENT_POLICY>(),
                };
                User32.SetWindowCompositionAttribute(hwnd, ref data);
                User32.SendMessageW(hwnd, 0x031E /* WM_DWMCOMPOSITIONCHANGED */, (nint)1, nint.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(policyPtr);
            }
        }
    }

    public void SetAeroPeekVisibility(nint taskbarHwnd, bool show)
    {
        // 1. Classic / Win10 taskbar window check (TrayShowDesktopButtonWClass)
        try
        {
            var notifyWnd = User32.FindWindowExW(taskbarHwnd, nint.Zero, "TrayNotifyWnd", null);
            if (notifyWnd != nint.Zero)
            {
                var peekWnd = User32.FindWindowExW(notifyWnd, nint.Zero, "TrayShowDesktopButtonWClass", null);
                if (peekWnd != nint.Zero)
                {
                    var style = (long)User32.GetWindowLongPtr(peekWnd, User32.GWL_EXSTYLE);
                    if (show)
                    {
                        User32.SetWindowLongPtr(peekWnd, User32.GWL_EXSTYLE, (nint)(style & ~(long)User32.WS_EX_LAYERED));
                    }
                    else
                    {
                        User32.SetWindowLongPtr(peekWnd, User32.GWL_EXSTYLE, (nint)(style | (long)User32.WS_EX_LAYERED));
                        User32.SetLayeredWindowAttributes(peekWnd, 0, 1, User32.LWA_ALPHA);
                    }
                    _logger?.Debug($"Classic peek button visibility set to {show}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed setting classic peek visibility: {ex.Message}");
        }

        // 2. Windows 11 registry setting (DisablePreviewDesktop and TaskbarSd)
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            if (key != null)
            {
                key.SetValue("DisablePreviewDesktop", show ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("TaskbarSd", show ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }

            // Notify Explorer and taskbars that settings changed
            User32.PostMessageW((nint)0xFFFF /* HWND_BROADCAST */, User32.WM_SETTINGCHANGE, nint.Zero, nint.Zero);
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed updating Aero Peek registry: {ex.Message}");
        }
    }

    public void SetTaskbarLineVisibility(nint taskbarHwnd, bool show)
    {
        // On Windows 11, the taskbar border/line is controlled via ExplorerTAP's SetTaskbarBorderVisibility.
        // This method provides a registry-based fallback and Windows 10 compatibility.
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            if (key != null)
            {
                key.SetValue("TaskbarDa", show ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            _logger?.Debug($"Taskbar line visibility set to {show} via registry");
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed setting taskbar line visibility: {ex.Message}");
        }
    }

    private static bool IsPackaged
    {
        get
        {
            try
            {
                return Windows.ApplicationModel.Package.Current != null;
            }
            catch
            {
                return false;
            }
        }
    }

    private static ACCENT_STATE MapAccentState(TaskbarAccentMode mode) => mode switch
    {
        TaskbarAccentMode.Normal => ACCENT_STATE.ACCENT_DISABLED,
        TaskbarAccentMode.Opaque => ACCENT_STATE.ACCENT_ENABLE_GRADIENT,
        TaskbarAccentMode.Clear => ACCENT_STATE.ACCENT_ENABLE_TRANSPARENTGRADIENT,
        TaskbarAccentMode.Blur => ACCENT_STATE.ACCENT_ENABLE_BLURBEHIND,
        TaskbarAccentMode.Acrylic => ACCENT_STATE.ACCENT_ENABLE_ACRYLICBLURBEHIND,
        _ => ACCENT_STATE.ACCENT_DISABLED,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reapplyTimer?.Dispose();
        _reapplyTimer = null;
        ResetToDefault();
    }
}
