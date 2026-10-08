using Microsoft.Extensions.Logging;

namespace AlbionSaveEditor.Desktop;

/// <summary>
/// Minimal logger that appends to %LOCALAPPDATA%\AlbionSaveEditor\editor.log (a WinExe has no console).
/// The file is truncated on every start.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object sync = new();
    private readonly StreamWriter? writer;

    public string Path { get; }

    public FileLoggerProvider(string path)
    {
        Path = path;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            writer = null; // logging is best effort
        }
    }

    public void Write(string category, LogLevel level, string message)
    {
        if (writer == null) return;
        lock (sync)
            writer.WriteLine($"{DateTime.Now:HH:mm:ss} {level,-11} {category}: {message}");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (sync) writer?.Dispose();
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        // per-request ASP.NET Core logging is noise for a desktop app
        public bool IsEnabled(LogLevel logLevel) => logLevel >= (category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ? LogLevel.Warning : LogLevel.Information);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string msg = formatter(state, exception);
            if (exception != null) msg += Environment.NewLine + exception;
            provider.Write(category, logLevel, msg);
        }
    }
}
