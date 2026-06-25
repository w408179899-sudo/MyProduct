using System.Text.Json;
using System.Text.Json.Serialization;

namespace Roadhog.Infrastructure.Offsets;

public sealed class OffsetCatalogLoader
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<OffsetCatalog> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<OffsetCatalog>(stream, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (catalog is null)
        {
            throw new InvalidDataException("Offset catalog is empty: " + path);
        }

        var errors = catalog.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException("Offset catalog failed validation: " + string.Join("; ", errors));
        }

        return catalog;
    }

    public async Task SaveAsync(string path, OffsetCatalog catalog, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, catalog, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
