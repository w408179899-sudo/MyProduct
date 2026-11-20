using System.Text.Json;
using System.Text.Json.Nodes;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Paths;
using Roadhog.Infrastructure.Config;

namespace Roadhog.Infrastructure.Paths;

public sealed class JsonSharedPathStore : ISharedPathStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public JsonSharedPathStore(string directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory)
            ? throw new ArgumentException("Path library directory cannot be empty.", nameof(directory))
            : directory;
    }

    public async Task<OperationResult<IReadOnlyList<SharedPathSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var summaries = new List<SharedPathSummary>();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                var document = await ReadDocumentAsync(file, cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    continue;
                }

                NormalizeDocument(document, Path.GetFileNameWithoutExtension(file));
                summaries.Add(new SharedPathSummary(
                    document.Name,
                    document.PointCount,
                    document.TotalDistance,
                    document.UpdatedAt));
            }

            return OperationResult<IReadOnlyList<SharedPathSummary>>.Ok(
                summaries
                    .OrderBy(summary => summary.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray());
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<SharedPathSummary>>.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult<SharedPathDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return OperationResult<SharedPathDocument>.Fail("Path name cannot be empty.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var path = ResolvePath(normalizedName);
            if (!AtomicJsonFile.Exists(path))
            {
                return OperationResult<SharedPathDocument>.Fail("Path file was not found: " + normalizedName);
            }

            var document = await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return OperationResult<SharedPathDocument>.Fail("Path file is empty or invalid: " + normalizedName);
            }

            NormalizeDocument(document, normalizedName);
            return OperationResult<SharedPathDocument>.Ok(document.Clone());
        }
        catch (Exception ex)
        {
            return OperationResult<SharedPathDocument>.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult> SaveAsync(
        SharedPathDocument path,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(path.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return OperationResult.Fail("Path name cannot be empty.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var filePath = ResolvePath(normalizedName);
            await using var writeLock = await AtomicJsonFile.AcquireWriteLockAsync(filePath, cancellationToken).ConfigureAwait(false);
            var document = path.Clone();
            NormalizeDocument(document, normalizedName);

            if (File.Exists(filePath))
            {
                var existing = await ReadDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.CreatedAt != default) document.CreatedAt = existing.CreatedAt;
                    // Existing priority metadata is owned by the priority editor, including clears.
                    document.MonsterKillPriorities = existing.MonsterKillPriorities;
                }
            }

            document.UpdatedAt = DateTimeOffset.Now;
            await AtomicJsonFile.WriteAsync(filePath, document, _jsonOptions, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult> SaveMonsterKillPrioritiesAsync(
        string name, IReadOnlyList<string> before, IReadOnlyList<string> after,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        if (normalizedName.Length == 0) return OperationResult.Fail("请先选择已保存的复活路径。");

        try
        {
            EnsureDirectory();
            var filePath = ResolvePath(normalizedName);
            await using var writeLock = await AtomicJsonFile.AcquireWriteLockAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(filePath)) return OperationResult.Fail("复活路径未保存或已删除。");
            JsonObject? document;
            await using (var stream = AtomicJsonFile.OpenRead(filePath))
                document = await JsonSerializer.DeserializeAsync<JsonObject>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            if (document is null) return OperationResult.Fail("复活路径文件无法读取。");
            var current = BagCleanupNameListsDocument.NormalizeKeywords(document.Deserialize<PathPriorities>(_jsonOptions)?.MonsterKillPriorities);
            if (!current.SequenceEqual(before, StringComparer.OrdinalIgnoreCase))
                return OperationResult.Fail("击杀优先级已被修改，请核对当前顺序后重试。");

            var next = BagCleanupNameListsDocument.NormalizeKeywords(after);
            if (current.SequenceEqual(next, StringComparer.OrdinalIgnoreCase)) return OperationResult.Ok();
            SetPathMetadata(document, nameof(SharedPathDocument.MonsterKillPriorities),
                next.Count == 0 ? null : JsonSerializer.SerializeToNode(next));
            SetPathMetadata(document, nameof(SharedPathDocument.UpdatedAt), JsonValue.Create(DateTimeOffset.Now));
            await AtomicJsonFile.WriteAsync(filePath, document, _jsonOptions, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<string>>> LoadMonsterKillPrioritiesAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult<List<string>>.Fail("请先选择已保存的复活路径。");
        var filePath = ResolvePath(name);
        try
        {
            if (!AtomicJsonFile.Exists(filePath)) return OperationResult<List<string>>.Fail("复活路径未保存或已删除。");
            await using var stream = AtomicJsonFile.OpenRead(filePath);
            var data = await JsonSerializer.DeserializeAsync<PathPriorities>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            return data is null
                ? OperationResult<List<string>>.Fail("复活路径文件无法读取。")
                : OperationResult<List<string>>.Ok(BagCleanupNameListsDocument.NormalizeKeywords(data.MonsterKillPriorities));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OperationResult<List<string>>.Fail(ex.Message);
        }
    }

    private static void SetPathMetadata(JsonObject document, string name, JsonNode? value)
    {
        var key = document.Select(entry => entry.Key).FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        if (value is null) document.Remove(key);
        else document[key] = value;
    }

    private sealed record PathPriorities(List<string>? MonsterKillPriorities);

    public async Task<OperationResult> DeleteAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return OperationResult.Fail("Path name cannot be empty.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var path = ResolvePath(normalizedName);
            await using var writeLock = await AtomicJsonFile.AcquireWriteLockAsync(path, cancellationToken).ConfigureAwait(false);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<SharedPathDocument?> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = AtomicJsonFile.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SharedPathDocument>(stream, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_directory);
    }

    private string ResolvePath(string name)
    {
        return Path.Combine(_directory, ToSafeFileName(name) + ".json");
    }

    private static void NormalizeDocument(SharedPathDocument document, string fallbackName)
    {
        document.Version = document.Version <= 0 ? 1 : document.Version;
        document.Name = string.IsNullOrWhiteSpace(document.Name)
            ? fallbackName
            : document.Name.Trim();
        document.CreatedAt = document.CreatedAt == default ? DateTimeOffset.Now : document.CreatedAt;
        document.UpdatedAt = document.UpdatedAt == default ? document.CreatedAt : document.UpdatedAt;
        document.CleanupNpcName = document.CleanupNpcName?.Trim() ?? string.Empty;
        document.AuctionNpcName = document.AuctionNpcName?.Trim() ?? string.Empty;
        document.Points ??= new List<SharedPathPoint>();

        var buffer = new PathRecordingBuffer();
        buffer.Load(document.Points);
        document.Points = buffer.Points.Select(point => point.Clone()).ToList();
    }

    private static string NormalizeName(string? name)
    {
        return name?.Trim() ?? string.Empty;
    }

    private static string ToSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = NormalizeName(name)
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        var safe = new string(chars).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "path" : safe;
    }
}
