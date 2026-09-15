using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace TranslucentTabBar.ViewModels;

/// <summary>
/// ViewModel for the color picker page.
/// </summary>
public class ColorPickerViewModel : INotifyPropertyChanged
{
    private Color _originalColor;
    private Color _currentColor;
    private bool _alwaysOnTop;

    /// <summary>
    /// Raised when the user confirms a color selection.
    /// </summary>
    public event Action<Color>? ColorConfirmed;

    /// <summary>
    /// Raised when the color changes in real-time (for preview).
    /// </summary>
    public event Action<Color>? ColorPreviewChanged;

    public Color OriginalColor
    {
        get => _originalColor;
        set { _originalColor = value; OnPropertyChanged(); }
    }

    public Color CurrentColor
    {
        get => _currentColor;
        set
        {
            if (_currentColor != value)
            {
                _currentColor = value;
                OnPropertyChanged();
                ColorPreviewChanged?.Invoke(value);
            }
        }
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set { _alwaysOnTop = value; OnPropertyChanged(); }
    }

    public void ConfirmColor()
    {
        ColorConfirmed?.Invoke(_currentColor);
    }

    public void CancelColor()
    {
        // Restore original
        CurrentColor = OriginalColor;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
