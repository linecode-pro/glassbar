using System.Runtime.InteropServices;
using TranslucentTabBar.NativeMethods;

namespace TranslucentTabBar.Services;

/// <summary>
/// Window procedure delegate type.
/// </summary>
internal delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

/// <summary>
/// Manages the system tray icon and its context menu using Win32 Shell_NotifyIcon.
/// Supports theme-aware icons and proper TaskbarCreated message registration.
/// </summary>
public class TrayIconService : IDisposable
{
    private const uint WM_TRAY_CALLBACK = 0xBEEF;

    private nint _hWnd;
    private nint _hIcon;
    private nint _hDarkIcon;
    private uint _trayId = 1001;
    private uint _taskbarCreatedMsg;
    private readonly uint _showSettingsMsg;
    private bool _disposed;
    private bool _isShowing;
    private readonly LoggerService _logger;

    // Window procedure delegate
    private WndProcDelegate _wndProc = null!;
    private GCHandle _wndProcHandle;

    /// <summary>
    /// Raised when the user clicks the tray icon.
    /// </summary>
    public event Action? TrayLeftClick;

    /// <summary>
    /// Raised when the user right-clicks the tray icon (requesting context menu).
    /// </summary>
    public event Action? TrayRightClick;

    /// <summary>
    /// Raised when Explorer restarts and the tray icon needs to be recreated.
    /// </summary>
    public event Action? ExplorerRestarted;

    /// <summary>
    /// Raised when a menu command is received from the context menu.
    /// </summary>
    public event Action<int>? MenuCommand;

    public TrayIconService(LoggerService logger)
    {
        _logger = logger;
        _taskbarCreatedMsg = User32.RegisterWindowMessageW("TaskbarCreated");
        _showSettingsMsg = User32.RegisterWindowMessageW("TranslucentTabBar_ShowSettings");
        _logger.Debug($"TaskbarCreated message registered: 0x{_taskbarCreatedMsg:X8}");

        CreateMessageWindow();
        LoadIcons();
        Show();
    }

    /// <summary>
    /// Gets the handle of the hidden message window used for tray communication.
    /// </summary>
    public nint WindowHandle => _hWnd;

    private void CreateMessageWindow()
    {
        var hInstance = Marshal.GetHINSTANCE(typeof(TrayIconService).Module);

        _wndProc = WndProc;
        _wndProcHandle = GCHandle.Alloc(_wndProc);

        const string className = "TranslucentTabBar_TrayWindow";

        var wndClass = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className,
            hbrBackground = nint.Zero,
        };

        var atom = User32.RegisterClassW(ref wndClass);
        if (atom == 0)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1410) // 1410 = ERROR_CLASS_ALREADY_EXISTS
            {
                _logger.Warn($"RegisterClassW returned 0, error: {err}");
            }
        }

        _hWnd = User32.CreateWindowExW(
            0,
            className,
            "Glassbar",
            0,
            0, 0, 0, 0,
            nint.Zero,
            nint.Zero,
            hInstance,
            nint.Zero);

        if (_hWnd == nint.Zero)
        {
            _logger.Error($"CreateWindowExW failed, error: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            _logger.Debug($"Tray message window created: 0x{_hWnd:X8}");
        }
    }

    private void LoadIcons()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _hIcon = User32.LoadImageW(nint.Zero, iconPath, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
            }

            if (_hIcon == nint.Zero)
            {
                var hInstance = Marshal.GetHINSTANCE(typeof(TrayIconService).Module);
                _hIcon = User32.LoadImageW(hInstance, "#101", 1, 16, 16, 0);
            }

            if (_hIcon == nint.Zero)
            {
                _hIcon = User32.LoadIconW(nint.Zero, 32512 /* IDI_APPLICATION */);
            }

            _hDarkIcon = _hIcon;
            _logger.Debug($"Tray icon loaded: 0x{_hIcon:X8}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load tray icon: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the tray icon based on the current Windows theme.
    /// </summary>
    public void UpdateThemeIcon()
    {
        try
        {
            var isDarkMode = UxTheme.ShouldSystemUseDarkMode() != 0;
            var data = CreateNotifyIconData();
            data.hIcon = isDarkMode ? _hDarkIcon : _hIcon;
            data.uFlags |= (uint)NotifyIconFlags.NIF_ICON;
            User32.Shell_NotifyIconW(NotifyIconMessage.NIM_MODIFY, ref data);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to update theme icon: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows the tray icon.
    /// </summary>
    public void Show()
    {
        if (_isShowing || _hWnd == nint.Zero) return;
        _isShowing = true;

        var data = CreateNotifyIconData();
        if (!User32.Shell_NotifyIconW(NotifyIconMessage.NIM_ADD, ref data))
        {
            _logger.Warn($"Shell_NotifyIcon(NIM_ADD) failed, error: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            _logger.Info("Tray icon added successfully");
        }
    }

    /// <summary>
    /// Hides the tray icon.
    /// </summary>
    public void Hide()
    {
        if (!_isShowing) return;
        _isShowing = false;

        var data = CreateNotifyIconData();
        User32.Shell_NotifyIconW(NotifyIconMessage.NIM_DELETE, ref data);
    }

    /// <summary>
    /// Updates the tray icon.
    /// </summary>
    public void UpdateIcon()
    {
        if (!_isShowing) return;

        var data = CreateNotifyIconData();
        User32.Shell_NotifyIconW(NotifyIconMessage.NIM_MODIFY, ref data);
    }

    /// <summary>
    /// Shows a balloon notification.
    /// </summary>
    public void ShowNotification(string title, string text, uint infoFlags = 0)
    {
        if (!_isShowing) return;

        var data = CreateNotifyIconData();
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = infoFlags;
        data.uFlags |= (uint)NotifyIconFlags.NIF_INFO;

        User32.Shell_NotifyIconW(NotifyIconMessage.NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATA CreateNotifyIconData()
    {
        var flags = NotifyIconFlags.NIF_MESSAGE | NotifyIconFlags.NIF_TIP;
        if (_hIcon != nint.Zero)
        {
            flags |= NotifyIconFlags.NIF_ICON;
        }

        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = _trayId,
            uFlags = (uint)flags,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = _hIcon,
            szTip = "Glassbar",
        };
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            var lParamLow = (uint)(lParam.ToInt64() & 0xFFFF);

            switch (lParamLow)
            {
                case 0x0205: // WM_RBUTTONUP
                case 0x007B: // WM_CONTEXTMENU
                    TrayRightClick?.Invoke();
                    return nint.Zero;

                case 0x0202: // WM_LBUTTONUP
                case 0x0203: // WM_LBUTTONDBLCLK
                    TrayLeftClick?.Invoke();
                    return nint.Zero;
            }
        }
        else if (msg == User32.WM_COMMAND)
        {
            var commandId = (int)(wParam.ToInt64() & 0xFFFF);
            MenuCommand?.Invoke(commandId);
            return nint.Zero;
        }
        else if (msg == _showSettingsMsg)
        {
            TrayLeftClick?.Invoke();
            return nint.Zero;
        }
        else if (msg == _taskbarCreatedMsg)
        {
            _logger.Info("TaskbarCreated message received - Explorer restarted");
            _isShowing = false;
            Show();
            ExplorerRestarted?.Invoke();
            return nint.Zero;
        }

        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Hide();

        if (_hIcon != nint.Zero)
            User32.DestroyIcon(_hIcon);

        if (_hWnd != nint.Zero)
            User32.DestroyWindow(_hWnd);

        if (_wndProcHandle.IsAllocated)
            _wndProcHandle.Free();
    }
}
