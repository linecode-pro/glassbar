using System.Text.Json.Serialization;
using Windows.UI;

namespace TranslucentTabBar.Models;

/// <summary>
/// Defines how a taskbar should appear for a given state.
/// </summary>
public class TaskbarAppearance
{
    /// <summary>The accent/effect mode to apply.</summary>
    [JsonPropertyName("accent")]
    public TaskbarAccentMode Accent { get; set; } = TaskbarAccentMode.Clear;

    /// <summary>The color to tint the taskbar (ARGB).</summary>
    [JsonConverter(typeof(ColorJsonConverter))]
    [JsonPropertyName("color")]
    public Color Color { get; set; } = Color.FromArgb(0, 0, 0, 0);

    /// <summary>Whether to show the Aero Peek button (Win10) or taskbar line (Win11).</summary>
    [JsonPropertyName("show_peek")]
    public bool ShowPeek { get; set; } = true;

    /// <summary>Whether to show the taskbar line on Windows 11.</summary>
    [JsonPropertyName("show_line")]
    public bool ShowLine { get; set; } = true;

    /// <summary>The blur radius for Blur/Acrylic effects (0-750).</summary>
    [JsonPropertyName("blur_radius")]
    public float BlurRadius { get; set; } = 9.0f;

    public TaskbarAppearance() { }

    public TaskbarAppearance(TaskbarAccentMode accent, Color color, bool showPeek, bool showLine, float blurRadius)
    {
        Accent = accent;
        Color = color;
        ShowPeek = showPeek;
        ShowLine = showLine;
        BlurRadius = Math.Clamp(blurRadius, 0, 750);
    }
}

/// <summary>
/// An optional taskbar appearance with an enabled/disabled toggle.
/// </summary>
public class OptionalTaskbarAppearance : TaskbarAppearance
{
    /// <summary>Whether this dynamic mode is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    public OptionalTaskbarAppearance() { }

    public OptionalTaskbarAppearance(bool enabled, TaskbarAccentMode accent, Color color, bool showPeek, bool showLine, float blurRadius)
        : base(accent, color, showPeek, showLine, blurRadius)
    {
        Enabled = enabled;
    }
}

/// <summary>
/// A taskbar appearance with per-window rules (class, title, file).
/// </summary>
public class RuledTaskbarAppearance : OptionalTaskbarAppearance
{
    /// <summary>Per-window-class overrides.</summary>
    [JsonPropertyName("rules")]
    public WindowRules Rules { get; set; } = new();

    public RuledTaskbarAppearance() { }

    public RuledTaskbarAppearance(bool enabled, TaskbarAccentMode accent, Color color, bool showPeek, bool showLine, float blurRadius)
        : base(enabled, accent, color, showPeek, showLine, blurRadius)
    {
    }
}

/// <summary>
/// Per-window rules that can override the taskbar appearance.
/// </summary>
public class WindowRules
{
    [JsonPropertyName("class")]
    public Dictionary<string, ActiveInactiveAppearance> ClassRules { get; set; } = new();

    [JsonPropertyName("title")]
    public Dictionary<string, ActiveInactiveAppearance> TitleRules { get; set; } = new();

    [JsonPropertyName("file")]
    public Dictionary<string, ActiveInactiveAppearance> FileRules { get; set; } = new();
}

/// <summary>
/// An appearance variant for active and inactive states of a specific window.
/// </summary>
public class ActiveInactiveAppearance
{
    [JsonPropertyName("active")]
    public TaskbarAppearance? Active { get; set; }

    [JsonPropertyName("inactive")]
    public TaskbarAppearance? Inactive { get; set; }
}
