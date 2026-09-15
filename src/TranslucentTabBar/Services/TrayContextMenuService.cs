using System.Runtime.InteropServices;
using TranslucentTabBar.Models;
using TranslucentTabBar.NativeMethods;
using Windows.UI;

namespace TranslucentTabBar.Services;

/// <summary>
/// Creates and manages the Win32 popup context menu for the tray icon,
/// mirroring the full functionality of the original TranslucentTB context menu.
/// </summary>
public class TrayContextMenuService : IDisposable
{
    private readonly nint _hWnd;
    private readonly ConfigService _configService;
    private readonly StateResolverService _stateResolver;
    private readonly TaskbarService _taskbarService;
    private readonly StartupManager _startupManager;
    private readonly LocalizationService _localizationService;
    private readonly LoggerService _logger;
    private nint _hMenu;
    private bool _disposed;
    private int _nextSubMenuId = 2000;

    // Sub-menu handles
    private readonly Dictionary<string, nint> _subMenus = new();

    /// <summary>
    /// Raised when the user requests to open a color picker for a specific state.
    /// </summary>
    public event Action<Models.DynamicState, Color>? ColorRequested;

    /// <summary>
    /// Raised when the user requests to open the settings window.
    /// </summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// Raised when the user requests to open the welcome/about page.
    /// </summary>
    public event Action? AboutRequested;

    /// <summary>
    /// Raised when the user requests to exit the app.
    /// </summary>
    public event Action? ExitRequested;

    public TrayContextMenuService(
        nint hWnd,
        ConfigService configService,
        StateResolverService stateResolver,
        TaskbarService taskbarService,
        StartupManager startupManager,
        LocalizationService localizationService,
        LoggerService logger)
    {
        _hWnd = hWnd;
        _configService = configService;
        _stateResolver = stateResolver;
        _taskbarService = taskbarService;
        _startupManager = startupManager;
        _localizationService = localizationService;
        _logger = logger;
    }

    /// <summary>
    /// Shows the context menu at the given screen coordinates.
    /// </summary>
    public void Show(int x, int y)
    {
        DestroyMenu();
        BuildMenu();
        RefreshMenuStates();

        User32.SetForegroundWindow(_hWnd);
        User32.TrackPopupMenu(
            _hMenu,
            (uint)(TrackPopupMenuFlags.TPM_LEFTALIGN | TrackPopupMenuFlags.TPM_BOTTOMALIGN | TrackPopupMenuFlags.TPM_RIGHTBUTTON),
            x, y,
            0,
            _hWnd,
            nint.Zero);
        User32.PostMessageW(_hWnd, User32.WM_NULL, nint.Zero, nint.Zero);
    }

    /// <summary>
    /// Handles a menu command from the tray window's WM_COMMAND.
    /// </summary>
    public bool HandleCommand(int commandId)
    {
        var config = _configService.Config;

        // ── Desktop accent modes ──────────────────────────────────
        if (commandId >= TrayMenuCommands.ID_DESKTOP_ACCENT_BASE &&
            commandId < TrayMenuCommands.ID_DESKTOP_ACCENT_BASE + 5)
        {
            var mode = (TaskbarAccentMode)(commandId - TrayMenuCommands.ID_DESKTOP_ACCENT_BASE);
            config.DesktopAppearance.Accent = mode;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_DESKTOP_SHOW_PEEK)
        {
            config.DesktopAppearance.ShowPeek = !config.DesktopAppearance.ShowPeek;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_DESKTOP_SHOW_LINE)
        {
            config.DesktopAppearance.ShowLine = !config.DesktopAppearance.ShowLine;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_DESKTOP_COLOR)
        {
            ColorRequested?.Invoke(Models.DynamicState.Desktop, config.DesktopAppearance.Color);
            return true;
        }

        // ── Visible Window ────────────────────────────────────────
        if (commandId >= TrayMenuCommands.ID_VISIBLE_ACCENT_BASE &&
            commandId < TrayMenuCommands.ID_VISIBLE_ACCENT_BASE + 5)
        {
            var mode = (TaskbarAccentMode)(commandId - TrayMenuCommands.ID_VISIBLE_ACCENT_BASE);
            config.VisibleWindowAppearance.Accent = mode;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_VISIBLE_ENABLED)
        {
            config.VisibleWindowAppearance.Enabled = !config.VisibleWindowAppearance.Enabled;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_VISIBLE_COLOR)
        {
            ColorRequested?.Invoke(Models.DynamicState.VisibleWindow, config.VisibleWindowAppearance.Color);
            return true;
        }

        // ── Maximized Window ──────────────────────────────────────
        if (commandId >= TrayMenuCommands.ID_MAXIMIZED_ACCENT_BASE &&
            commandId < TrayMenuCommands.ID_MAXIMIZED_ACCENT_BASE + 5)
        {
            var mode = (TaskbarAccentMode)(commandId - TrayMenuCommands.ID_MAXIMIZED_ACCENT_BASE);
            config.MaximizedWindowAppearance.Accent = mode;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_MAXIMIZED_ENABLED)
        {
            config.MaximizedWindowAppearance.Enabled = !config.MaximizedWindowAppearance.Enabled;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_MAXIMIZED_COLOR)
        {
            ColorRequested?.Invoke(Models.DynamicState.MaximizedWindow, config.MaximizedWindowAppearance.Color);
            return true;
        }

        // ── Start Opened ──────────────────────────────────────────
        if (commandId >= TrayMenuCommands.ID_START_ACCENT_BASE &&
            commandId < TrayMenuCommands.ID_START_ACCENT_BASE + 5)
        {
            var mode = (TaskbarAccentMode)(commandId - TrayMenuCommands.ID_START_ACCENT_BASE);
            config.StartOpenedAppearance.Accent = mode;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_START_ENABLED)
        {
            config.StartOpenedAppearance.Enabled = !config.StartOpenedAppearance.Enabled;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_START_COLOR)
        {
            ColorRequested?.Invoke(Models.DynamicState.StartOpened, config.StartOpenedAppearance.Color);
            return true;
        }

        // ── Search Opened ─────────────────────────────────────────
        if (commandId >= TrayMenuCommands.ID_SEARCH_ACCENT_BASE &&
            commandId < TrayMenuCommands.ID_SEARCH_ACCENT_BASE + 5)
        {
            var mode = (TaskbarAccentMode)(commandId - TrayMenuCommands.ID_SEARCH_ACCENT_BASE);
            config.SearchOpenedAppearance.Accent = mode;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_SEARCH_ENABLED)
        {
            config.SearchOpenedAppearance.Enabled = !config.SearchOpenedAppearance.Enabled;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_SEARCH_COLOR)
        {
            ColorRequested?.Invoke(Models.DynamicState.SearchOpened, config.SearchOpenedAppearance.Color);
            return true;
        }

        // ── Task View Opened ──────────────────────────────────────
        if (commandId >= TrayMenuCommands.ID_TASKVIEW_ACCENT_BASE &&
            commandId < TrayMenuCommands.ID_TASKVIEW_ACCENT_BASE + 5)
        {
            var mode = (TaskbarAccentMode)(commandId - TrayMenuCommands.ID_TASKVIEW_ACCENT_BASE);
            config.TaskViewOpenedAppearance.Accent = mode;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_TASKVIEW_ENABLED)
        {
            config.TaskViewOpenedAppearance.Enabled = !config.TaskViewOpenedAppearance.Enabled;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_TASKVIEW_COLOR)
        {
            ColorRequested?.Invoke(Models.DynamicState.TaskViewOpened, config.TaskViewOpenedAppearance.Color);
            return true;
        }

        // ── Battery Saver ─────────────────────────────────────────
        if (commandId >= TrayMenuCommands.ID_BATTERY_ACCENT_BASE &&
            commandId < TrayMenuCommands.ID_BATTERY_ACCENT_BASE + 5)
        {
            var mode = (TaskbarAccentMode)(commandId - TrayMenuCommands.ID_BATTERY_ACCENT_BASE);
            config.BatterySaverAppearance.Accent = mode;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_BATTERY_ENABLED)
        {
            config.BatterySaverAppearance.Enabled = !config.BatterySaverAppearance.Enabled;
            ApplyAndSave();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_BATTERY_COLOR)
        {
            ColorRequested?.Invoke(Models.DynamicState.BatterySaver, config.BatterySaverAppearance.Color);
            return true;
        }

        // ── Global commands ───────────────────────────────────────
        if (commandId == TrayMenuCommands.ID_OPEN_AT_BOOT)
        {
            var isRegistered = _startupManager.IsRegistered;
            _ = _startupManager.SetRegisteredAsync(!isRegistered);
            _logger.Info($"Startup toggled: {!isRegistered}");
            return true;
        }

        if (commandId == TrayMenuCommands.ID_OPEN_SETTINGS)
        {
            SettingsRequested?.Invoke();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_WELCOME)
        {
            AboutRequested?.Invoke();
            return true;
        }

        if (commandId == TrayMenuCommands.ID_EXIT)
        {
            ExitRequested?.Invoke();
            return true;
        }

        return false;
    }

    private string Loc(string key, string fallback)
    {
        var s = _localizationService.GetString(key);
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    private void BuildMenu()
    {
        _hMenu = User32.CreatePopupMenu();
        _subMenus.Clear();
        _nextSubMenuId = 2000;

        var desktopText = Loc("Tray_Desktop", "Desktop");
        var visibleText = Loc("Tray_VisibleWindow", "Visible Window");
        var maximizedText = Loc("Tray_MaximizedWindow", "Maximized Window");
        var startText = Loc("Tray_StartOpened", "Start Opened");
        var searchText = Loc("Tray_SearchOpened", "Search Opened");
        var taskViewText = Loc("Tray_TaskViewOpened", "Task View Opened");
        var batteryText = Loc("Tray_BatterySaver", "Battery Saver");
        var showPeekText = Loc("Tray_ShowPeek", "Show Peek");
        var showLineText = Loc("Tray_ShowLine", "Show Line");
        var enabledText = Loc("Tray_Enabled", "Enabled");
        var colorText = Loc("Tray_Color", "Color...");

        // ── Desktop ──────────────────────────────────────────────
        var hDesktop = CreateSubMenu(desktopText, TrayMenuCommands.ID_DESKTOP_ACCENT_BASE);
        AddToggleItem(hDesktop, showPeekText, TrayMenuCommands.ID_DESKTOP_SHOW_PEEK);
        AddToggleItem(hDesktop, showLineText, TrayMenuCommands.ID_DESKTOP_SHOW_LINE);
        AddSeparator(hDesktop);
        AddAccentRadioItems(hDesktop, TrayMenuCommands.ID_DESKTOP_ACCENT_BASE);
        AddSeparator(hDesktop);
        AddMenuItem(hDesktop, colorText, TrayMenuCommands.ID_DESKTOP_COLOR);
        AddSubMenu(_hMenu, "▶ " + desktopText, hDesktop);

        // ── Visible Window ───────────────────────────────────────
        var hVisible = CreateSubMenu(visibleText, TrayMenuCommands.ID_VISIBLE_ACCENT_BASE);
        AddToggleItem(hVisible, enabledText, TrayMenuCommands.ID_VISIBLE_ENABLED);
        AddSeparator(hVisible);
        AddAccentRadioItems(hVisible, TrayMenuCommands.ID_VISIBLE_ACCENT_BASE);
        AddSeparator(hVisible);
        AddMenuItem(hVisible, colorText, TrayMenuCommands.ID_VISIBLE_COLOR);
        AddSubMenu(_hMenu, "▶ " + visibleText, hVisible);

        // ── Maximized Window ─────────────────────────────────────
        var hMaximized = CreateSubMenu(maximizedText, TrayMenuCommands.ID_MAXIMIZED_ACCENT_BASE);
        AddToggleItem(hMaximized, enabledText, TrayMenuCommands.ID_MAXIMIZED_ENABLED);
        AddSeparator(hMaximized);
        AddAccentRadioItems(hMaximized, TrayMenuCommands.ID_MAXIMIZED_ACCENT_BASE);
        AddSeparator(hMaximized);
        AddMenuItem(hMaximized, colorText, TrayMenuCommands.ID_MAXIMIZED_COLOR);
        AddSubMenu(_hMenu, "▶ " + maximizedText, hMaximized);

        // ── Start Opened ─────────────────────────────────────────
        var hStart = CreateSubMenu(startText, TrayMenuCommands.ID_START_ACCENT_BASE);
        AddToggleItem(hStart, enabledText, TrayMenuCommands.ID_START_ENABLED);
        AddSeparator(hStart);
        AddAccentRadioItems(hStart, TrayMenuCommands.ID_START_ACCENT_BASE);
        AddSeparator(hStart);
        AddMenuItem(hStart, colorText, TrayMenuCommands.ID_START_COLOR);
        AddSubMenu(_hMenu, "▶ " + startText, hStart);

        // ── Search Opened ────────────────────────────────────────
        var hSearch = CreateSubMenu(searchText, TrayMenuCommands.ID_SEARCH_ACCENT_BASE);
        AddToggleItem(hSearch, enabledText, TrayMenuCommands.ID_SEARCH_ENABLED);
        AddSeparator(hSearch);
        AddAccentRadioItems(hSearch, TrayMenuCommands.ID_SEARCH_ACCENT_BASE);
        AddSeparator(hSearch);
        AddMenuItem(hSearch, colorText, TrayMenuCommands.ID_SEARCH_COLOR);
        AddSubMenu(_hMenu, "▶ " + searchText, hSearch);

        // ── Task View Opened ─────────────────────────────────────
        var hTaskView = CreateSubMenu(taskViewText, TrayMenuCommands.ID_TASKVIEW_ACCENT_BASE);
        AddToggleItem(hTaskView, enabledText, TrayMenuCommands.ID_TASKVIEW_ENABLED);
        AddSeparator(hTaskView);
        AddAccentRadioItems(hTaskView, TrayMenuCommands.ID_TASKVIEW_ACCENT_BASE);
        AddSeparator(hTaskView);
        AddMenuItem(hTaskView, colorText, TrayMenuCommands.ID_TASKVIEW_COLOR);
        AddSubMenu(_hMenu, "▶ " + taskViewText, hTaskView);

        // ── Battery Saver ────────────────────────────────────────
        var hBattery = CreateSubMenu(batteryText, TrayMenuCommands.ID_BATTERY_ACCENT_BASE);
        AddToggleItem(hBattery, enabledText, TrayMenuCommands.ID_BATTERY_ENABLED);
        AddSeparator(hBattery);
        AddAccentRadioItems(hBattery, TrayMenuCommands.ID_BATTERY_ACCENT_BASE);
        AddSeparator(hBattery);
        AddMenuItem(hBattery, colorText, TrayMenuCommands.ID_BATTERY_COLOR);
        AddSubMenu(_hMenu, "▶ " + batteryText, hBattery);

        // ── Separator ────────────────────────────────────────────
        AddSeparator(_hMenu);

        // ── Global items ─────────────────────────────────────────
        AddMenuItem(_hMenu, Loc("Tray_Settings", "Settings"), TrayMenuCommands.ID_OPEN_SETTINGS);
        AddToggleItem(_hMenu, Loc("Tray_OpenAtBoot", "Open at boot"), TrayMenuCommands.ID_OPEN_AT_BOOT);

        AddSeparator(_hMenu);

        AddMenuItem(_hMenu, Loc("Tray_About", "About"), TrayMenuCommands.ID_WELCOME);
        AddMenuItem(_hMenu, Loc("Tray_Exit", "Exit"), TrayMenuCommands.ID_EXIT);
    }

    private void RefreshMenuStates()
    {
        var config = _configService.Config;

        // Refresh checkmarks for each state's submenu
        RefreshAccentRadioState(TrayMenuCommands.ID_DESKTOP_ACCENT_BASE, (int)config.DesktopAppearance.Accent);
        RefreshToggleState(TrayMenuCommands.ID_DESKTOP_SHOW_PEEK, config.DesktopAppearance.ShowPeek);
        RefreshToggleState(TrayMenuCommands.ID_DESKTOP_SHOW_LINE, config.DesktopAppearance.ShowLine);

        RefreshToggleState(TrayMenuCommands.ID_VISIBLE_ENABLED, config.VisibleWindowAppearance.Enabled);
        RefreshAccentRadioState(TrayMenuCommands.ID_VISIBLE_ACCENT_BASE, (int)config.VisibleWindowAppearance.Accent);

        RefreshToggleState(TrayMenuCommands.ID_MAXIMIZED_ENABLED, config.MaximizedWindowAppearance.Enabled);
        RefreshAccentRadioState(TrayMenuCommands.ID_MAXIMIZED_ACCENT_BASE, (int)config.MaximizedWindowAppearance.Accent);

        RefreshToggleState(TrayMenuCommands.ID_START_ENABLED, config.StartOpenedAppearance.Enabled);
        RefreshAccentRadioState(TrayMenuCommands.ID_START_ACCENT_BASE, (int)config.StartOpenedAppearance.Accent);

        RefreshToggleState(TrayMenuCommands.ID_SEARCH_ENABLED, config.SearchOpenedAppearance.Enabled);
        RefreshAccentRadioState(TrayMenuCommands.ID_SEARCH_ACCENT_BASE, (int)config.SearchOpenedAppearance.Accent);

        RefreshToggleState(TrayMenuCommands.ID_TASKVIEW_ENABLED, config.TaskViewOpenedAppearance.Enabled);
        RefreshAccentRadioState(TrayMenuCommands.ID_TASKVIEW_ACCENT_BASE, (int)config.TaskViewOpenedAppearance.Accent);

        RefreshToggleState(TrayMenuCommands.ID_BATTERY_ENABLED, config.BatterySaverAppearance.Enabled);
        RefreshAccentRadioState(TrayMenuCommands.ID_BATTERY_ACCENT_BASE, (int)config.BatterySaverAppearance.Accent);

        RefreshToggleState(TrayMenuCommands.ID_OPEN_AT_BOOT, _startupManager.IsRegistered);
    }

    private nint CreateSubMenu(string name, int baseId)
    {
        var hMenu = User32.CreatePopupMenu();
        _subMenus[name] = hMenu;
        return hMenu;
    }

    private void AddMenuItem(nint hMenu, string text, int commandId)
    {
        var mii = new MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MenuItemMask.MIIM_ID | MenuItemMask.MIIM_STRING | MenuItemMask.MIIM_FTYPE,
            fType = (uint)MenuFlags.MF_STRING,
            wID = (uint)commandId,
            dwTypeData = text,
            cch = (uint)text.Length,
        };
        User32.InsertMenuItemW(hMenu, uint.MaxValue, true, ref mii);
    }

    private void AddToggleItem(nint hMenu, string text, int commandId)
    {
        var mii = new MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MenuItemMask.MIIM_ID | MenuItemMask.MIIM_STRING | MenuItemMask.MIIM_FTYPE | MenuItemMask.MIIM_STATE,
            fType = (uint)MenuFlags.MF_STRING,
            fState = (uint)MenuFlags.MF_UNCHECKED,
            wID = (uint)commandId,
            dwTypeData = text,
            cch = (uint)text.Length,
        };
        User32.InsertMenuItemW(hMenu, uint.MaxValue, true, ref mii);
    }

    private void AddAccentRadioItems(nint hMenu, int baseId)
    {
        string[] keys = ["Accent_Normal", "Accent_Opaque", "Accent_Clear", "Accent_Blur", "Accent_Acrylic"];
        string[] fallbacks = ["Normal", "Opaque", "Clear", "Blur", "Acrylic"];
        for (int i = 0; i < keys.Length; i++)
        {
            var text = Loc(keys[i], fallbacks[i]);
            var mii = new MENUITEMINFO
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MenuItemMask.MIIM_ID | MenuItemMask.MIIM_STRING | MenuItemMask.MIIM_FTYPE | MenuItemMask.MIIM_STATE,
                fType = (uint)MenuFlags.MF_STRING,
                fState = (uint)(MenuFlags.MF_UNCHECKED),
                wID = (uint)(baseId + i),
                dwTypeData = text,
                cch = (uint)text.Length,
            };
            User32.InsertMenuItemW(hMenu, uint.MaxValue, true, ref mii);
        }
    }

    private void AddSeparator(nint hMenu)
    {
        var mii = new MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MenuItemMask.MIIM_FTYPE,
            fType = (uint)MenuFlags.MF_SEPARATOR,
        };
        User32.InsertMenuItemW(hMenu, uint.MaxValue, true, ref mii);
    }

    private void AddSubMenu(nint hParent, string text, nint hSubMenu)
    {
        var mii = new MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MenuItemMask.MIIM_STRING | MenuItemMask.MIIM_FTYPE | MenuItemMask.MIIM_SUBMENU | MenuItemMask.MIIM_ID,
            fType = (uint)MenuFlags.MF_STRING,
            fState = (uint)MenuFlags.MF_ENABLED,
            wID = (uint)_nextSubMenuId++,
            hSubMenu = hSubMenu,
            dwTypeData = text,
            cch = (uint)text.Length,
        };
        User32.InsertMenuItemW(hParent, uint.MaxValue, true, ref mii);
    }

    private void RefreshAccentRadioState(int baseId, int selectedIndex)
    {
        for (int i = 0; i < 5; i++)
        {
            SetItemCheckState((uint)(baseId + i), i == selectedIndex);
        }
    }

    private void RefreshToggleState(int commandId, bool isChecked)
    {
        SetItemCheckState((uint)commandId, isChecked);
    }

    /// <summary>
    /// Updates the checked state of the item with the given command ID.
    /// Items live in submenus, and SetMenuItemInfoW only searches the menu handle
    /// it is given, so every menu has to be tried until the ID is found.
    /// </summary>
    private void SetItemCheckState(uint commandId, bool isChecked)
    {
        var mii = new MENUITEMINFO
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MenuItemMask.MIIM_STATE,
            fState = isChecked ? (uint)MenuFlags.MF_CHECKED : (uint)MenuFlags.MF_UNCHECKED,
        };

        if (_hMenu != nint.Zero && User32.SetMenuItemInfoW(_hMenu, commandId, false, ref mii))
            return;

        foreach (var hSubMenu in _subMenus.Values)
        {
            if (hSubMenu != nint.Zero && User32.SetMenuItemInfoW(hSubMenu, commandId, false, ref mii))
                return;
        }
    }

    private void ApplyAndSave()
    {
        _configService.Save();
        _stateResolver.Refresh();
    }

    public void DestroyMenu()
    {
        if (_hMenu != nint.Zero)
        {
            User32.DestroyMenu(_hMenu);
            _hMenu = nint.Zero;
        }
        _subMenus.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyMenu();
    }
}

/// <summary>
/// Taskbar states for color picker requests from the tray menu.
/// Uses Models.DynamicState for consistency.
/// </summary>
// Using TranslucentTabBar.Models.DynamicState instead of a duplicate enum
