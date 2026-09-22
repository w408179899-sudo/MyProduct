using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.Config;

public sealed class SharedAccountConfigurationStore : ISharedAccountConfiguration
{
    public const string DefaultFileName = "shared-cleanup.json";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip
    };
    public string FilePath { get; }
    public string Region { get; set; }

    public SharedAccountConfigurationStore(string path, string region)
    { FilePath = Path.GetFullPath(path); Region = string.IsNullOrWhiteSpace(region) ? "未分区" : region.Trim(); }

    public static string PathFor(string accountConfigPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(accountConfigPath))!, DefaultFileName);

    internal async Task<SharedCleanupConfiguration> ReadAsync(CancellationToken token)
    {
        await using var stream = AtomicJsonFile.OpenRead(FilePath);
        using var parsed = await JsonDocument.ParseAsync(stream, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }, token).ConfigureAwait(false);
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out _) ||
            !root.TryGetProperty("common", out var common) || common.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("auctions", out _) || !root.TryGetProperty("monsterFilters", out _) ||
            !root.TryGetProperty("migratedAccounts", out _) ||
            new[] { "whitelist", "blacklist", "sell", "stall" }.Any(key => !common.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array))
            throw new InvalidDataException("共享清包配置缺少必要字段。");
        var data = root.Deserialize<SharedCleanupConfiguration>(Json);
        if (data is null || data.Version != 1 || data.Common is null || data.Auctions is null ||
            data.MonsterFilters is null || data.MigratedAccounts is null || data.Auctions.Any(a => a.Value is null))
            throw new InvalidDataException("共享清包配置无效或版本不受支持。");
        return data;
    }

    internal async Task UpdateAsync(Func<SharedCleanupConfiguration, Task<bool>> edit, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // The file lock also serializes independent client processes. Atomic replacement
        // keeps workers' lock-free reads coherent.
        FileStream? gate = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (gate is null)
        {
            token.ThrowIfCancellationRequested();
            try { gate = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(25, token).ConfigureAwait(false); }
        }
        await using (gate)
        {
            var data = AtomicJsonFile.Exists(FilePath) ? await ReadAsync(token).ConfigureAwait(false) : new();
            if (await edit(data).ConfigureAwait(false))
                await AtomicJsonFile.WriteAsync(FilePath, data, Json, token).ConfigureAwait(false);
        }
    }

    public async Task<OperationResult<BagCleanupNameListsLoadResult>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult<BagCleanupNameListsLoadResult>.Ok(new(data.ForRegion(Region), BagCleanupNameListsSource.Json));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return OperationResult<BagCleanupNameListsLoadResult>.Fail(ex.Message); }
    }

    public async Task<OperationResult> SaveAsync(BagCleanupNameListsDocument document, CancellationToken cancellationToken = default)
    {
        var before = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!before.Success) return OperationResult.Fail(before.Error!);
        return await SaveChangesAsync(before.Value!.Document!, document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> SaveChangesAsync(BagCleanupNameListsDocument before, BagCleanupNameListsDocument after,
        CancellationToken cancellationToken = default)
    {
        var region = Region;
        try
        {
            await UpdateAsync(data =>
            {
                // A deleted shared file is not a new empty configuration during an edit.
                if (!AtomicJsonFile.Exists(FilePath)) throw new InvalidDataException("共享配置已丢失，请重新打开设置。");
                data.Common.Whitelist = SharedCleanupConfiguration.PatchNames(data.Common.Whitelist, before.Whitelist, after.Whitelist);
                data.Common.Blacklist = SharedCleanupConfiguration.PatchNames(data.Common.Blacklist, before.Blacklist, after.Blacklist);
                data.Common.Sell = SharedCleanupConfiguration.PatchNames(data.Common.Sell, before.Sell, after.Sell);
                data.Common.Stall = SharedCleanupConfiguration.PatchPrices(data.Common.Stall, before.Stall, after.Stall);
                data.Auctions[region] = SharedCleanupConfiguration.PatchPrices(data.Auctions.GetValueOrDefault(region) ?? new(), before.AuctionHouse, after.AuctionHouse);
                return Task.FromResult(true);
            }, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return OperationResult.Fail(ex.Message); }
    }

    public async Task<OperationResult<List<string>>> LoadMonsterFiltersAsync(CancellationToken cancellationToken = default)
    {
        try { return OperationResult<List<string>>.Ok(BagCleanupNameListsDocument.NormalizeKeywords((await ReadAsync(cancellationToken).ConfigureAwait(false)).MonsterFilters)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return OperationResult<List<string>>.Fail(ex.Message); }
    }

    public async Task<OperationResult> SaveMonsterFiltersAsync(IReadOnlyList<string> before, IReadOnlyList<string> after,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await UpdateAsync(data =>
            {
                if (!AtomicJsonFile.Exists(FilePath)) throw new InvalidDataException("共享配置已丢失，请重新打开设置。");
                data.MonsterFilters = SharedCleanupConfiguration.PatchNames(data.MonsterFilters, before, after);
                return Task.FromResult(true);
            }, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return OperationResult.Fail(ex.Message); }
    }
}
