using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Config;

internal sealed class JsonGlobalSettingsStore(string accountConfigPath)
{
    private const int ReloadIntervalMs = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly RoadhogGlobalSettings Defaults = new();

    // Keep the existing path and readCacheMs field compatible with older workers.
    private readonly string _path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(accountConfigPath))!, "dma-read-cache.json");
    private readonly object _reloadSync = new();
    private long _nextReloadAt = long.MinValue;
    private RoadhogGlobalSettings _current = Defaults;
    private string? _lastLoadError;

    public RoadhogGlobalSettings Current => Volatile.Read(ref _current);

    public RoadhogGlobalSettings Load()
    {
        if (!AtomicJsonFile.Exists(_path)) return Defaults;
        using var file = AtomicJsonFile.OpenRead(_path);
        var settings = JsonSerializer.Deserialize<RoadhogGlobalSettings>(file, Json)
            ?? throw new InvalidDataException("全局设置文件为空。");
        Validate(settings);
        return settings;
    }

    public async Task SaveAsync(RoadhogGlobalSettings settings, CancellationToken token)
    {
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var gate = await AtomicJsonFile.AcquireWriteLockAsync(_path, token).ConfigureAwait(false);
        await AtomicJsonFile.WriteAsync(_path, settings, Json, token).ConfigureAwait(false);
        lock (_reloadSync)
        {
            Volatile.Write(ref _current, settings);
            _lastLoadError = null;
        }
    }

    public void Refresh(IRoadhogLogger logger)
    {
        var now = Environment.TickCount64;
        if (now < Volatile.Read(ref _nextReloadAt) || !Monitor.TryEnter(_reloadSync)) return;
        try
        {
            if (now < _nextReloadAt) return;
            Volatile.Write(ref _nextReloadAt, now + ReloadIntervalMs);
            if (ReferenceEquals(Current, Defaults) && _lastLoadError is null && !File.Exists(_path)) return;
            Volatile.Write(ref _current, Load());
            _lastLoadError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            if (_lastLoadError == ex.Message) return;
            _lastLoadError = ex.Message;
            logger.Warn("global_settings.load_failed", new Dictionary<string, object?>
            {
                ["path"] = _path,
                ["error"] = ex.Message,
                ["heldReadCacheMs"] = Current.ReadCacheMs
            });
        }
        finally { Monitor.Exit(_reloadSync); }
    }

    private static void Validate(RoadhogGlobalSettings settings)
    {
        if (settings.Validate() is { } error) throw new InvalidDataException(error);
    }
}
