using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Api;

internal interface IEquipmentUpgradeGameApi
{
    Task<OperationResult<EquipmentUpgradeInventory>> ReadEquipmentUpgradeInventoryAsync(GameApiReadContext context, CancellationToken token);
    Task<OperationResult<EquipmentUpgradeUi>> ReadEquipmentUpgradeUiAsync(GameApiReadContext context, CancellationToken token);
}
