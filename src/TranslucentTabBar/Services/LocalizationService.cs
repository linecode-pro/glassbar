using System.Globalization;
using System.Linq;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace TranslucentTabBar.Services;

/// <summary>
/// Manages application localization and language switching.
/// Dynamically discovers available languages from the Strings/ resource directory.
/// </summary>
public class LocalizationService
{
    private static IReadOnlyList<LanguageInfo>? _availableLanguages;

    /// <summary>
    /// Available languages, discovered from the Strings/ resource directory.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> AvailableLanguages
    {
        get
        {
            _availableLanguages ??= DiscoverLanguages();
            return _availableLanguages;
        }
    }

    private static IReadOnlyList<LanguageInfo> DiscoverLanguages()
    {
        var languages = new List<LanguageInfo>();

        var supportedCodes = new[]
        {
            "en-US", "ru-RU", "zh-CN", "hi-IN", "es-ES", "fr-FR", "ar-SA", "bn-BD", "pt-BR", "de-DE"
        };

        foreach (var code in supportedCodes)
        {
            languages.Add(CreateLanguageInfo(code));
        }

        return languages.OrderBy(l => l.DisplayName).ToList();
    }

    private static LanguageInfo CreateLanguageInfo(string code)
    {
        string displayName;
        try
        {
            var culture = new CultureInfo(code);
            displayName = culture.NativeName;
            if (string.IsNullOrEmpty(displayName))
                displayName = culture.EnglishName;
        }
        catch
        {
            displayName = code;
        }
        return new LanguageInfo(code, displayName);
    }

    private string _currentLanguage = "en-US";
    private ResourceManager? _resourceManager;
    private ResourceContext? _resourceContext;
    private ResourceMap? _resourceMap;

    /// <summary>
    /// Raised when the application language changes.
    /// </summary>
    public event Action? LanguageChanged;

    /// <summary>
    /// Gets the current language code (e.g., "en-US", "ru-RU").
    /// </summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Gets the display name of the current language.
    /// </summary>
    public string CurrentLanguageDisplayName
    {
        get
        {
            foreach (var lang in AvailableLanguages)
            {
                if (lang.Code == _currentLanguage)
                    return lang.DisplayName;
            }
            return "English";
        }
    }

    public LocalizationService(string? initialLanguage = null)
    {
        InitResourceManager();

        if (!string.IsNullOrEmpty(initialLanguage))
        {
            var resolved = ResolveLanguage(initialLanguage);
            if (resolved != null)
            {
                _currentLanguage = resolved;
                ApplyLanguage(_currentLanguage);
                return;
            }
        }

        var systemLanguages = Enumerable.Empty<string>();
        try
        {
            systemLanguages = ApplicationLanguages.Languages ?? Enumerable.Empty<string>();
        }
        catch { }

        foreach (var sysLang in systemLanguages)
        {
            var resolved = ResolveLanguage(sysLang);
            if (resolved != null)
            {
                _currentLanguage = resolved;
                ApplyLanguage(_currentLanguage);
                return;
            }
        }

        _currentLanguage = "en-US";
        ApplyLanguage(_currentLanguage);
    }

    private void InitResourceManager()
    {
        try
        {
            _resourceManager = new ResourceManager();
            _resourceContext = _resourceManager.CreateResourceContext();
            _resourceMap = _resourceManager.MainResourceMap.GetSubtree("Resources");
        }
        catch
        {
            // Fallback if unpackaged or initialization fails
        }
    }

    /// <summary>
    /// Sets the application language.
    /// </summary>
    public void SetLanguage(string languageCode)
    {
        var resolved = ResolveLanguage(languageCode);
        if (resolved == null)
            return;

        if (_currentLanguage == resolved)
            return;

        _currentLanguage = resolved;
        ApplyLanguage(resolved);
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// Gets a localized string by key from the default Resources.resw.
    /// </summary>
    public string GetString(string key)
    {
        try
        {
            if (_resourceMap != null && _resourceContext != null)
            {
                _resourceContext.QualifierValues["Language"] = _currentLanguage;

                // WinAppSDK MRT translates dotted keys (Settings_Desktop.Text) to subtrees (Settings_Desktop/Text)
                var lookupKey = key.Contains('.') ? key.Replace('.', '/') : key;
                var candidate = _resourceMap.TryGetValue(lookupKey, _resourceContext)
                             ?? _resourceMap.TryGetValue(key, _resourceContext);

                if (candidate != null && !string.IsNullOrEmpty(candidate.ValueAsString))
                {
                    return candidate.ValueAsString;
                }
            }
        }
        catch { }

        // Fallback to UWP ResourceLoader if available
        try
        {
            var loader = Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse();
            var str = loader.GetString(key);
            if (string.IsNullOrEmpty(str) && key.Contains('.'))
            {
                str = loader.GetString(key.Replace('.', '/'));
            }
            if (!string.IsNullOrEmpty(str))
                return str;
        }
        catch { }

        return key;
    }

    /// <summary>
    /// Gets a localized string from a specific resource map.
    /// </summary>
    public string GetString(string mapName, string key)
    {
        try
        {
            if (_resourceManager != null && _resourceContext != null)
            {
                _resourceContext.QualifierValues["Language"] = _currentLanguage;
                var map = _resourceManager.MainResourceMap.GetSubtree(mapName);

                var lookupKey = key.Contains('.') ? key.Replace('.', '/') : key;
                var candidate = map.TryGetValue(lookupKey, _resourceContext)
                             ?? map.TryGetValue(key, _resourceContext);

                if (candidate != null && !string.IsNullOrEmpty(candidate.ValueAsString))
                {
                    return candidate.ValueAsString;
                }
            }
        }
        catch { }

        try
        {
            var loader = Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse(mapName);
            var str = loader.GetString(key);
            if (string.IsNullOrEmpty(str) && key.Contains('.'))
            {
                str = loader.GetString(key.Replace('.', '/'));
            }
            if (!string.IsNullOrEmpty(str))
                return str;
        }
        catch { }

        return key;
    }

    private void ApplyLanguage(string languageCode)
    {
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageCode;
        }
        catch { }

        try
        {
            if (_resourceContext != null)
            {
                _resourceContext.QualifierValues["Language"] = languageCode;
            }
        }
        catch { }
    }

    private static string? ResolveLanguage(string code)
    {
        var exact = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact.Code;

        var prefix = AvailableLanguages.FirstOrDefault(l =>
            l.Code.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith(l.Code.Split('-')[0] + "-", StringComparison.OrdinalIgnoreCase));
        return prefix?.Code;
    }

    private static bool ValidateLanguage(string code)
    {
        return ResolveLanguage(code) != null;
    }
}

/// <summary>
/// Information about a supported language.
/// </summary>
public class LanguageInfo
{
    public string Code { get; }
    public string DisplayName { get; }

    public LanguageInfo(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}