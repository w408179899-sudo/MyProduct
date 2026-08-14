using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

internal static class BagCleanupGameApi
{
    public static Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(
        AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadPlayerAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadPlayerAsync(context.StopToken);
    }

    public static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    public static Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(
        AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadWorldObjectsAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadWorldObjectsAsync(context.StopToken);
    }

    public static Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(
        AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadInventoryAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadInventoryAsync(context.StopToken);
    }

    public static Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        AccountWorkerContext context,
        InventoryWindowRectSource rectSource)
    {
        if (context.GameApi is not IInventoryWindowGameApi inventoryApi)
        {
            return Task.FromResult(OperationResult<InventoryWindowSnapshot>.Fail(
                "Inventory window VMM API is not available."));
        }

        return inventoryApi.ReadInventoryWindowAsync(CreateReadContext(context), rectSource, context.StopToken);
    }

    public static Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is not IInventoryWindowGameApi inventoryApi)
        {
            return Task.FromResult(OperationResult<InventoryWindowSnapshot>.Fail(
                "Inventory window VMM API is not available."));
        }

        return inventoryApi.ReadInventoryWindowAsync(CreateReadContext(context), context.StopToken);
    }

    public static Task<OperationResult<ulong>> ReadInventoryMoneyAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is not IInventoryMoneyGameApi moneyApi)
        {
            return Task.FromResult(OperationResult<ulong>.Fail(
                "Inventory money VMM API is not available."));
        }

        return moneyApi.ReadInventoryMoneyAsync(CreateReadContext(context), context.StopToken);
    }

    public static Task<OperationResult<int>> ReadInventoryCapacityAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is not IInventoryCapacityGameApi capacityApi)
        {
            return Task.FromResult(OperationResult<int>.Fail(
                "Inventory capacity VMM API is not available."));
        }

        return capacityApi.ReadInventoryCapacityAsync(CreateReadContext(context), context.StopToken);
    }

    public static Task<OperationResult<InventoryDiscardConfirmSnapshot>> ReadInventoryDiscardConfirmAsync(
        AccountWorkerContext context)
    {
        if (context.GameApi is not IInventoryDiscardConfirmGameApi discardConfirmApi)
        {
            return Task.FromResult(OperationResult<InventoryDiscardConfirmSnapshot>.Fail(
                "Inventory discard confirmation VMM API is not available."));
        }

        return discardConfirmApi.ReadInventoryDiscardConfirmAsync(
            CreateReadContext(context),
            context.StopToken);
    }

    public static GameApiReadContext CreateReadContext(AccountWorkerContext context)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName,
            BypassMemoryCache: true,
            RequireFresh: true);
    }
}
