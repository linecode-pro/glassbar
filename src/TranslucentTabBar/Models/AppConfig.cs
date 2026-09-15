using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranslucentTabBar.Models;

/// <summary>
/// Root configuration model matching the TranslucentTB settings.json schema.
/// </summary>
public class AppConfig
{
    private const string DefaultLanguage = "en-US";

    // ── Appearance States ────────────────────────────────────────────

    [JsonPropertyName("desktop_appearance")]
    public TaskbarAppearance DesktopAppearance { get; set; } = new()
    {
        Accent = TaskbarAccentMode.Normal,
        Color = Windows.UI.Color.FromArgb(0, 0, 0, 0),
        ShowPeek = true,
        ShowLine = true,
        BlurRadius = 9.0f,
    };

    [JsonPropertyName("visible_window_appearance")]
    public RuledTaskbarAppearance VisibleWindowAppearance { get; set; } = new(false, TaskbarAccentMode.Normal, Windows.UI.Color.FromArgb(0, 0, 0, 0), true, true, 9.0f);

    [JsonPropertyName("maximized_window_appearance")]
    public RuledTaskbarAppearance MaximizedWindowAppearance { get; set; } = new(false, TaskbarAccentMode.Normal, Windows.UI.Color.FromArgb(0, 0, 0, 0), true, true, 9.0f);

    [JsonPropertyName("start_opened_appearance")]
    public OptionalTaskbarAppearance StartOpenedAppearance { get; set; } = new(false, TaskbarAccentMode.Normal, Windows.UI.Color.FromArgb(0, 0, 0, 0), true, true, 9.0f);

    [JsonPropertyName("search_opened_appearance")]
    public OptionalTaskbarAppearance SearchOpenedAppearance { get; set; } = new(false, TaskbarAccentMode.Normal, Windows.UI.Color.FromArgb(0, 0, 0, 0), true, true, 9.0f);

    [JsonPropertyName("task_view_opened_appearance")]
    public OptionalTaskbarAppearance TaskViewOpenedAppearance { get; set; } = new(false, TaskbarAccentMode.Normal, Windows.UI.Color.FromArgb(0, 0, 0, 0), true, true, 9.0f);

    [JsonPropertyName("battery_saver_appearance")]
    public OptionalTaskbarAppearance BatterySaverAppearance { get; set; } = new(false, TaskbarAccentMode.Normal, Windows.UI.Color.FromArgb(0, 0, 0, 0), true, true, 9.0f);

    // ── Advanced Settings ────────────────────────────────────────────

    /// <summary>Windows to ignore when determining the visible/maximized state.</summary>
    [JsonPropertyName("ignored_windows")]
    public List<string> IgnoredWindows { get; set; } = new();

    /// <summary>Whether to hide the tray icon.</summary>
    [JsonPropertyName("hide_tray")]
    public bool? HideTray { get; set; }

    /// <summary>Disable saving of configuration changes.</summary>
    [JsonPropertyName("disable_saving")]
    public bool DisableSaving { get; set; } = false;

    /// <summary>The application language code (e.g. "en-US", "ru-RU").</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = DefaultLanguage;

    /// <summary>Log verbosity level.</summary>
    [JsonPropertyName("verbosity")]
    public string Verbosity { get; set; } = "warn";

    /// <summary>Whether to use the XAML context menu instead of Win32.</summary>
    [JsonPropertyName("use_xaml_context_menu")]
    public bool? UseXamlContextMenu { get; set; }

    /// <summary>Whether to copy DLLs for portable mode.</summary>
    [JsonPropertyName("copy_dlls")]
    public bool? CopyDlls { get; set; }

    /// <summary>
    /// Serializes the config to a JSON string.
    /// </summary>
    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        var json = JsonSerializer.Serialize(this, options);

        // Add $schema reference as the first property
        var obj = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
        if (obj != null)
        {
            obj.Insert(0, "$schema", "settings.schema.json");
            return obj.ToJsonString(options);
        }

        return json;
    }

    /// <summary>
    /// Deserializes the config from a JSON string.
    /// </summary>
    public static AppConfig FromJson(string json)
    {
        return JsonSerializer.Deserialize(json, ConfigSerializerContext.Default.AppConfig) ?? new AppConfig();
    }
}

/// <summary>
/// JSON serializer context for source-generated serialization.
/// </summary>
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(TaskbarAppearance))]
[JsonSerializable(typeof(OptionalTaskbarAppearance))]
[JsonSerializable(typeof(RuledTaskbarAppearance))]
[JsonSerializable(typeof(WindowRules))]
[JsonSerializable(typeof(ActiveInactiveAppearance))]
internal partial class ConfigSerializerContext : JsonSerializerContext
{
}

/// <summary>
/// Custom JSON converter for Windows.UI.Color (serializes as #AARRGGBB hex string).
/// </summary>
public class ColorJsonConverter : JsonConverter<Windows.UI.Color>
{
    public override Windows.UI.Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hex = reader.GetString();
        if (Helpers.ColorHelper.TryFromHex(hex, out var color))
            return color;
        return Windows.UI.Color.FromArgb(0, 0, 0, 0);
    }

    public override void Write(Utf8JsonWriter writer, Windows.UI.Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(Helpers.ColorHelper.ToHex(value));
    }
}
