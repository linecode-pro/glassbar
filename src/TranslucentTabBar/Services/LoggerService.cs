namespace TranslucentTabBar.Services;

/// <summary>
/// Simple file-based logger for the application.
/// </summary>
public class LoggerService : IDisposable
{
    private readonly string _logPath = string.Empty;
    private readonly StreamWriter _writer = null!;
    private readonly object _lock = new();
    private bool _disposed;

    public LoggerService()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TranslucentTabBar");
            Directory.CreateDirectory(logDir);

            _logPath = Path.Combine(logDir, $"log-{DateTime.Now:yyyy-MM-dd}.txt");
            _writer = new StreamWriter(_logPath, append: true) { AutoFlush = true };

            Log(LogLevel.Info, "=== TranslucentTabBar started ===");
        }
        catch
        {
            // Logging infrastructure failure is non-fatal
            _writer = null!;
        }
    }

    public void Log(LogLevel level, string message)
    {
        if (_writer == null) return;

        lock (_lock)
        {
            try
            {
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
            }
            catch { }
        }
    }

    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warn, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);

    public void OpenLogFile()
    {
        try
        {
            if (_logPath != null && File.Exists(_logPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _logPath,
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            try
            {
                _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [Info] === TranslucentTabBar shutting down ===");
                _writer?.Dispose();
            }
            catch { }
        }
    }
}

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}
