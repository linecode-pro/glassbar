namespace TranslucentTabBar.Models;

/// <summary>
/// Represents the different states/situations that can trigger a taskbar appearance change.
/// </summary>
public enum DynamicState
{
    /// <summary>Default desktop state (no special conditions).</summary>
    Desktop = 0,

    /// <summary>At least one window is visible on the desktop.</summary>
    VisibleWindow = 1,

    /// <summary>A window is maximized.</summary>
    MaximizedWindow = 2,

    /// <summary>The Start menu is open.</summary>
    StartOpened = 3,

    /// <summary>The Search/Cortana panel is open.</summary>
    SearchOpened = 4,

    /// <summary>Task View (virtual desktops) is open.</summary>
    TaskViewOpened = 5,

    /// <summary>Battery saver mode is active.</summary>
    BatterySaver = 6,
}
