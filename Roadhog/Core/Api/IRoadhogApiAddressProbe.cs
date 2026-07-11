#if DEBUG
using Roadhog.Core.Common;

namespace Roadhog.Core.Api;

public interface IRoadhogApiAddressProbe
{
    Task<OperationResult<IReadOnlyList<GameApiAddressProbeResult>>> ProbeAddressesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default);
}

public sealed record GameApiAddressProbeResult(
    string Name,
    bool Success,
    string Detail)
{
    public static readonly IReadOnlyList<string> RequiredCheckNames = Array.AsReadOnly(new[]
    {
        "Address.EntitySystemPointer",
        "Address.ServerObjectTree",
        "Address.PrimaryPartyList",
        "Address.SecondaryPartyList",
        "Address.LocalEntityId",
        "Address.LocalTargetEntityId",
        "Address.LocalMaxHp",
        "Address.LocalCurrentHp",
        "Address.LocalMaxMp",
        "Address.LocalCurrentMp",
        "Address.LocalCurrentDp",
        "Address.CameraPitch",
        "Address.CameraRoll",
        "Address.CameraYaw",
        "Address.SpecialCameraMode",
        "Address.SpecialCameraPitch",
        "Address.SpecialCameraRoll",
        "Address.SpecialCameraYaw",
        "Address.SkillInventoryManager",
        "Address.LearnedSkillTree",
        "Address.InventoryMoney",
        "Address.InventoryCapacity",
        "Address.InventoryItemTreeHeader",
        "Address.InventoryItemTreeCount",
        "Address.InventoryEquipmentIds",
        "Address.ItemStaticIndex",
        "Address.StaticResolverChunk0",
        "Address.DlgInventoryDialog27Method",
        "Address.DlgInventoryDialog28Method"
    });
}
#endif
