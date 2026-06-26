using System.Text;
using System.Text.Json;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Diagnostics;

public sealed class FileRoadhogLogger : IRoadhogLogger
{
    private readonly string _logDirectory;
    private readonly object _syncRoot = new();

    public FileRoadhogLogger(string logDirectory)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : logDirectory;
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
                File.AppendAllText(GetDailyLogPath(now), line, Encoding.UTF8);
                File.AppendAllText(Path.Combine(_logDirectory, "latest.log"), line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the worker loop.
        }
    }

    private string GetDailyLogPath(DateTimeOffset timestamp)
    {
        return Path.Combine(_logDirectory, "roadhog-" + timestamp.ToString("yyyyMMdd") + ".log");
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
