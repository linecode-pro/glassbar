using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TranslucentTabBar.Services;
using TranslucentTabBar.ViewModels;
using Windows.UI;

namespace TranslucentTabBar.Views;

public sealed partial class ColorPickerPage : Page
{
    private readonly ColorPickerViewModel _viewModel;
    private readonly LocalizationService? _localizationService;
    private Color _originalColor;

    /// <summary>
    /// Raised when the user confirms a color selection.
    /// </summary>
    public event Action<Color>? ColorConfirmed;

    /// <summary>
    /// Raised when the color changes in real-time (for live preview).
    /// </summary>
    public event Action<Color>? ColorPreviewChanged;

    /// <summary>
    /// Raised when the page is closed without confirming.
    /// </summary>
    public event Action? Closed;

    public ColorPickerPage(Color initialColor, LocalizationService? localizationService = null)
    {
        this.InitializeComponent();

        _localizationService = localizationService;
        _originalColor = initialColor;
        _viewModel = new ColorPickerViewModel
        {
            OriginalColor = initialColor,
            CurrentColor = initialColor,
        };

        Picker.Color = initialColor;
        _viewModel.ColorPreviewChanged += OnColorPreviewChanged;

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        if (_localizationService == null) return;

        HeaderText.Text = _localizationService.GetString("ColorPicker_Header.Text");
        DescriptionText.Text = _localizationService.GetString("ColorPicker_Description.Text");
        OkButton.Content = _localizationService.GetString("ColorPicker_Ok.Content");
        ResetButton.Content = _localizationService.GetString("ColorPicker_Reset.Content");
        CancelButton.Content = _localizationService.GetString("ColorPicker_Cancel.Content");
    }

    /// <summary>
    /// Gets or sets whether the window should stay on top.
    /// </summary>
    public bool AlwaysOnTop
    {
        get => _viewModel.AlwaysOnTop;
        set => _viewModel.AlwaysOnTop = value;
    }

    public Color OriginalColor => _originalColor;

    public Color CurrentColor => _viewModel.CurrentColor;

    private void OnColorPreviewChanged(Color color)
    {
        ColorPreviewChanged?.Invoke(color);
    }

    private void OnColorChanged(object sender, ColorChangedEventArgs e)
    {
        _viewModel.CurrentColor = e.NewColor;
        ColorPreviewChanged?.Invoke(e.NewColor);
    }

    private void OnOkButtonClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentColor = Picker.Color;
        _viewModel.ConfirmColor();
        ColorConfirmed?.Invoke(Picker.Color);
    }

    private void OnResetButtonClicked(object sender, RoutedEventArgs e)
    {
        var transparent = Color.FromArgb(0, 0, 0, 0);
        Picker.Color = transparent;
        _viewModel.CurrentColor = transparent;
        ColorPreviewChanged?.Invoke(transparent);
    }

    private void OnCancelButtonClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelColor();
        ColorPreviewChanged?.Invoke(_originalColor);
        Closed?.Invoke();
    }
}
