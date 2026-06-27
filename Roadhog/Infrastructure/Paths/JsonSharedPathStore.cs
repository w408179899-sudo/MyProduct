using System.Text.Json;
using Roadhog.Core.Common;
using Roadhog.Core.Paths;

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
            if (!File.Exists(path))
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
            var document = path.Clone();
            NormalizeDocument(document, normalizedName);

            if (File.Exists(filePath))
            {
                var existing = await ReadDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (existing is not null && existing.CreatedAt != default)
                {
                    document.CreatedAt = existing.CreatedAt;
                }
            }

            document.UpdatedAt = DateTimeOffset.Now;
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, document, _jsonOptions, cancellationToken).ConfigureAwait(false);
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
        await using var stream = File.OpenRead(path);
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
