using System.Runtime.InteropServices;
using TranslucentTabBar.NativeMethods;

namespace TranslucentTabBar.Services;

/// <summary>
/// Service that uses the IAppVisibility COM interface to detect Start Menu visibility.
/// More reliable than checking window class names.
/// </summary>
public class AppVisibilityService : IDisposable
{
    private IAppVisibility? _appVisibility;
    private IAppVisibilityEvents? _eventSink;
    private int _cookie;
    private GCHandle _sinkHandle;
    private bool _disposed;

    /// <summary>
    /// Raised when the Start Menu / launcher visibility changes.
    /// </summary>
    public event Action<bool>? LauncherVisibilityChanged;

    /// <summary>
    /// Gets whether the Start Menu is currently visible.
    /// </summary>
    public bool IsLauncherVisible { get; private set; }

    public AppVisibilityService()
    {
        try
        {
            // Create the IAppVisibility COM object
            var type = Type.GetTypeFromCLSID(AppVisibilityClsid.CLSID_AppVisibility);
            if (type != null)
            {
                _appVisibility = (IAppVisibility)Activator.CreateInstance(type)!;

                // Check initial state
                _appVisibility.IsLauncherVisible(out bool visible);
                IsLauncherVisible = visible;

                // Subscribe to events
                _eventSink = new AppVisibilityEventSink(this);
                _sinkHandle = GCHandle.Alloc(_eventSink);
                _appVisibility.Advise(_eventSink, out _cookie);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create IAppVisibility: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes the launcher visibility state manually.
    /// </summary>
    public void Refresh()
    {
        try
        {
            bool visible = false;
            _appVisibility?.IsLauncherVisible(out visible);
            IsLauncherVisible = visible;
        }
        catch { }
    }

    private void OnLauncherVisibilityChanged(bool visible)
    {
        IsLauncherVisible = visible;
        LauncherVisibilityChanged?.Invoke(visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_appVisibility != null && _cookie != 0)
            {
                _appVisibility.Unadvise(_cookie);
            }
        }
        catch { }

        if (_sinkHandle.IsAllocated)
            _sinkHandle.Free();
    }

    /// <summary>
    /// Internal event sink implementation.
    /// </summary>
    private class AppVisibilityEventSink : IAppVisibilityEvents
    {
        private readonly AppVisibilityService _service;

        public AppVisibilityEventSink(AppVisibilityService service)
        {
            _service = service;
        }

        public void AppVisibilityOnMonitorChanged(nint hMonitor, MONITOR_APP_VISIBILITY previousMode, MONITOR_APP_VISIBILITY currentMode)
        {
            // Visibility on monitor changed - refresh
            _service.Refresh();
        }

        public void LauncherVisibilityChange(bool visible)
        {
            _service.OnLauncherVisibilityChanged(visible);
        }
    }
}
