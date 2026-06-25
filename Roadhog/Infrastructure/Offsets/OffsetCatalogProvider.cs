using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Offsets;

public sealed class OffsetCatalogProvider
{
    private readonly OffsetCatalogLoader _loader;
    private readonly IRoadhogLogger _logger;
    private OffsetCatalog? _catalog;

    public OffsetCatalogProvider(OffsetCatalogLoader loader, IRoadhogLogger logger)
    {
        _loader = loader;
        _logger = logger;
    }

    public OffsetCatalog? Current => _catalog;

    public async Task<OffsetCatalog> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        _catalog = await _loader.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        _logger.Info("offsets.loaded", new Dictionary<string, object?>
        {
            ["path"] = path,
            ["count"] = _catalog.Offsets.Count,
            ["version"] = _catalog.Version
        });
        return _catalog;
    }
}
