using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Diagnostics;

public sealed class FileRoadhogLogger : IRoadhogLogger, IFileLoggingControl
{
    public const long DefaultMaxLogFileBytes = 1024L * 1024L;
    public const long DefaultMaxDirectoryBytes = 64L * 1024L * 1024L;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    private static readonly TimeSpan LogRetention = TimeSpan.FromDays(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private readonly string _logDirectory;
    private readonly string _latestPath;
    private readonly long _maxLogFileBytes;
    private readonly long _maxDirectoryBytes;
    private readonly TimeProvider _timeProvider;
    private readonly FileLogCompactor _compactor = new();
    private readonly object _syncRoot = new();
    // Worker command and runtime loggers can share a directory. Serialize append,
    // rename and cleanup across both instances and overlapping worker processes.
    private readonly Mutex _directoryMutex;
    private DateTimeOffset? _lastCleanupAt;
    private bool _enabled;

    public FileRoadhogLogger(string logDirectory, long maxLogFileBytes = DefaultMaxLogFileBytes, bool enabled = true,
        long maxDirectoryBytes = DefaultMaxDirectoryBytes, TimeProvider? timeProvider = null)
    {
        _logDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs") : logDirectory));
        _latestPath = Path.Combine(_logDirectory, "latest.log");
        _maxLogFileBytes = Math.Max(1L, maxLogFileBytes);
        _maxDirectoryBytes = Math.Max(_maxLogFileBytes, maxDirectoryBytes);
        _timeProvider = timeProvider ?? TimeProvider.System;
        var directoryKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_logDirectory.ToUpperInvariant())));
        _directoryMutex = new Mutex(false, "Local\\RoadhogLog-" + directoryKey);
        _enabled = enabled;
    }

    public void SetFileLoggingEnabled(bool enabled)
    {
        lock (_syncRoot)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            _compactor.Clear();
        }
    }

    public void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null) => Append("info", eventName, fields, null);
    public void Warn(string eventName, IReadOnlyDictionary<string, object?>? fields = null) => Append("warn", eventName, fields, null);

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
        var merged = fields is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(fields);
        merged["exceptionType"] = exception.GetType().FullName;
        merged["exceptionMessage"] = exception.Message;
        merged["stackTrace"] = exception.StackTrace;
        Append("error", eventName, merged, exception.Message);
    }

    private void Append(string level, string eventName, IReadOnlyDictionary<string, object?>? fields, string? error)
    {
        try
        {
            lock (_syncRoot)
            {
                if (!_enabled) return;
                var now = _timeProvider.GetLocalNow();
                var entries = _compactor.Accept(new(now, level, eventName, NormalizeFields(fields), error));
                if (entries.Count == 0) return;
                var acquired = false;
                try
                {
                    try { acquired = _directoryMutex.WaitOne(TimeSpan.FromSeconds(1)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) return; // A blocked logger must not hang account control.
                    Directory.CreateDirectory(_logDirectory);
                    var rotated = false;
                    foreach (var entry in entries)
                    {
                        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
                        rotated |= RotateIfNeeded(now, Utf8NoBom.GetByteCount(line));
                        File.AppendAllText(_latestPath, line, Utf8NoBom);
                    }
                    Cleanup(now, rotated);
                }
                finally { if (acquired) _directoryMutex.ReleaseMutex(); }
            }
        }
        catch
        {
            // Logging must never break the worker loop.
        }
    }

    private bool RotateIfNeeded(DateTimeOffset now, int lineBytes)
    {
        var latest = new FileInfo(_latestPath);
        if (!latest.Exists || latest.Length == 0) return false;
        if (latest.Length + lineBytes <= _maxLogFileBytes && latest.LastWriteTime.Date == now.Date) return false;
        var prefix = "roadhog-" + now.ToString("yyyyMMdd-HHmmss-fff");
        var archive = Path.Combine(_logDirectory, prefix + ".log");
        for (var index = 1; File.Exists(archive); index++)
            archive = Path.Combine(_logDirectory, prefix + "-" + index.ToString("000") + ".log");
        File.Move(_latestPath, archive);
        return true;
    }

    private void Cleanup(DateTimeOffset now, bool rotated)
    {
        if (!rotated && _lastCleanupAt is { } last && now >= last && now - last < CleanupInterval) return;
        _lastCleanupAt = now;
        var archives = new DirectoryInfo(_logDirectory).EnumerateFiles("roadhog-*.log")
            .OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToArray();
        // Reserve a complete active segment, so appends between cleanups cannot
        // grow an ordinary directory beyond the cap. Never delete latest.log or
        // unrelated files. A single oversized exception is retained intact.
        var reserved = Math.Max(_maxLogFileBytes, new FileInfo(_latestPath).Length);
        var total = archives.Sum(file => file.Length) + reserved;
        foreach (var file in archives)
        {
            if (file.LastWriteTimeUtc >= now.UtcDateTime - LogRetention && total <= _maxDirectoryBytes) continue;
            try { var bytes = file.Length; file.Delete(); total -= bytes; }
            catch { /* Retention is best-effort; locked files can be retried next cleanup. */ }
        }
    }

    private static IReadOnlyDictionary<string, object?> NormalizeFields(IReadOnlyDictionary<string, object?>? fields)
    {
        var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (fields is not null)
            foreach (var pair in fields) safe[pair.Key] = NormalizeValue(pair.Value);
        return safe;
    }

    private static object? NormalizeValue(object? value) => value switch
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
