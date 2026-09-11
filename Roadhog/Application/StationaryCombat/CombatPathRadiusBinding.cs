using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;

namespace Roadhog.Application.StationaryCombat;

/// <summary>Applies a path radius to private startup settings: revive path, combat path, then profile.</summary>
public static class CombatPathRadiusBinding
{
    public static async Task ApplyAsync(
        AccountConfig workerConfig,
        ISharedPathStore pathStore,
        IRoadhogLogger logger,
        CancellationToken cancellationToken)
    {
        var settings = workerConfig.ScriptSettings;
        if (settings is null || settings.MainMode != AccountMainMode.CustomCombat ||
            settings.CombatMode != AccountCombatMode.Stationary)
        {
            return;
        }

        var revivePathName = ResolveName(settings.Paths?.RevivePathName, workerConfig.RevivePathName);
        if (!string.IsNullOrWhiteSpace(revivePathName) &&
            await TryApplyAsync(workerConfig, pathStore, logger, revivePathName, "revive", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var combatPathName = ResolveName(settings.Paths?.CombatPathName, workerConfig.CombatPathName);
        if (!string.IsNullOrWhiteSpace(combatPathName) &&
            !string.Equals(combatPathName, revivePathName, StringComparison.OrdinalIgnoreCase))
        {
            await TryApplyAsync(workerConfig, pathStore, logger, combatPathName, "combat", cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolveName(string? configuredName, string? legacyName) =>
        (string.IsNullOrWhiteSpace(configuredName) ? legacyName : configuredName)?.Trim() ?? string.Empty;

    private static async Task<bool> TryApplyAsync(
        AccountConfig workerConfig,
        ISharedPathStore pathStore,
        IRoadhogLogger logger,
        string pathName,
        string pathKind,
        CancellationToken cancellationToken)
    {
        var settings = workerConfig.ScriptSettings!;
        var result = await pathStore.LoadAsync(pathName, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var fields = new Dictionary<string, object?>
        {
            ["account"] = workerConfig.AccountName,
            ["pathName"] = pathName,
            ["pathKind"] = pathKind,
            ["profileRadius"] = settings.Combat.StationaryCombatRadius
        };
        if (!result.Success || result.Value is null)
        {
            fields["error"] = result.Error;
            logger.Warn("stationary_combat.path_radius.load_failed", fields);
            return false;
        }

        if (!result.Value.BoundStationaryCombatRadius.HasValue)
        {
            return false;
        }

        if (!result.Value.TryGetBoundStationaryCombatRadius(out var radius))
        {
            fields["boundRadius"] = result.Value.BoundStationaryCombatRadius;
            logger.Warn("stationary_combat.path_radius.invalid", fields);
            return false;
        }

        settings.Combat.StationaryCombatRadius = radius;
        fields["radius"] = radius;
        logger.Info("stationary_combat.path_radius.applied", fields);
        return true;
    }
}
