using System.Runtime.InteropServices;
using System.Text;

namespace TranslucentTabBar.NativeMethods;

#region ACCENT_POLICY and related enums/structs

/// <summary>
/// Undocumented: Determines how a window's background is rendered.
/// Maps to the native ACCENT_STATE enum from user32.
/// </summary>
internal enum ACCENT_STATE
{
    ACCENT_DISABLED = 0,
    ACCENT_ENABLE_GRADIENT = 1,
    ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
    ACCENT_ENABLE_BLURBEHIND = 3,
    ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    ACCENT_ENABLE_HOSTBACKDROP = 5,
    ACCENT_INVALID_STATE = 6,
}

/// <summary>
/// Undocumented: Determines what attribute is being manipulated.
/// </summary>
internal enum WINDOWCOMPOSITIONATTRIB
{
    WCA_ACCENT_POLICY = 19,
}

/// <summary>
/// Undocumented: Determines how a window's background is rendered.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ACCENT_POLICY
{
    public ACCENT_STATE AccentState;
    public uint AccentFlags;
    public uint GradientColor;
    public int AnimationId;
}

/// <summary>
/// Undocumented: Options for SetWindowCompositionAttribute.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWCOMPOSITIONATTRIBDATA
{
    public WINDOWCOMPOSITIONATTRIB Attrib;
    public nint pvData;
    public uint cbData;
}

#endregion

#region Shell / Tray

/// <summary>
/// Callback function for Shell_NotifyIconGetRect.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATA
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;
    public uint uVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

[Flags]
internal enum NotifyIconFlags : uint
{
    NIF_MESSAGE = 0x00000001,
    NIF_ICON = 0x00000002,
    NIF_TIP = 0x00000004,
    NIF_STATE = 0x00000008,
    NIF_INFO = 0x00000010,
    NIF_GUID = 0x00000020,
    NIF_REALTIME = 0x00000040,
    NIF_SHOWTIP = 0x00000080,
}

internal enum NotifyIconMessage : uint
{
    NIM_ADD = 0x00000000,
    NIM_MODIFY = 0x00000001,
    NIM_DELETE = 0x00000002,
    NIM_SETFOCUS = 0x00000003,
    NIM_SETVERSION = 0x00000004,
}

#endregion

#region P/Invoke declarations

internal static partial class User32
{
    private const string DllName = "user32.dll";

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowCompositionAttribute(
        nint hwnd,
        ref WINDOWCOMPOSITIONATTRIBDATA data);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIconW(
        NotifyIconMessage dwMessage,
        ref NOTIFYICONDATA lpdata);

    [DllImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIconGetRect(
        ref NOTIFYICONDATA identifier,
        out RECT rect);

    [DllImport(DllName)]
    internal static extern nint GetDesktopWindow();

    [DllImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(
        EnumWindowsProc lpEnumFunc,
        nint lParam);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(
        nint hWnd,
        StringBuilder lpClassName,
        int nMaxCount);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(
        nint hWnd,
        StringBuilder lpString,
        int nMaxCount);

    [DllImport(DllName, SetLastError = true)]
    internal static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport(DllName, SetLastError = true)]
    internal static extern int GetWindowLongW(nint hWnd, int nIndex);

    [DllImport(DllName, SetLastError = true)]
    internal static extern int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);

    [DllImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport(DllName, SetLastError = true)]
    internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport(DllName, SetLastError = true)]
    internal static extern nint SendMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint hIcon);

    [DllImport(DllName, SetLastError = true)]
    internal static extern nint LoadIconW(nint hInstance, nint lpIconName);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint LoadImageW(
        nint hInst,
        string lpszName,
        uint uType,
        int cxDesired,
        int cyDesired,
        uint fuLoad);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport(DllName, SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport(DllName, SetLastError = true)]
    internal static extern nint DefWindowProcW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hWnd);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowExW(
        nint hWndParent,
        nint hWndChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport(DllName)]
    internal static extern uint GetDpiForWindow(nint hWnd);

    [DllImport(DllName, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport(DllName, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport(DllName)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    internal const uint WM_NULL = 0x0000;
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_ACTIVATE = 0x0006;
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const uint LWA_COLORKEY = 0x00000001;
    internal const uint LWA_ALPHA = 0x00000002;
}

/// <summary>
/// Callback for EnumWindows.
/// </summary>
internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

/// <summary>
/// Callback for WinEvent hooks.
/// </summary>
internal delegate void WinEventProc(
    nint hWinEventHook,
    uint eventType,
    nint hWnd,
    long idObject,
    long idChild,
    uint dwEventThread,
    uint dwmsEventTime);

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASS
{
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public string? lpszMenuName;
    public string? lpszClassName;
}

#endregion

#region Shell32

internal enum ABM : uint
{
    ABM_GETTASKBARPOS = 0x00000005,
}

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public int cbSize;
    public nint hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public int lParam;
}

internal static partial class Shell32
{
    private const string DllName = "shell32.dll";

    [DllImport(DllName, SetLastError = true)]
    internal static extern nint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}

#endregion

#region Constants

internal static class WinEventConstants
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
}

internal static class WindowStyles
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
}

internal static class ShowWindowCommands
{
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMINIMIZED = 2;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_SHOWMINNOACTIVE = 7;
    public const int SW_SHOWNA = 8;
    public const int SW_RESTORE = 9;
}

internal static class MonitorConstants
{
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
}

#endregion
