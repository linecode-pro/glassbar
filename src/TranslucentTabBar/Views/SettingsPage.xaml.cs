using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TranslucentTabBar.Models;
using TranslucentTabBar.Services;
using TranslucentTabBar.ViewModels;
using Windows.UI;

namespace TranslucentTabBar.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;
    private readonly ConfigService _configService;
    private readonly LocalizationService _localizationService;
    private readonly TaskbarService _taskbarService;
    private readonly StateResolverService _stateResolver;
    private readonly StartupManager _startupManager;

    // Color buttons for each state
    private readonly Dictionary<string, Button> _colorButtons = new();
    private readonly Dictionary<string, Color> _currentColors = new();
    private bool _isLoading = true;
    private readonly DispatcherQueueTimer _saveTimer;

    // Events
    public event Action? ExitRequested;
    public event Action? AboutRequested;
    public event Action<DynamicState, Color>? ColorRequested;

    public SettingsPage(
        SettingsViewModel viewModel,
        ConfigService configService,
        LocalizationService localizationService,
        TaskbarService taskbarService,
        StateResolverService stateResolver,
        StartupManager startupManager)
    {
        this.InitializeComponent();

        _viewModel = viewModel;
        _configService = configService;
        _localizationService = localizationService;
        _taskbarService = taskbarService;
        _stateResolver = stateResolver;
        _startupManager = startupManager;

        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(150);
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            DoSave();
        };

        // Register color buttons
        _colorButtons["Desktop"] = DesktopColorButton;
        _colorButtons["Visible"] = VisibleColorButton;
        _colorButtons["Maximized"] = MaximizedColorButton;
        _colorButtons["Start"] = StartColorButton;
        _colorButtons["Search"] = SearchColorButton;
        _colorButtons["TaskView"] = TaskViewColorButton;
        _colorButtons["Battery"] = BatteryColorButton;

        // Set up language selector
        LanguageCombo.ItemsSource = LocalizationService.AvailableLanguages;
        var activeLang = _configService.Config.Language ?? _localizationService.CurrentLanguage;
        LanguageCombo.SelectedItem = LocalizationService.AvailableLanguages
            .FirstOrDefault(l => l.Code == activeLang)
            ?? LocalizationService.AvailableLanguages[0];

        ApplyLocalization();
        LoadSettings();

        // Wire up save-on-change events for all controls after loading
        WireUpChangeEvents();
    }

    public void ApplyLocalization()
    {
        LanguageLabel.Text = _localizationService.GetString("Settings_Language.Text");
        LanguageCombo.PlaceholderText = _localizationService.GetString("Settings_LanguageCombo.PlaceholderText");

        DesktopLabel.Text = _localizationService.GetString("Settings_Desktop.Text");
        DesktopShowLine.Content = _localizationService.GetString("Settings_ShowLine.Content");

        VisibleWindowLabel.Text = _localizationService.GetString("Settings_VisibleWindow.Text");
        MaximizedWindowLabel.Text = _localizationService.GetString("Settings_MaximizedWindow.Text");
        StartOpenedLabel.Text = _localizationService.GetString("Settings_StartOpened.Text");
        SearchOpenedLabel.Text = _localizationService.GetString("Settings_SearchOpened.Text");
        TaskViewOpenedLabel.Text = _localizationService.GetString("Settings_TaskViewOpened.Text");
        BatterySaverLabel.Text = _localizationService.GetString("Settings_BatterySaver.Text");

        var enabledText = _localizationService.GetString("Settings_Enabled.Header");
        VisibleWindowEnabled.Header = enabledText;
        MaximizedWindowEnabled.Header = enabledText;
        StartOpenedEnabled.Header = enabledText;
        SearchOpenedEnabled.Header = enabledText;
        TaskViewOpenedEnabled.Header = enabledText;
        BatterySaverEnabled.Header = enabledText;

        AdvancedLabel.Text = _localizationService.GetString("Settings_Advanced.Text");
        StartAtBootToggle.Header = _localizationService.GetString("Settings_StartAtBoot.Header");
        DisableSavingToggle.Header = _localizationService.GetString("Settings_DisableSaving.Header");
        HelpText.Text = _localizationService.GetString("Settings_Help.Content");
        ResetText.Text = _localizationService.GetString("Settings_ResetDefaults.Content");
        AboutText.Text = _localizationService.GetString("Settings_About.Content");
        ExitText.Text = _localizationService.GetString("Settings_Exit.Content");

        var blurHeader = _localizationService.GetString("Settings_BlurRadius.Header");
        DesktopBlurRadius.Header = blurHeader;
        VisibleBlurRadius.Header = blurHeader;
        MaximizedBlurRadius.Header = blurHeader;
        StartBlurRadius.Header = blurHeader;
        SearchBlurRadius.Header = blurHeader;
        TaskViewBlurRadius.Header = blurHeader;
        BatteryBlurRadius.Header = blurHeader;

        var accentModes = new[]
        {
            _localizationService.GetString("Accent_Normal"),
            _localizationService.GetString("Accent_Opaque"),
            _localizationService.GetString("Accent_Clear"),
            _localizationService.GetString("Accent_Blur"),
            _localizationService.GetString("Accent_Acrylic"),
        };

        UpdateComboItems(DesktopAccentCombo, accentModes);
        UpdateComboItems(VisibleAccentCombo, accentModes);
        UpdateComboItems(MaximizedAccentCombo, accentModes);
        UpdateComboItems(StartAccentCombo, accentModes);
        UpdateComboItems(SearchAccentCombo, accentModes);
        UpdateComboItems(TaskViewAccentCombo, accentModes);
        UpdateComboItems(BatteryAccentCombo, accentModes);
    }

    private static void UpdateComboItems(ComboBox combo, string[] items)
    {
        int prev = combo.SelectedIndex;
        combo.ItemsSource = items;
        if (prev >= 0 && prev < items.Length)
        {
            combo.SelectedIndex = prev;
        }
    }

    private void OnAccentSelectionChanged(string state, ComboBox combo)
    {
        if (_isLoading) return;
        var mode = (TaskbarAccentMode)combo.SelectedIndex;
        if (mode == TaskbarAccentMode.Clear || mode == TaskbarAccentMode.Blur)
        {
            if (_currentColors.TryGetValue(state, out var color) && color.A == 255)
            {
                var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                _currentColors[state] = transparent;
                UpdateColorButton(state, transparent);
            }
        }
        UpdateBlurVisibility();
        SaveSettings();
    }

    private void WireUpChangeEvents()
    {
        // Desktop
        DesktopAccentCombo.SelectionChanged += (_, _) => OnAccentSelectionChanged("Desktop", DesktopAccentCombo);
        DesktopShowLine.Checked += (_, _) => SaveSettings();
        DesktopShowLine.Unchecked += (_, _) => SaveSettings();
        DesktopBlurRadius.ValueChanged += (_, _) => SaveSettings();

        // Visible Window
        VisibleWindowEnabled.Toggled += (_, _) => SaveSettings();
        VisibleAccentCombo.SelectionChanged += (_, _) => OnAccentSelectionChanged("Visible", VisibleAccentCombo);
        VisibleBlurRadius.ValueChanged += (_, _) => SaveSettings();

        // Maximized Window
        MaximizedWindowEnabled.Toggled += (_, _) => SaveSettings();
        MaximizedAccentCombo.SelectionChanged += (_, _) => OnAccentSelectionChanged("Maximized", MaximizedAccentCombo);
        MaximizedBlurRadius.ValueChanged += (_, _) => SaveSettings();

        // Start Opened
        StartOpenedEnabled.Toggled += (_, _) => SaveSettings();
        StartAccentCombo.SelectionChanged += (_, _) => OnAccentSelectionChanged("Start", StartAccentCombo);
        StartBlurRadius.ValueChanged += (_, _) => SaveSettings();

        // Search Opened
        SearchOpenedEnabled.Toggled += (_, _) => SaveSettings();
        SearchAccentCombo.SelectionChanged += (_, _) => OnAccentSelectionChanged("Search", SearchAccentCombo);
        SearchBlurRadius.ValueChanged += (_, _) => SaveSettings();

        // Task View Opened
        TaskViewOpenedEnabled.Toggled += (_, _) => SaveSettings();
        TaskViewAccentCombo.SelectionChanged += (_, _) => OnAccentSelectionChanged("TaskView", TaskViewAccentCombo);
        TaskViewBlurRadius.ValueChanged += (_, _) => SaveSettings();

        // Battery Saver
        BatterySaverEnabled.Toggled += (_, _) => SaveSettings();
        BatteryAccentCombo.SelectionChanged += (_, _) => OnAccentSelectionChanged("Battery", BatteryAccentCombo);
        BatteryBlurRadius.ValueChanged += (_, _) => SaveSettings();

        // Advanced
        StartAtBootToggle.Toggled += async (_, _) =>
        {
            if (_isLoading) return;
            await _startupManager.SetRegisteredAsync(StartAtBootToggle.IsOn);
        };
        DisableSavingToggle.Toggled += (_, _) => SaveSettings();
    }

    public void LoadSettings()
    {
        _isLoading = true;
        try
        {
            var config = _configService.Config;

            // Cache colors
            _currentColors["Desktop"] = config.DesktopAppearance.Color;
            _currentColors["Visible"] = config.VisibleWindowAppearance.Color;
            _currentColors["Maximized"] = config.MaximizedWindowAppearance.Color;
            _currentColors["Start"] = config.StartOpenedAppearance.Color;
            _currentColors["Search"] = config.SearchOpenedAppearance.Color;
            _currentColors["TaskView"] = config.TaskViewOpenedAppearance.Color;
            _currentColors["Battery"] = config.BatterySaverAppearance.Color;

            // Desktop
            DesktopAccentCombo.SelectedIndex = (int)config.DesktopAppearance.Accent;
            DesktopShowLine.IsChecked = config.DesktopAppearance.ShowLine;
            DesktopBlurRadius.Value = config.DesktopAppearance.BlurRadius;
            UpdateColorButton("Desktop", _currentColors["Desktop"]);

            // Visible Window
            VisibleWindowEnabled.IsOn = config.VisibleWindowAppearance.Enabled;
            VisibleAccentCombo.SelectedIndex = (int)config.VisibleWindowAppearance.Accent;
            VisibleBlurRadius.Value = config.VisibleWindowAppearance.BlurRadius;
            UpdateColorButton("Visible", _currentColors["Visible"]);

            // Maximized Window
            MaximizedWindowEnabled.IsOn = config.MaximizedWindowAppearance.Enabled;
            MaximizedAccentCombo.SelectedIndex = (int)config.MaximizedWindowAppearance.Accent;
            MaximizedBlurRadius.Value = config.MaximizedWindowAppearance.BlurRadius;
            UpdateColorButton("Maximized", _currentColors["Maximized"]);

            // Start Opened
            StartOpenedEnabled.IsOn = config.StartOpenedAppearance.Enabled;
            StartAccentCombo.SelectedIndex = (int)config.StartOpenedAppearance.Accent;
            StartBlurRadius.Value = config.StartOpenedAppearance.BlurRadius;
            UpdateColorButton("Start", _currentColors["Start"]);

            // Search Opened
            SearchOpenedEnabled.IsOn = config.SearchOpenedAppearance.Enabled;
            SearchAccentCombo.SelectedIndex = (int)config.SearchOpenedAppearance.Accent;
            SearchBlurRadius.Value = config.SearchOpenedAppearance.BlurRadius;
            UpdateColorButton("Search", _currentColors["Search"]);

            // Task View Opened
            TaskViewOpenedEnabled.IsOn = config.TaskViewOpenedAppearance.Enabled;
            TaskViewAccentCombo.SelectedIndex = (int)config.TaskViewOpenedAppearance.Accent;
            TaskViewBlurRadius.Value = config.TaskViewOpenedAppearance.BlurRadius;
            UpdateColorButton("TaskView", _currentColors["TaskView"]);

            // Battery Saver
            BatterySaverEnabled.IsOn = config.BatterySaverAppearance.Enabled;
            BatteryAccentCombo.SelectedIndex = (int)config.BatterySaverAppearance.Accent;
            BatteryBlurRadius.Value = config.BatterySaverAppearance.BlurRadius;
            UpdateColorButton("Battery", _currentColors["Battery"]);

            // Advanced
            StartAtBootToggle.IsOn = _startupManager.IsRegistered;
            DisableSavingToggle.IsOn = config.DisableSaving;

            UpdateBlurVisibility();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SaveSettings()
    {
        if (_isLoading) return;

        SyncConfigFromUI();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SyncConfigFromUI()
    {
        var config = _configService.Config;

        config.DesktopAppearance.Accent = (TaskbarAccentMode)DesktopAccentCombo.SelectedIndex;
        config.DesktopAppearance.ShowLine = DesktopShowLine.IsChecked ?? true;
        if (_currentColors.TryGetValue("Desktop", out var desktopColor))
            config.DesktopAppearance.Color = desktopColor;
        config.DesktopAppearance.BlurRadius = (float)DesktopBlurRadius.Value;

        config.VisibleWindowAppearance.Enabled = VisibleWindowEnabled.IsOn;
        config.VisibleWindowAppearance.Accent = (TaskbarAccentMode)VisibleAccentCombo.SelectedIndex;
        if (_currentColors.TryGetValue("Visible", out var visibleColor))
            config.VisibleWindowAppearance.Color = visibleColor;
        config.VisibleWindowAppearance.BlurRadius = (float)VisibleBlurRadius.Value;

        config.MaximizedWindowAppearance.Enabled = MaximizedWindowEnabled.IsOn;
        config.MaximizedWindowAppearance.Accent = (TaskbarAccentMode)MaximizedAccentCombo.SelectedIndex;
        if (_currentColors.TryGetValue("Maximized", out var maxColor))
            config.MaximizedWindowAppearance.Color = maxColor;
        config.MaximizedWindowAppearance.BlurRadius = (float)MaximizedBlurRadius.Value;

        config.StartOpenedAppearance.Enabled = StartOpenedEnabled.IsOn;
        config.StartOpenedAppearance.Accent = (TaskbarAccentMode)StartAccentCombo.SelectedIndex;
        if (_currentColors.TryGetValue("Start", out var startColor))
            config.StartOpenedAppearance.Color = startColor;
        config.StartOpenedAppearance.BlurRadius = (float)StartBlurRadius.Value;

        config.SearchOpenedAppearance.Enabled = SearchOpenedEnabled.IsOn;
        config.SearchOpenedAppearance.Accent = (TaskbarAccentMode)SearchAccentCombo.SelectedIndex;
        if (_currentColors.TryGetValue("Search", out var searchColor))
            config.SearchOpenedAppearance.Color = searchColor;
        config.SearchOpenedAppearance.BlurRadius = (float)SearchBlurRadius.Value;

        config.TaskViewOpenedAppearance.Enabled = TaskViewOpenedEnabled.IsOn;
        config.TaskViewOpenedAppearance.Accent = (TaskbarAccentMode)TaskViewAccentCombo.SelectedIndex;
        if (_currentColors.TryGetValue("TaskView", out var tvColor))
            config.TaskViewOpenedAppearance.Color = tvColor;
        config.TaskViewOpenedAppearance.BlurRadius = (float)TaskViewBlurRadius.Value;

        config.BatterySaverAppearance.Enabled = BatterySaverEnabled.IsOn;
        config.BatterySaverAppearance.Accent = (TaskbarAccentMode)BatteryAccentCombo.SelectedIndex;
        if (_currentColors.TryGetValue("Battery", out var batColor))
            config.BatterySaverAppearance.Color = batColor;
        config.BatterySaverAppearance.BlurRadius = (float)BatteryBlurRadius.Value;

        config.DisableSaving = DisableSavingToggle.IsOn;
    }

    private void DoSave()
    {
        _configService.Save();
        _stateResolver.Refresh();
    }

    private void UpdateColorButton(string key, Color color)
    {
        if (_colorButtons.TryGetValue(key, out var button))
        {
            button.Background = new SolidColorBrush(color);
            button.Content = color.A == 0 ? "🎨" : "";
        }
    }

    private void UpdateBlurVisibility()
    {
        DesktopBlurRadius.Visibility = (DesktopAccentCombo.SelectedIndex == (int)TaskbarAccentMode.Blur) ? Visibility.Visible : Visibility.Collapsed;
        VisibleBlurRadius.Visibility = (VisibleAccentCombo.SelectedIndex == (int)TaskbarAccentMode.Blur) ? Visibility.Visible : Visibility.Collapsed;
        MaximizedBlurRadius.Visibility = (MaximizedAccentCombo.SelectedIndex == (int)TaskbarAccentMode.Blur) ? Visibility.Visible : Visibility.Collapsed;
        StartBlurRadius.Visibility = (StartAccentCombo.SelectedIndex == (int)TaskbarAccentMode.Blur) ? Visibility.Visible : Visibility.Collapsed;
        SearchBlurRadius.Visibility = (SearchAccentCombo.SelectedIndex == (int)TaskbarAccentMode.Blur) ? Visibility.Visible : Visibility.Collapsed;
        TaskViewBlurRadius.Visibility = (TaskViewAccentCombo.SelectedIndex == (int)TaskbarAccentMode.Blur) ? Visibility.Visible : Visibility.Collapsed;
        BatteryBlurRadius.Visibility = (BatteryAccentCombo.SelectedIndex == (int)TaskbarAccentMode.Blur) ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetStateColor(DynamicState state, Color newColor)
    {
        string key = state switch
        {
            DynamicState.Desktop => "Desktop",
            DynamicState.VisibleWindow => "Visible",
            DynamicState.MaximizedWindow => "Maximized",
            DynamicState.StartOpened => "Start",
            DynamicState.SearchOpened => "Search",
            DynamicState.TaskViewOpened => "TaskView",
            DynamicState.BatterySaver => "Battery",
            _ => "Desktop",
        };

        _currentColors[key] = newColor;
        UpdateColorButton(key, newColor);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is LanguageInfo lang)
        {
            _isLoading = true;
            try
            {
                _localizationService.SetLanguage(lang.Code);
                _configService.Config.Language = lang.Code;
                _configService.Save();
                ApplyLocalization();
            }
            finally
            {
                _isLoading = false;
            }
        }
    }

    private void OnDesktopColorClicked(object sender, RoutedEventArgs e)
    {
        _currentColors.TryGetValue("Desktop", out var color);
        ColorRequested?.Invoke(DynamicState.Desktop, color);
    }

    private void OnVisibleColorClicked(object sender, RoutedEventArgs e)
    {
        _currentColors.TryGetValue("Visible", out var color);
        ColorRequested?.Invoke(DynamicState.VisibleWindow, color);
    }

    private void OnMaximizedColorClicked(object sender, RoutedEventArgs e)
    {
        _currentColors.TryGetValue("Maximized", out var color);
        ColorRequested?.Invoke(DynamicState.MaximizedWindow, color);
    }

    private void OnStartColorClicked(object sender, RoutedEventArgs e)
    {
        _currentColors.TryGetValue("Start", out var color);
        ColorRequested?.Invoke(DynamicState.StartOpened, color);
    }

    private void OnSearchColorClicked(object sender, RoutedEventArgs e)
    {
        _currentColors.TryGetValue("Search", out var color);
        ColorRequested?.Invoke(DynamicState.SearchOpened, color);
    }

    private void OnTaskViewColorClicked(object sender, RoutedEventArgs e)
    {
        _currentColors.TryGetValue("TaskView", out var color);
        ColorRequested?.Invoke(DynamicState.TaskViewOpened, color);
    }

    private void OnBatteryColorClicked(object sender, RoutedEventArgs e)
    {
        _currentColors.TryGetValue("Battery", out var color);
        ColorRequested?.Invoke(DynamicState.BatterySaver, color);
    }

    private async void OnHelpClicked(object sender, RoutedEventArgs e)
    {
        bool isRu = _localizationService.CurrentLanguage == "ru-RU";
        string title = isRu ? "Справка по настройкам" : "Settings Help";
        string col1Header = isRu ? "Настройка" : "Setting";
        string col2Header = isRu ? "Что делает эта настройка" : "What this setting does";

        var items = isRu ? new (string Name, string Desc)[]
        {
            ("Панель задач", "Основной вид панели задач на рабочем столе (Обычный, Сплошной, Прозрачный, Размытие, Акрил)."),
            ("Цвет (кнопка 🎨)", "Выбор цвета и степени прозрачности (Alpha). В окне выбора цвета кнопка «Сбросить» делает цвет полностью прозрачным."),
            ("Линия панели задач", "Показывает или скрывает верхнюю разделительную линию над панелью задач."),
            ("Радиус размытия", "Сила размытия фона панели задач (настройка видна и действует в режиме «Размытие»)."),
            ("Видимое окно", "Включает особый вид панели задач, если на экране открыто любое окно приложения."),
            ("Развернутое окно", "Включает особый вид панели задач, когда активное окно развернуто на весь экран (Maximized)."),
            ("Меню «Пуск» открыто", "Включает особый вид панели задач, пока открыто меню «Пуск»."),
            ("Поиск открыт", "Включает особый вид панели задач во время работы поиска Windows."),
            ("Task View открыт", "Включает особый вид панели задач при открытии представления задач (виртуальных рабочих столов)."),
            ("Экономия заряда", "Включает особый вид панели при переходе Windows в режим энергосбережения."),
            ("Запускать при старте", "Автоматический запуск TranslucentTabBar при входе в Windows."),
            ("Отключить сохранение", "Запрещает запись изменений в файл settings.json (для временных тестов)."),
            ("Сбросить настройки", "Выключает все переключатели и возвращает оформление к исходному режиму («Обычный»).")
        } : new (string Name, string Desc)[]
        {
            ("Taskbar", "Main taskbar appearance on the desktop (Normal, Opaque, Clear, Blur, Acrylic)."),
            ("Color (🎨 button)", "Customize color tint and opacity (Alpha). The \"Reset\" button in the picker sets full transparency."),
            ("Taskbar line", "Shows or hides the accent separator line along the top of the taskbar."),
            ("Blur radius", "Adjusts the background blur radius (visible and active in \"Blur\" mode)."),
            ("Visible Window", "Applies an appearance when any regular application window is visible."),
            ("Maximized Window", "Applies an appearance when an active window is maximized."),
            ("Start Opened", "Applies an appearance while the Start Menu is open."),
            ("Search Opened", "Applies an appearance while the Windows Search window is open."),
            ("Task View Opened", "Applies an appearance while Task View is active."),
            ("Battery Saver", "Applies an appearance when Windows Battery Saver mode turns on."),
            ("Start with Windows", "Automatically launch TranslucentTabBar when signing in to Windows."),
            ("Disable saving", "Prevents writing changes to settings.json (for temporary testing)."),
            ("Reset to Defaults", "Turns off all toggles and resets all appearances to default (\"Normal\").")
        };

        var contentGrid = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var h1 = new TextBlock
        {
            Text = col1Header,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(6, 4, 6, 6)
        };
        var h2 = new TextBlock
        {
            Text = col2Header,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(6, 4, 6, 6)
        };
        Grid.SetColumn(h1, 0);
        Grid.SetRow(h1, 0);
        Grid.SetColumn(h2, 1);
        Grid.SetRow(h2, 0);
        contentGrid.Children.Add(h1);
        contentGrid.Children.Add(h2);

        int row = 1;
        foreach (var (name, desc) in items)
        {
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            if (row % 2 == 1)
            {
                var rowBackground = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(25, 128, 128, 128)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 1, 0, 1)
                };
                Grid.SetRow(rowBackground, row);
                Grid.SetColumnSpan(rowBackground, 2);
                contentGrid.Children.Add(rowBackground);
            }

            var tName = new TextBlock
            {
                Text = name,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 6, 6, 6),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tDesc = new TextBlock
            {
                Text = desc,
                Margin = new Thickness(6, 6, 6, 6),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(tName, row);
            Grid.SetColumn(tName, 0);
            Grid.SetRow(tDesc, row);
            Grid.SetColumn(tDesc, 1);

            contentGrid.Children.Add(tName);
            contentGrid.Children.Add(tDesc);

            row++;
        }

        var scrollViewer = new ScrollViewer
        {
            Content = contentGrid,
            MaxHeight = 440,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = scrollViewer,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private void OnResetDefaultsClicked(object sender, RoutedEventArgs e)
    {
        _configService.ResetToDefaults();
        LoadSettings();
        _stateResolver.Refresh();
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        AboutRequested?.Invoke();
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke();
    }
}