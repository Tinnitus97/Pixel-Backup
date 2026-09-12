using System.Text;

namespace PixelBackup.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message)
{
    public override string ToString() =>
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} [{Level.ToString().ToUpperInvariant()}] {Message}";
}

/// <summary>Zielsenke für Protokollmeldungen (UI, Datei, Tests …).</summary>
public interface ILogSink
{
    void Write(LogLevel level, string message);
}

public static class LogSinkExtensions
{
    public static void Debug(this ILogSink sink, string message) => sink.Write(LogLevel.Debug, message);

    public static void Info(this ILogSink sink, string message) => sink.Write(LogLevel.Info, message);

    public static void Warn(this ILogSink sink, string message) => sink.Write(LogLevel.Warning, message);

    public static void Error(this ILogSink sink, string message) => sink.Write(LogLevel.Error, message);

    public static void Error(this ILogSink sink, string message, Exception exception) =>
        sink.Write(LogLevel.Error, message + " – " + exception.Message);
}

public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();

    public void Write(LogLevel level, string message)
    {
    }
}

public sealed class DelegateLogSink : ILogSink
{
    private readonly Action<LogEntry> _callback;

    public DelegateLogSink(Action<LogEntry> callback) => _callback = callback;

    public void Write(LogLevel level, string message) =>
        _callback(new LogEntry(DateTimeOffset.Now, level, message));
}

public sealed class CompositeLogSink : ILogSink
{
    private readonly IReadOnlyList<ILogSink> _sinks;

    public CompositeLogSink(params ILogSink[] sinks) => _sinks = sinks;

    public void Write(LogLevel level, string message)
    {
        foreach (var sink in _sinks)
        {
            sink.Write(level, message);
        }
    }
}

/// <summary>Schreibt das Protokoll zeilenweise in eine Datei (eine Datei pro Tag).</summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private StreamWriter? _writer;
    private DateOnly _writerDate;

    public FileLogSink(string directory, LogLevel minimumLevel = LogLevel.Debug)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
    }

    public string Directory => _directory;

    public void Write(LogLevel level, string message)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        lock (_gate)
        {
            try
            {
                EnsureWriter(DateOnly.FromDateTime(entry.Timestamp.LocalDateTime));
                _writer?.WriteLine(entry.ToString());
            }
            catch (IOException)
            {
                // Protokollierung darf die Anwendung niemals zum Absturz bringen.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void EnsureWriter(DateOnly date)
    {
        if (_writer is not null && _writerDate == date)
        {
            return;
        }

        _writer?.Dispose();
        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"pixel-backup-{date:yyyy-MM-dd}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true
        };
        _writerDate = date;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
