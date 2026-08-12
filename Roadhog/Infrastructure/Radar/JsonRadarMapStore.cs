using System.Globalization;
using System.Text.Json;
using Roadhog.Core.Common;
using Roadhog.Core.Radar;

namespace Roadhog.Infrastructure.Radar;

public sealed class JsonRadarMapStore : IRadarMapStore
{
    private const double MinimumSegmentLengthMeters = 0.05D;
    private const int MaximumSegmentCount = 5000;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public JsonRadarMapStore(string directoryPath)
    {
        DirectoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? throw new ArgumentException("Radar map directory cannot be empty.", nameof(directoryPath))
            : Path.GetFullPath(directoryPath);
    }

    public string DirectoryPath { get; }

    public async Task<OperationResult<RadarMapLoadResult>> LoadAsync(
        uint mapId,
        CancellationToken cancellationToken = default)
    {
        if (mapId == 0)
        {
            return OperationResult<RadarMapLoadResult>.Fail("MapId must be greater than zero.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = ResolvePath(mapId);
            if (!File.Exists(path))
            {
                return OperationResult<RadarMapLoadResult>.Ok(
                    new RadarMapLoadResult(false, CreateEmpty(mapId)));
            }

            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer
                .DeserializeAsync<RadarMapDocument>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                return OperationResult<RadarMapLoadResult>.Fail("Radar map file is empty: " + path);
            }

            var normalized = NormalizeAndValidate(document, mapId);
            return normalized.Success && normalized.Value is not null
                ? OperationResult<RadarMapLoadResult>.Ok(new RadarMapLoadResult(true, normalized.Value))
                : OperationResult<RadarMapLoadResult>.Fail(normalized.Error ?? "Radar map validation failed.");
        }
        catch (Exception ex)
        {
            return OperationResult<RadarMapLoadResult>.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult> SaveAsync(
        RadarMapDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document is null)
        {
            return OperationResult.Fail("Radar map document cannot be null.");
        }

        var normalized = NormalizeAndValidate(document, document.MapId);
        if (!normalized.Success || normalized.Value is null)
        {
            return OperationResult.Fail(normalized.Error ?? "Radar map validation failed.");
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = ResolvePath(document.MapId);
            var value = normalized.Value!;
            if (File.Exists(path))
            {
                await using var existingStream = File.OpenRead(path);
                var existing = await JsonSerializer
                    .DeserializeAsync<RadarMapDocument>(existingStream, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is { CreatedAt: var existingCreatedAt } && existingCreatedAt != default)
                {
                    value.CreatedAt = existingCreatedAt;
                }
            }

            value.UpdatedAt = DateTimeOffset.Now;
            temporaryPath = Path.Combine(
                DirectoryPath,
                "." + document.MapId.ToString(CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
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
                    // The validated map was not replaced; a stale temporary file is harmless.
                }
            }

            _sync.Release();
        }
    }

    private string ResolvePath(uint mapId)
    {
        return Path.Combine(DirectoryPath, mapId.ToString(CultureInfo.InvariantCulture) + ".json");
    }

    private static RadarMapDocument CreateEmpty(uint mapId)
    {
        return new RadarMapDocument
        {
            MapId = mapId,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static OperationResult<RadarMapDocument> NormalizeAndValidate(
        RadarMapDocument source,
        uint expectedMapId)
    {
        if (expectedMapId == 0 || source.MapId == 0)
        {
            return OperationResult<RadarMapDocument>.Fail("MapId must be greater than zero.");
        }

        if (source.MapId != expectedMapId)
        {
            return OperationResult<RadarMapDocument>.Fail(
                "Radar map MapId mismatch. Expected " + expectedMapId + ", actual " + source.MapId + ".");
        }

        if (source.Version > RadarMapDocument.CurrentVersion)
        {
            return OperationResult<RadarMapDocument>.Fail(
                "Radar map version is newer than this client: " + source.Version + ".");
        }

        var rawSegments = source.Segments ?? new List<RadarObstacleSegment>();
        if (rawSegments.Count > MaximumSegmentCount)
        {
            return OperationResult<RadarMapDocument>.Fail(
                "Radar map contains too many segments: " + rawSegments.Count + ".");
        }

        var segments = new List<RadarObstacleSegment>(rawSegments.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in rawSegments)
        {
            if (!IsFinite(raw.Start) || !IsFinite(raw.End))
            {
                return OperationResult<RadarMapDocument>.Fail("Radar map contains a non-finite coordinate.");
            }

            if (raw.Length < MinimumSegmentLengthMeters)
            {
                continue;
            }

            var key = BuildUndirectedKey(raw.Start, raw.End);
            if (!keys.Add(key))
            {
                continue;
            }

            segments.Add(new RadarObstacleSegment
            {
                Id = string.IsNullOrWhiteSpace(raw.Id)
                    ? "wall-" + (segments.Count + 1).ToString("D4", CultureInfo.InvariantCulture)
                    : raw.Id.Trim(),
                Start = raw.Start,
                End = raw.End
            });
        }

        var createdAt = source.CreatedAt == default ? DateTimeOffset.Now : source.CreatedAt;
        return OperationResult<RadarMapDocument>.Ok(new RadarMapDocument
        {
            Version = RadarMapDocument.CurrentVersion,
            MapId = source.MapId,
            MapCode = source.MapCode?.Trim() ?? string.Empty,
            CreatedAt = createdAt,
            UpdatedAt = source.UpdatedAt == default ? createdAt : source.UpdatedAt,
            Segments = segments
        });
    }

    private static bool IsFinite(RadarPoint point)
    {
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    private static string BuildUndirectedKey(RadarPoint left, RadarPoint right)
    {
        var first = FormatPoint(left);
        var second = FormatPoint(right);
        return string.CompareOrdinal(first, second) <= 0
            ? first + "|" + second
            : second + "|" + first;
    }

    private static string FormatPoint(RadarPoint point)
    {
        return Math.Round(point.X, 4).ToString("0.####", CultureInfo.InvariantCulture) + "," +
               Math.Round(point.Y, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
