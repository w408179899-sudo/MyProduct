using Roadhog.Application.Channels;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;

namespace Roadhog.Application.Food;

/// <summary>Per-worker post-combat maintenance without a second snapshot cache.</summary>
public sealed class FoodMaintenanceController(IKeyboardInput input,
    Func<int, CancellationToken, Task>? delay = null, int useTimeoutMs = 12000,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public bool Pending { get; private set; }
    private DateTimeOffset nextAttempt;
    private readonly Dictionary<FoodKind, DateTimeOffset> retryAfter = new();

    public void ObserveCombat(MaintenanceScriptSettings settings, bool fighting)
    {
        if (!settings.AutoDrinkEnabled && !settings.AutoFoodEnabled) { Pending = false; return; }
        if (fighting) Pending = true;
    }

    internal static bool HasWork(StationaryCombatState state) => state.Fighting ||
        state.CurrentTargetEntityId != 0 || state.CurrentTargetServerObjectId != 0;

    public async Task<bool> TickAsync(AccountWorkerContext context, StationaryCombatState state,
        Func<Task> prepareInput, Func<bool> otherWork)
    {
        var settings = context.Config.ScriptSettings?.Maintenance;
        if (settings == null) return false;
        if (!Pending || (!settings.AutoDrinkEnabled && !settings.AutoFoodEnabled) ||
            clock.GetUtcNow() < nextAttempt || HasWork(state) || ChannelSwitchSafety.HasExclusiveWork(state) || otherWork())
            return false;
        nextAttempt = clock.GetUtcNow().AddSeconds(5);
        async Task<bool> Safe()
        {
            if (HasWork(state) || ChannelSwitchSafety.HasExclusiveWork(state) || otherWork()) return false;
            var p = (await context.Snapshots.ReadPlayerAsync().WaitAsync(context.StopToken)).Value;
            if (p.IsDead || p.IsResting) return false;
            var petId = state.LocalCombatSidePetServerObjectId;
            if (p.IsSpiritmaster)
            {
                var pet = (await context.Snapshots.ReadSummonedPetAsync().WaitAsync(context.StopToken)).Value;
                petId = pet.IsSummoned ? pet.ServerObjectId : 0;
            }
            var target = (await context.Snapshots.ReadLockedTargetAsync().WaitAsync(context.StopToken)).Value;
            if (context.Config.ScriptSettings?.MainMode == AccountMainMode.SemiAuto && target.IsMonsterAlive) return false;
            if (target.HasTarget && target.IsAlive && target.ServerObjectId != target.LocalServerObjectId &&
                (target.IsTargetingLocalPlayer || target.TargetServerObjectIdMatchesLocal ||
                 (petId != 0 && target.TargetServerObjectId == petId))) return false;
            var world = (await context.Snapshots.ReadWorldObjectsAsync().WaitAsync(context.StopToken)).Value;
            return !world.Any(i => i.IsAlive && (i.IsTargetingLocalPlayer || (petId != 0 && i.TargetServerObjectId == petId)));
        }
        var used = false;
        try
        {
            var catalog = FoodCatalog.Default;
            var statuses = (await context.Snapshots.ReadPlayerAbnormalStatusesAsync().WaitAsync(context.StopToken)).Value;
            var retryPending = false;
            foreach (var kind in new[] { FoodKind.Drink, FoodKind.Food })
            {
                if (kind == FoodKind.Drink ? !settings.AutoDrinkEnabled : !settings.AutoFoodEnabled) continue;
                if (retryAfter.GetValueOrDefault(kind) > clock.GetUtcNow()) { retryPending = true; continue; }
                try
                {
                    if (catalog.HasStatus(statuses, kind)) continue;
                    var inventory = (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value;
                    var player = (await context.Snapshots.ReadPlayerAsync().WaitAsync(context.StopToken)).Value;
                    var preferences = kind == FoodKind.Drink ? settings.PreferredDrinks : settings.PreferredFoods;
                    FoodQuickbarBinding? binding = null;
                    if (preferences is { Count: > 0 })
                    {
                        var quickbar = (await context.Snapshots.ReadQuickbarAsync().WaitAsync(context.StopToken)).Value;
                        binding = FoodQuickbarBindings.Select(quickbar, inventory, catalog, kind, player.Level, preferences);
                    }
                    var item = binding?.Item ?? catalog.Select(inventory, kind, player.Level);
                    if (item == null) continue;
                    if (!await Safe()) return used;
                    await prepareInput();
                    if (!await Safe()) return used;
                    var success = await new FoodUseSequence(input, catalog, delay, useTimeoutMs)
                        .RunAsync(context.Snapshots, kind, item, Safe, context.StopToken, binding: binding);
                    used |= success;
                    if (success) context.Logger.Info("food.used", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName, ["kind"] = kind.ToString(),
                        ["templateId"] = item.TemplateId, ["instanceId"] = item.InstanceId,
                        ["key"] = binding?.Key, ["source"] = binding == null ? "inventory" : "quickbar",
                        ["name"] = item.Name, ["level"] = item.Food!.Level, ["skill"] = item.Food.UseSkillName
                    });
                }
                catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    retryPending = true;
                    retryAfter[kind] = clock.GetUtcNow().AddSeconds(30);
                    context.Logger.Warn("food.attempt_unconfirmed", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName, ["kind"] = kind.ToString(),
                        ["error"] = ex.Message, ["retryAfterMs"] = 30000
                    });
                }
            }
            Pending = retryPending;
        }
        catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            nextAttempt = clock.GetUtcNow().AddSeconds(30);
            context.Logger.Warn("food.attempt_unconfirmed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["error"] = ex.Message,
                ["retryAfterMs"] = 30000
            });
        }
        return used;
    }
}
