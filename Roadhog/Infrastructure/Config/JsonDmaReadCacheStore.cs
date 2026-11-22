using System.Text.Json;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Config;

internal sealed class JsonDmaReadCacheStore(string accountConfigPath)
{
    public const int TickPeriodMs = 50;
    public const int DefaultPeriodMs = 150;
    private const int ReloadIntervalMs = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(accountConfigPath))!, "dma-read-cache.json");
    private readonly object _reloadSync = new();
    private long _nextReloadAt = long.MinValue;
    private int _currentPeriodMs = DefaultPeriodMs;
    private string? _lastLoadError;

    public int CurrentPeriodMs => Volatile.Read(ref _currentPeriodMs);

    public static bool IsValidPeriod(int periodMs) => periodMs > 0 && periodMs % TickPeriodMs == 0;

    public int Load()
    {
        if (!AtomicJsonFile.Exists(_path)) return DefaultPeriodMs;

        using var file = AtomicJsonFile.OpenRead(_path);
        var settings = JsonSerializer.Deserialize<Settings>(file, Json);
        if (settings is null || !IsValidPeriod(settings.ReadCacheMs))
            throw new InvalidDataException($"DMA 读缓存必须填写 {TickPeriodMs} ms 的正整数倍。");
        return settings.ReadCacheMs;
    }

    public async Task SaveAsync(int periodMs, CancellationToken token)
    {
        if (!IsValidPeriod(periodMs)) throw new ArgumentOutOfRangeException(nameof(periodMs));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var gate = await AtomicJsonFile.AcquireWriteLockAsync(_path, token).ConfigureAwait(false);
        await AtomicJsonFile.WriteAsync(_path, new Settings(periodMs), Json, token).ConfigureAwait(false);
    }

    public void Refresh(IRoadhogLogger logger)
    {
        var now = Environment.TickCount64;
        if (now < Volatile.Read(ref _nextReloadAt) || !Monitor.TryEnter(_reloadSync)) return;
        try
        {
            if (now < _nextReloadAt) return;
            Volatile.Write(ref _nextReloadAt, now + ReloadIntervalMs);
            if (CurrentPeriodMs == DefaultPeriodMs && !File.Exists(_path)) return;
            Volatile.Write(ref _currentPeriodMs, Load());
            _lastLoadError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            if (_lastLoadError == ex.Message) return;
            _lastLoadError = ex.Message;
            logger.Warn("vmm.read_cache.setting_load_failed", new Dictionary<string, object?>
            {
                ["path"] = _path,
                ["error"] = ex.Message,
                ["heldReadCacheMs"] = CurrentPeriodMs
            });
        }
        finally { Monitor.Exit(_reloadSync); }
    }

    private sealed record Settings(int ReadCacheMs);
}
