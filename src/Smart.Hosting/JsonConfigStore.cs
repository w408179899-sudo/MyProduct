using System.Text.Json;
using System.Text.Json.Serialization;
namespace Smart.Hosting;

public sealed record ConfigDocument<T>(int SchemaVersion, T Settings);
public sealed class JsonConfigStore<T>(string path, int schemaVersion, Action<T> validate,
    IReadOnlyDictionary<int, Func<JsonElement, JsonElement>>? migrations = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public async Task<T> LoadAsync(CancellationToken ct = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using var document = await JsonDocument.ParseAsync(file, cancellationToken: ct).ConfigureAwait(false);
        foreach (var property in document.RootElement.EnumerateObject())
            if (property.Name is not ("SchemaVersion" or "Settings"))
                throw new JsonException("Unknown configuration document property: " + property.Name);
        var version = document.RootElement.GetProperty("SchemaVersion").GetInt32();
        var settings = document.RootElement.GetProperty("Settings").Clone();
        if (version > schemaVersion || version < 1) throw new InvalidDataException("Unsupported configuration schema.");
        while (version < schemaVersion)
        {
            if (migrations is null || !migrations.TryGetValue(version, out var migrate))
                throw new InvalidDataException("Missing configuration migration from version " + version);
            settings = migrate(settings); version++;
        }
        var result = settings.Deserialize<T>(Options) ?? throw new InvalidDataException("Empty configuration.");
        validate(result);
        return result;
    }
    public async Task SaveAsync(T settings, CancellationToken ct = default)
    {
        validate(settings);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var fullPath = Path.GetFullPath(path);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(file, new ConfigDocument<T>(schemaVersion, settings), Options, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }
}
