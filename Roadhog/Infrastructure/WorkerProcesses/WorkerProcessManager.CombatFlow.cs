using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Config;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed partial class WorkerProcessManager
{
    public JsonCombatFlowPolicyStore CombatFlowPolicy => new(JsonCombatFlowPolicyStore.PathFor(_paths.AccountConfigPath));

    public async Task<IReadOnlyDictionary<string, OperationResult>> SetForceLegacyCombatFlowAsync(
        bool forceLegacy, CancellationToken cancellationToken = default)
    {
        using var admission = await HoldStartAdmissionsAsync(cancellationToken).ConfigureAwait(false);
        var saved = await CombatFlowPolicy.SaveAsync(forceLegacy, cancellationToken).ConfigureAwait(false);
        if (!saved.Success) throw new InvalidOperationException("战斗版本设置未保存：" + saved.Error);
        _logger.Info("combat_flow.policy.saved", new Dictionary<string, object?> { ["forceLegacy"] = forceLegacy });
        if (!forceLegacy) return new Dictionary<string, OperationResult>();
        return await StopNewCombatFlowAccountsAsync().ConfigureAwait(false);
    }

    private async Task EnforcePersistedCombatFlowPolicyAsync(CancellationToken cancellationToken)
    {
        var loaded = await CombatFlowPolicy.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Success && loaded.Value is { ForceLegacy: false }) return;
        if (!loaded.Success)
            _logger.Warn("combat_flow.policy.invalid_using_legacy", new Dictionary<string, object?>
                { ["path"] = CombatFlowPolicy.FilePath, ["error"] = loaded.Error });
        // Adoption has finished and released every entry gate. Complete a rollback
        // interrupted between its durable policy write and process shutdown before
        // starting the recovery monitor.
        using var admission = await HoldStartAdmissionsAsync(cancellationToken).ConfigureAwait(false);
        await StopNewCombatFlowAccountsAsync().ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, OperationResult>> StopNewCombatFlowAccountsAsync()
    {

        // Persist first, then cancel the current generation through the same explicit-stop
        // path as the Stop button. No restart or transaction request is queued here.
        var affected = Entries().Where(entry =>
        {
            lock (entry.Sync)
            {
                var version = entry.Status?.ActiveCombatFlowVersion ?? entry.DispatchedCombatFlowVersion ?? entry.Config.CombatFlowVersion;
                return (entry.Desired || entry.Process is not null) &&
                    version == CombatFlowVersion.CoordinatedV2;
            }
        }).ToArray();
        var stops = affected.Select(entry => new
        {
            entry.Config.AccountName,
            Completion = StopAsync(entry.Config.InstanceId)
        }).ToArray();
        var results = new Dictionary<string, OperationResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var stop in stops)
        {
            var result = await stop.Completion.ConfigureAwait(false);
            results.Add(stop.AccountName, result);
            if (!result.Success)
                _logger.Warn("combat_flow.rollback.stop_failed", new Dictionary<string, object?>
                    { ["account"] = stop.AccountName, ["error"] = result.Error });
        }
        return results;
    }
}
