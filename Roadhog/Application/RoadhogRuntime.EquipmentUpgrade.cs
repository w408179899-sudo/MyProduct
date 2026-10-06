using Roadhog.Application.EquipmentUpgrade;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Application;

public sealed partial class RoadhogRuntime
{
    public async Task<EquipmentUpgradeInventory> RefreshEquipmentUpgradeAsync(string account, CancellationToken token)
    {
        var snapshots = _snapshotReaders.Create(ResolveSnapshotConfig(account), _logger, token);
        return (await snapshots.ReadEquipmentUpgradeInventoryAsync().WaitAsync(token).ConfigureAwait(false)).Value;
    }

    public Task<OperationResult<EquipmentUpgradeResult>> RunEquipmentUpgradeBatchAsync(string account,
        EquipmentUpgradeSettings settings, IProgress<string>? progress, CancellationToken token) =>
        RunEquipmentUpgradeCoreAsync(account, settings, null, progress, token);

    public Task<OperationResult<EquipmentUpgradeResult>> RunEquipmentUpgradeAsync(string account,
        EquipmentUpgradeSettings settings, EquipmentUpgradeKind kind, IProgress<string>? progress, CancellationToken token)
        => RunEquipmentUpgradeCoreAsync(account, settings, kind, progress, token);

    private async Task<OperationResult<EquipmentUpgradeResult>> RunEquipmentUpgradeCoreAsync(string account,
        EquipmentUpgradeSettings settings, EquipmentUpgradeKind? kind, IProgress<string>? progress, CancellationToken token)
    {
        if (_keyboardInput == null) return OperationResult<EquipmentUpgradeResult>.Fail("鼠标键盘输入不可用。");
        if (!await _inventoryTestGate.WaitAsync(0, token).ConfigureAwait(false))
            return OperationResult<EquipmentUpgradeResult>.Fail("另一个背包操作正在进行。");
        try
        {
            OperationResult<EquipmentUpgradeResult>? result = null;
            async Task<OperationResult> Execute()
            {
                var snapshots = _snapshotReaders.Create(ResolveSnapshotConfig(account), _logger, token);
                result = kind.HasValue
                    ? await new EquipmentUpgradeSequence(_keyboardInput, _logger).RunAsync(snapshots, account, settings, kind.Value, progress, token).ConfigureAwait(false)
                    : await new EquipmentUpgradeBatch(_keyboardInput, _logger).RunAsync(snapshots, account, settings, progress, token).ConfigureAwait(false);
                return result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Error ?? "装备操作停止。");
            }
            var operation = Orchestrator == null ? await Execute().ConfigureAwait(false)
                : await Orchestrator.RunManualInputAsync(Execute).ConfigureAwait(false);
            return result ?? OperationResult<EquipmentUpgradeResult>.Fail(operation.Error ?? "无法开始装备操作。");
        }
        finally { _inventoryTestGate.Release(); }
    }
}
