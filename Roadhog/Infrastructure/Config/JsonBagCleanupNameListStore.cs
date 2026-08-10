using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Config;

public sealed class JsonBagCleanupNameListStore : IBagCleanupNameListStore
{
    public const string DefaultFileName = "bag-cleanup-name-lists.json";
    public const string LegacyFileName = "bag-cleanup-excluded.txt";

    private readonly string _legacyPath;
    private readonly IRoadhogLogger _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public JsonBagCleanupNameListStore(
        string path,
        string? legacyPath = null,
        IRoadhogLogger? logger = null)
    {
        FilePath = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Bag cleanup name-list path cannot be empty.", nameof(path))
            : path;
        _legacyPath = string.IsNullOrWhiteSpace(legacyPath)
            ? Path.Combine(Path.GetDirectoryName(FilePath) ?? AppContext.BaseDirectory, LegacyFileName)
            : legacyPath;
        _logger = logger ?? NoOpRoadhogLogger.Instance;
    }

    public string FilePath { get; }

    public async Task<OperationResult<BagCleanupNameListsLoadResult>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(FilePath))
            {
                await using var stream = File.OpenRead(FilePath);
                var fileDocument = await JsonSerializer
                    .DeserializeAsync<FileDocument>(stream, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                var validationError = Validate(fileDocument);
                if (validationError is not null)
                {
                    _logger.Warn("bag_cleanup.name_lists.load_failed", new Dictionary<string, object?>
                    {
                        ["path"] = FilePath,
                        ["error"] = validationError
                    });
                    return OperationResult<BagCleanupNameListsLoadResult>.Fail(validationError);
                }

                var document = new BagCleanupNameListsDocument
                {
                    Whitelist = BagCleanupNameListsDocument.NormalizeKeywords(fileDocument!.Whitelist),
                    Blacklist = BagCleanupNameListsDocument.NormalizeKeywords(fileDocument.Blacklist)
                };
                LogLoaded(document, BagCleanupNameListsSource.Json, FilePath);
                return OperationResult<BagCleanupNameListsLoadResult>.Ok(
                    new BagCleanupNameListsLoadResult(document, BagCleanupNameListsSource.Json));
            }

            if (File.Exists(_legacyPath))
            {
                var lines = await File.ReadAllLinesAsync(_legacyPath, cancellationToken).ConfigureAwait(false);
                var document = new BagCleanupNameListsDocument
                {
                    Whitelist = BagCleanupNameListsDocument.NormalizeKeywords(lines)
                };
                LogLoaded(document, BagCleanupNameListsSource.LegacyText, _legacyPath);
                return OperationResult<BagCleanupNameListsLoadResult>.Ok(
                    new BagCleanupNameListsLoadResult(document, BagCleanupNameListsSource.LegacyText));
            }

            return OperationResult<BagCleanupNameListsLoadResult>.Ok(
                new BagCleanupNameListsLoadResult(null, BagCleanupNameListsSource.None));
        }
        catch (Exception ex)
        {
            _logger.Error("bag_cleanup.name_lists.load_failed", ex, new Dictionary<string, object?>
            {
                ["path"] = FilePath
            });
            return OperationResult<BagCleanupNameListsLoadResult>.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult> SaveAsync(
        BagCleanupNameListsDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var normalized = document.Clone();
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var fileDocument = new FileDocument
            {
                Version = BagCleanupNameListsDocument.CurrentVersion,
                Whitelist = normalized.Whitelist,
                Blacklist = normalized.Blacklist
            };
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer
                    .SerializeAsync(stream, fileDocument, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
            temporaryPath = null;
            _logger.Info("bag_cleanup.name_lists.saved", new Dictionary<string, object?>
            {
                ["path"] = FilePath,
                ["whitelistCount"] = normalized.Whitelist.Count,
                ["blacklistCount"] = normalized.Blacklist.Count
            });
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error("bag_cleanup.name_lists.save_failed", ex, new Dictionary<string, object?>
            {
                ["path"] = FilePath
            });
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup; the original save error is more useful to the caller.
                }
            }

            _sync.Release();
        }
    }

    private static string? Validate(FileDocument? document)
    {
        if (document is null)
        {
            return "Bag cleanup name-list file is empty or invalid.";
        }

        if (document.Version != BagCleanupNameListsDocument.CurrentVersion)
        {
            return "Unsupported bag cleanup name-list version: " + document.Version;
        }

        if (document.Whitelist is null || document.Blacklist is null)
        {
            return "Bag cleanup name-list file must contain whitelist and blacklist arrays.";
        }

        return null;
    }

    private void LogLoaded(
        BagCleanupNameListsDocument document,
        BagCleanupNameListsSource source,
        string path)
    {
        _logger.Info("bag_cleanup.name_lists.loaded", new Dictionary<string, object?>
        {
            ["path"] = path,
            ["source"] = source.ToString(),
            ["whitelistCount"] = document.Whitelist.Count,
            ["blacklistCount"] = document.Blacklist.Count
        });
    }

    private sealed class FileDocument
    {
        public int Version { get; set; }

        public List<string>? Whitelist { get; set; }

        public List<string>? Blacklist { get; set; }
    }
}
