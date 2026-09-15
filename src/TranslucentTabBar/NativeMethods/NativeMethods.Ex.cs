using System.Runtime.InteropServices;

namespace TranslucentTabBar.NativeMethods;

#region IAppVisibility COM interface

/// <summary>
/// COM interface for detecting Start Menu and other launcher visibility.
/// </summary>
[ComImport]
[Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppVisibility
{
    void GetAppVisibilityOnMonitor(nint hMonitor, out MONITOR_APP_VISIBILITY mode);
    void IsLauncherVisible([MarshalAs(UnmanagedType.Bool)] out bool visible);
    void Advise(IAppVisibilityEvents callback, out int cookie);
    void Unadvise(int cookie);
}

/// <summary>
/// Callback interface for IAppVisibility events.
/// </summary>
[ComImport]
[Guid("6584CE6B-7D82-49C2-89C9-C6BC02BA8C38")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppVisibilityEvents
{
    void AppVisibilityOnMonitorChanged(nint hMonitor, MONITOR_APP_VISIBILITY previousMode, MONITOR_APP_VISIBILITY currentMode);
    void LauncherVisibilityChange([MarshalAs(UnmanagedType.Bool)] bool visible);
}

internal enum MONITOR_APP_VISIBILITY
{
    MAV_UNKNOWN = 0,
    MAV_NO_APP_VISIBLE = 1,
    MAV_APP_VISIBLE = 2,
}

/// <summary>
/// CLSID for the AppVisibility COM object.
/// </summary>
internal static class AppVisibilityClsid
{
    public static readonly Guid CLSID_AppVisibility = new("7E5FE3D9-985F-4908-91F9-EE19F9FD1514");
}

#endregion

#region TaskbarCreated message

internal static partial class User32
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessageW(string lpString);
}

#endregion

#region Dark mode detection

internal static partial class UxTheme
{
    private const string DllName = "uxtheme.dll";

    [DllImport(DllName)]
    internal static extern int ShouldSystemUseDarkMode();
}

#endregion

#region Startup / Registry

internal static partial class Kernel32
{
    private const string DllName = "kernel32.dll";

    [DllImport(DllName, CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandleW(string? lpModuleName);
}

internal static partial class AdvApi32
{
    private const string DllName = "advapi32.dll";

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegOpenKeyExW(
        nint hKey,
        string lpSubKey,
        uint ulOptions,
        uint samDesired,
        out nint phkResult);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegSetValueExW(
        nint hKey,
        string lpValueName,
        uint Reserved,
        uint dwType,
        byte[] lpData,
        int cbData);

    [DllImport(DllName, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegDeleteValueW(
        nint hKey,
        string lpValueName);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegCloseKey(nint hKey);

    internal static readonly nint HKEY_CURRENT_USER = unchecked((nint)0x80000001);
    internal const uint KEY_SET_VALUE = 0x0002;
    internal const uint KEY_QUERY_VALUE = 0x0001;
    internal const uint REG_SZ = 1;
}

#endregion

#region Menu / Context menu

internal static partial class User32
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InsertMenuItemW(
        nint hMenu,
        uint uItem,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition,
        ref MENUITEMINFO lpmii);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMenuItemInfoW(
        nint hMenu,
        uint uItem,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition,
        ref MENUITEMINFO lpmii);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TrackPopupMenu(
        nint hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        nint hWnd,
        nint prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetMenuDefaultItem(nint hMenu, [MarshalAs(UnmanagedType.Bool)] bool fByPos, uint gmdiFlags);
}

[Flags]
internal enum MenuFlags : uint
{
    MF_STRING = 0x00000000,
    MF_BYPOSITION = 0x00000400,
    MF_SEPARATOR = 0x00000800,
    MF_ENABLED = 0x00000000,
    MF_GRAYED = 0x00000001,
    MF_DISABLED = 0x00000002,
    MF_UNCHECKED = 0x00000000,
    MF_CHECKED = 0x00000008,
    MF_POPUP = 0x00000010,
    MF_HILITE = 0x00000080,
    MF_OWNERDRAW = 0x00000100,
}

[Flags]
internal enum TrackPopupMenuFlags : uint
{
    TPM_LEFTALIGN = 0x0000,
    TPM_RIGHTALIGN = 0x0008,
    TPM_TOPALIGN = 0x0000,
    TPM_VCENTERALIGN = 0x0010,
    TPM_BOTTOMALIGN = 0x0020,
    TPM_LEFTBUTTON = 0x0000,
    TPM_RIGHTBUTTON = 0x0002,
    TPM_NONOTIFY = 0x0080,
    TPM_RETURNCMD = 0x0100,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MENUITEMINFO
{
    public uint cbSize;
    public uint fMask;
    public uint fType;
    public uint fState;
    public uint wID;
    public nint hSubMenu;
    public nint hbmpChecked;
    public nint hbmpUnchecked;
    public nint dwItemData;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? dwTypeData;
    public uint cch;
    public nint hbmpItem;
}

internal static class MenuItemMask
{
    public const uint MIIM_STATE = 0x00000001;
    public const uint MIIM_ID = 0x00000002;
    public const uint MIIM_SUBMENU = 0x00000004;
    public const uint MIIM_TYPE = 0x00000010;
    public const uint MIIM_STRING = 0x00000040;
    public const uint MIIM_BITMAP = 0x00000080;
    public const uint MIIM_FTYPE = 0x00000100;
}

/// <summary>
/// Menu command IDs for the tray context menu.
/// </summary>
internal static class TrayMenuCommands
{
    // Base IDs for each state's accent mode radio group
    public const int ID_DESKTOP_ACCENT_BASE = 1000;
    public const int ID_VISIBLE_ACCENT_BASE = 1010;
    public const int ID_MAXIMIZED_ACCENT_BASE = 1020;
    public const int ID_START_ACCENT_BASE = 1030;
    public const int ID_SEARCH_ACCENT_BASE = 1040;
    public const int ID_TASKVIEW_ACCENT_BASE = 1050;
    public const int ID_BATTERY_ACCENT_BASE = 1060;

    // Accent modes within each group (0-4)
    public const int ACCENT_OFFSET_NORMAL = 0;
    public const int ACCENT_OFFSET_OPAQUE = 1;
    public const int ACCENT_OFFSET_CLEAR = 2;
    public const int ACCENT_OFFSET_BLUR = 3;
    public const int ACCENT_OFFSET_ACRYLIC = 4;

    // Per-state toggles (enabled, show peek, show line)
    public const int ID_DESKTOP_SHOW_PEEK = 1100;
    public const int ID_DESKTOP_SHOW_LINE = 1101;
    public const int ID_DESKTOP_COLOR = 1102;

    public const int ID_VISIBLE_ENABLED = 1110;
    public const int ID_VISIBLE_SHOW_PEEK = 1111;
    public const int ID_VISIBLE_SHOW_LINE = 1112;
    public const int ID_VISIBLE_COLOR = 1113;

    public const int ID_MAXIMIZED_ENABLED = 1120;
    public const int ID_MAXIMIZED_SHOW_PEEK = 1121;
    public const int ID_MAXIMIZED_SHOW_LINE = 1122;
    public const int ID_MAXIMIZED_COLOR = 1123;

    public const int ID_START_ENABLED = 1130;
    public const int ID_START_SHOW_PEEK = 1131;
    public const int ID_START_SHOW_LINE = 1132;
    public const int ID_START_COLOR = 1133;

    public const int ID_SEARCH_ENABLED = 1140;
    public const int ID_SEARCH_SHOW_PEEK = 1141;
    public const int ID_SEARCH_SHOW_LINE = 1142;
    public const int ID_SEARCH_COLOR = 1143;

    public const int ID_TASKVIEW_ENABLED = 1150;
    public const int ID_TASKVIEW_SHOW_PEEK = 1151;
    public const int ID_TASKVIEW_SHOW_LINE = 1152;
    public const int ID_TASKVIEW_COLOR = 1153;

    public const int ID_BATTERY_ENABLED = 1160;
    public const int ID_BATTERY_SHOW_PEEK = 1161;
    public const int ID_BATTERY_SHOW_LINE = 1162;
    public const int ID_BATTERY_COLOR = 1163;

    // Global commands
    public const int ID_OPEN_AT_BOOT = 1200;
    public const int ID_OPEN_SETTINGS = 1201;
    public const int ID_OPEN_CONFIG = 1202;
    public const int ID_SEPARATOR_1 = 1203;
    public const int ID_WELCOME = 1204;
    public const int ID_EXIT = 1205;
}

#endregion
