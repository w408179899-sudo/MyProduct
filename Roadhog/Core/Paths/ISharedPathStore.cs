using Roadhog.Core.Common;
using Roadhog.Core.Accounts;

namespace Roadhog.Core.Paths;

public interface ISharedPathStore
{
    Task<OperationResult<IReadOnlyList<SharedPathSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<SharedPathDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        SharedPathDocument path,
        CancellationToken cancellationToken = default);

    async Task<OperationResult> SaveMonsterKillPrioritiesAsync(
        string name, IReadOnlyList<string> before, IReadOnlyList<string> after,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(name, cancellationToken).ConfigureAwait(false);
        if (loaded.Value is not { } path) return OperationResult.Fail(loaded.Error ?? "复活路径读取失败。");
        if (!path.ReadMonsterKillPriorities().SequenceEqual(before, StringComparer.OrdinalIgnoreCase))
            return OperationResult.Fail("击杀优先级已被修改，请核对当前顺序后重试。");
        path.MonsterKillPriorities = BagCleanupNameListsDocument.NormalizeKeywords(after);
        return await SaveAsync(path, cancellationToken).ConfigureAwait(false);
    }

    async Task<OperationResult<List<string>>> LoadMonsterKillPrioritiesAsync(string name, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(name, cancellationToken).ConfigureAwait(false);
        return loaded.Value is { } path
            ? OperationResult<List<string>>.Ok(path.ReadMonsterKillPriorities())
            : OperationResult<List<string>>.Fail(loaded.Error ?? "复活路径读取失败。");
    }

    Task<OperationResult> DeleteAsync(
        string name,
        CancellationToken cancellationToken = default);
}
