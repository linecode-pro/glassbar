namespace TranslucentTabBar.Models;

/// <summary>
/// Taskbar background effect states, matching the undocumented ACCENT_STATE enum.
/// </summary>
public enum AccentState
{
    /// <summary>Default - background is black.</summary>
    Disabled = 0,

    /// <summary>Background is a solid gradient color (alpha ignored).</summary>
    EnableGradient = 1,

    /// <summary>Background is transparent gradient with alpha.</summary>
    EnableTransparentGradient = 2,

    /// <summary>Background has a blur effect behind it.</summary>
    EnableBlurBehind = 3,

    /// <summary>Background has acrylic blur effect (Fluent Design).</summary>
    EnableAcrylicBlurBehind = 4,

    /// <summary>Allows desktop apps to use Compositor.CreateHostBackdropBrush.</summary>
    EnableHostBackdrop = 5,

    /// <summary>Unknown - draws background fully transparent.</summary>
    InvalidState = 6,
}

/// <summary>
/// User-friendly taskbar states exposed in the UI.
/// Maps to AccentState values for internal use.
/// </summary>
public enum TaskbarAccentMode
{
    /// <summary>Regular Windows style (as if TranslucentTB is not running).</summary>
    Normal = 0,

    /// <summary>Tinted taskbar without transparency.</summary>
    Opaque = 1,

    /// <summary>Tinted taskbar with transparency.</summary>
    Clear = 2,

    /// <summary>Blurred taskbar background.</summary>
    Blur = 3,

    /// <summary>Acrylic (Fluent Design) taskbar background.</summary>
    Acrylic = 4,
}
