using System.Text.Json;
using System.Text.Json.Serialization;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Profiles;

namespace Roadhog.Infrastructure.Profiles;

public sealed class JsonScriptProfileStore : IScriptProfileStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonScriptProfileStore(string directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory)
            ? throw new ArgumentException("Profile library directory cannot be empty.", nameof(directory))
            : directory;
    }

    public async Task<OperationResult<IReadOnlyList<ScriptProfileSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var summaries = new List<ScriptProfileSummary>();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                var document = await ReadDocumentAsync(file, cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    continue;
                }

                NormalizeDocument(document, Path.GetFileNameWithoutExtension(file));
                summaries.Add(new ScriptProfileSummary(document.Name, document.UpdatedAt));
            }

            return OperationResult<IReadOnlyList<ScriptProfileSummary>>.Ok(
                summaries
                    .OrderBy(summary => summary.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray());
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<ScriptProfileSummary>>.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult<ScriptProfileDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return OperationResult<ScriptProfileDocument>.Fail("Profile name cannot be empty.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var path = ResolvePath(normalizedName);
            if (!File.Exists(path))
            {
                return OperationResult<ScriptProfileDocument>.Fail("Profile file was not found: " + normalizedName);
            }

            var document = await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return OperationResult<ScriptProfileDocument>.Fail("Profile file is empty or invalid: " + normalizedName);
            }

            NormalizeDocument(document, normalizedName);
            return OperationResult<ScriptProfileDocument>.Ok(document.Clone());
        }
        catch (Exception ex)
        {
            return OperationResult<ScriptProfileDocument>.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult> SaveAsync(
        ScriptProfileDocument profile,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(profile.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return OperationResult.Fail("Profile name cannot be empty.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var filePath = ResolvePath(normalizedName);
            var document = profile.Clone();
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
            document.Settings.ProfileName = document.Name;
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
            return OperationResult.Fail("Profile name cannot be empty.");
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

    private async Task<ScriptProfileDocument?> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ScriptProfileDocument>(stream, _jsonOptions, cancellationToken)
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

    private static void NormalizeDocument(ScriptProfileDocument document, string fallbackName)
    {
        document.Version = document.Version <= 0 ? 1 : document.Version;
        document.Name = string.IsNullOrWhiteSpace(document.Name)
            ? fallbackName
            : document.Name.Trim();
        document.CreatedAt = document.CreatedAt == default ? DateTimeOffset.Now : document.CreatedAt;
        document.UpdatedAt = document.UpdatedAt == default ? document.CreatedAt : document.UpdatedAt;
        document.Settings = (document.Settings ?? new ScriptSettings()).Clone();
        document.Settings.ProfileName = document.Name;
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
        return string.IsNullOrWhiteSpace(safe) ? "profile" : safe;
    }
}
