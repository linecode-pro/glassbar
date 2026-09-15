using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace TranslucentTabBar;

/// <summary>
/// Hidden main window that serves as a message pump host for the tray application.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Hide the window programmatically
        var hwnd = WindowNative.GetWindowHandle(this);
        var style = NativeMethods.User32.GetWindowLongW(hwnd, NativeMethods.WindowStyles.GWL_EXSTYLE);
        _ = NativeMethods.User32.SetWindowLongW(hwnd,
            NativeMethods.WindowStyles.GWL_EXSTYLE,
            style | NativeMethods.WindowStyles.WS_EX_TOOLWINDOW);
    }
}
