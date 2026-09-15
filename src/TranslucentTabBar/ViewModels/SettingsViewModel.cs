using System.ComponentModel;
using System.Runtime.CompilerServices;
using TranslucentTabBar.Models;
using TranslucentTabBar.Services;

namespace TranslucentTabBar.ViewModels;

/// <summary>
/// ViewModel for the main settings page / tray context menu.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ConfigService _configService;
    private readonly StateResolverService _stateResolver;
    private readonly LocalizationService _localizationService;

    private TaskbarAppearance? _desktopAppearance;
    private RuledTaskbarAppearance? _visibleWindowAppearance;
    private RuledTaskbarAppearance? _maximizedWindowAppearance;
    private OptionalTaskbarAppearance? _startOpenedAppearance;
    private OptionalTaskbarAppearance? _searchOpenedAppearance;
    private OptionalTaskbarAppearance? _taskViewOpenedAppearance;
    private OptionalTaskbarAppearance? _batterySaverAppearance;
    private bool _isStartupEnabled;
    private bool _isDisableSaving;
    private string _currentLanguage;
    private string _selectedLanguage;
    private bool _isBlurSupported;

    public SettingsViewModel(ConfigService configService, StateResolverService stateResolver, LocalizationService localizationService)
    {
        _configService = configService;
        _stateResolver = stateResolver;
        _localizationService = localizationService;

        _currentLanguage = _localizationService.CurrentLanguage;
        _selectedLanguage = _currentLanguage;

        LoadFromConfig();
    }

    public TaskbarAppearance? DesktopAppearance
    {
        get => _desktopAppearance;
        set { _desktopAppearance = value; OnPropertyChanged(); }
    }

    public RuledTaskbarAppearance? VisibleWindowAppearance
    {
        get => _visibleWindowAppearance;
        set { _visibleWindowAppearance = value; OnPropertyChanged(); }
    }

    public RuledTaskbarAppearance? MaximizedWindowAppearance
    {
        get => _maximizedWindowAppearance;
        set { _maximizedWindowAppearance = value; OnPropertyChanged(); }
    }

    public OptionalTaskbarAppearance? StartOpenedAppearance
    {
        get => _startOpenedAppearance;
        set { _startOpenedAppearance = value; OnPropertyChanged(); }
    }

    public OptionalTaskbarAppearance? SearchOpenedAppearance
    {
        get => _searchOpenedAppearance;
        set { _searchOpenedAppearance = value; OnPropertyChanged(); }
    }

    public OptionalTaskbarAppearance? TaskViewOpenedAppearance
    {
        get => _taskViewOpenedAppearance;
        set { _taskViewOpenedAppearance = value; OnPropertyChanged(); }
    }

    public OptionalTaskbarAppearance? BatterySaverAppearance
    {
        get => _batterySaverAppearance;
        set { _batterySaverAppearance = value; OnPropertyChanged(); }
    }

    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        set { _isStartupEnabled = value; OnPropertyChanged(); }
    }

    public bool IsDisableSaving
    {
        get => _isDisableSaving;
        set { _isDisableSaving = value; OnPropertyChanged(); }
    }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage != value)
            {
                _selectedLanguage = value;
                OnPropertyChanged();
                ApplyLanguage();
            }
        }
    }

    public bool IsBlurSupported
    {
        get => _isBlurSupported;
        set { _isBlurSupported = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<Services.LanguageInfo> AvailableLanguages =>
        Services.LocalizationService.AvailableLanguages;

    public void LoadFromConfig()
    {
        var config = _configService.Config;

        DesktopAppearance = config.DesktopAppearance;
        VisibleWindowAppearance = config.VisibleWindowAppearance;
        MaximizedWindowAppearance = config.MaximizedWindowAppearance;
        StartOpenedAppearance = config.StartOpenedAppearance;
        SearchOpenedAppearance = config.SearchOpenedAppearance;
        TaskViewOpenedAppearance = config.TaskViewOpenedAppearance;
        BatterySaverAppearance = config.BatterySaverAppearance;

        IsDisableSaving = config.DisableSaving;
        CurrentLanguage = config.Language;
        SelectedLanguage = CurrentLanguage;

        // Blur is supported on Windows 10 1903+ and Windows 11
        IsBlurSupported = true; // We'll detect OS version properly later
    }

    public void SaveToConfig()
    {
        var config = _configService.Config;

        if (DesktopAppearance != null) config.DesktopAppearance = DesktopAppearance;
        if (VisibleWindowAppearance != null) config.VisibleWindowAppearance = VisibleWindowAppearance;
        if (MaximizedWindowAppearance != null) config.MaximizedWindowAppearance = MaximizedWindowAppearance;
        if (StartOpenedAppearance != null) config.StartOpenedAppearance = StartOpenedAppearance;
        if (SearchOpenedAppearance != null) config.SearchOpenedAppearance = SearchOpenedAppearance;
        if (TaskViewOpenedAppearance != null) config.TaskViewOpenedAppearance = TaskViewOpenedAppearance;
        if (BatterySaverAppearance != null) config.BatterySaverAppearance = BatterySaverAppearance;

        config.DisableSaving = IsDisableSaving;
        config.Language = CurrentLanguage;

        _configService.Save();
    }

    private void ApplyLanguage()
    {
        _localizationService.SetLanguage(_selectedLanguage);
        CurrentLanguage = _selectedLanguage;

        // Save language preference
        _configService.Config.Language = _selectedLanguage;
        _configService.Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
