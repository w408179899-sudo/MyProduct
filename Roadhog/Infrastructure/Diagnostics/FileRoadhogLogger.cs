using System.Text;
using System.Text.Json;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Diagnostics;

public sealed class FileRoadhogLogger : IRoadhogLogger
{
    public const long DefaultMaxLogFileBytes = 1024L * 1024L;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _logDirectory;
    private readonly long _maxLogFileBytes;
    private readonly object _syncRoot = new();
    private string? _currentLogPath;
    private string? _currentDateStamp;
    private string? _latestSourceLogPath;

    public FileRoadhogLogger(string logDirectory, long maxLogFileBytes = DefaultMaxLogFileBytes)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : logDirectory;
        _maxLogFileBytes = Math.Max(1L, maxLogFileBytes);
    }

    public void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Append("info", eventName, fields, null);
    }

    public void Warn(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Append("warn", eventName, fields, null);
    }

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
        var merged = fields is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(fields);
        merged["exceptionType"] = exception.GetType().FullName;
        merged["exceptionMessage"] = exception.Message;
        merged["stackTrace"] = exception.StackTrace;
        Append("error", eventName, merged, exception.Message);
    }

    private void Append(
        string level,
        string eventName,
        IReadOnlyDictionary<string, object?>? fields,
        string? error)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var entry = new RoadhogLogFileEntry(
                now,
                level,
                eventName,
                NormalizeFields(fields),
                error);
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;

            lock (_syncRoot)
            {
                Directory.CreateDirectory(_logDirectory);
                var lineBytes = Utf8NoBom.GetByteCount(line);
                var logPath = ResolveCurrentLogPath(now, lineBytes);
                File.AppendAllText(logPath, line, Utf8NoBom);
                AppendLatest(logPath, line, lineBytes);
            }
        }
        catch
        {
            // Logging must never break the worker loop.
        }
    }

    private string ResolveCurrentLogPath(DateTimeOffset timestamp, int lineBytes)
    {
        var dateStamp = timestamp.ToString("yyyyMMdd");
        if (_currentLogPath is not null &&
            string.Equals(_currentDateStamp, dateStamp, StringComparison.Ordinal) &&
            CanAppend(_currentLogPath, lineBytes))
        {
            return _currentLogPath;
        }

        _currentDateStamp = dateStamp;
        _currentLogPath = SelectWritableLogPath(timestamp, dateStamp, lineBytes);
        return _currentLogPath;
    }

    private string SelectWritableLogPath(DateTimeOffset timestamp, string dateStamp, int lineBytes)
    {
        var dailyPath = Path.Combine(_logDirectory, "roadhog-" + dateStamp + ".log");
        if (CanAppend(dailyPath, lineBytes))
        {
            return dailyPath;
        }

        foreach (var path in EnumerateExistingSegmentPaths(dateStamp))
        {
            if (CanAppend(path, lineBytes))
            {
                return path;
            }
        }

        return CreateTimestampedLogPath(timestamp);
    }

    private IEnumerable<string> EnumerateExistingSegmentPaths(string dateStamp)
    {
        var pattern = "roadhog-" + dateStamp + "-*.log";
        return Directory.EnumerateFiles(_logDirectory, pattern)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
    }

    private string CreateTimestampedLogPath(DateTimeOffset timestamp)
    {
        var prefix = "roadhog-" + timestamp.ToString("yyyyMMdd-HHmmss-fff");
        for (var index = 0; index < 1000; index++)
        {
            var suffix = index == 0 ? string.Empty : "-" + index.ToString("000");
            var path = Path.Combine(_logDirectory, prefix + suffix + ".log");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(_logDirectory, prefix + "-" + Guid.NewGuid().ToString("N") + ".log");
    }

    private bool CanAppend(string path, int lineBytes)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        var length = new FileInfo(path).Length;
        return length == 0 || length + lineBytes <= _maxLogFileBytes;
    }

    private void AppendLatest(string sourceLogPath, string line, int lineBytes)
    {
        var latestPath = Path.Combine(_logDirectory, "latest.log");
        var resetLatest = !string.Equals(_latestSourceLogPath, sourceLogPath, StringComparison.OrdinalIgnoreCase) ||
                          !CanAppend(latestPath, lineBytes);
        if (resetLatest)
        {
            File.WriteAllText(latestPath, line, Utf8NoBom);
            _latestSourceLogPath = sourceLogPath;
            return;
        }

        File.AppendAllText(latestPath, line, Utf8NoBom);
    }

    private static IReadOnlyDictionary<string, object?> NormalizeFields(IReadOnlyDictionary<string, object?>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            safe[pair.Key] = NormalizeValue(pair.Value);
        }

        return safe;
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool boolean => boolean,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            DateTime dateTime => dateTime.ToString("O"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
            TimeSpan timeSpan => timeSpan.ToString(),
            _ => value.ToString()
        };
    }

    private sealed record RoadhogLogFileEntry(
        DateTimeOffset Timestamp,
        string Level,
        string EventName,
        IReadOnlyDictionary<string, object?> Fields,
        string? Error);
}
