using System.Globalization;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Vmm;

internal static class VmmGameApiLiveProbe
{
    public static bool ShouldRun(string[] args)
    {
        return args.Any(arg =>
            string.Equals(arg, "game_api_probe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "vmm_api_probe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--game-api-probe", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var device = ReadOption(args, "--device=", "fpga");
        var processName = ReadOption(args, "--process=", "Aion.bin");
        var moduleName = ReadOption(args, "--module=", "Game.dll");
        var processId = ReadIntOption(args, "--pid=");

        Console.WriteLine(
            "Roadhog VMM game API live probe. Device=" + device +
            " Process=" + processName +
            " Pid=" + (processId == 0 ? "<by-name>" : processId.ToString(CultureInfo.InvariantCulture)) +
            " Module=" + moduleName);

        var options = new AionVmmGameApiOptions
        {
            DefaultVmmDeviceName = device,
            DefaultProcessName = processName,
            DefaultModuleName = moduleName,
            MemProcFsHome = AppContext.BaseDirectory
        };
        var context = new GameApiReadContext("live-probe", processId, processName, device, true);
        var api = new AionVmmGameApi(options, NoOpRoadhogLogger.Instance);
        var requiredReadsPassed = true;

        var player = await api.ReadPlayerAsync(context).ConfigureAwait(false);
        PrintResult(
            "Player",
            player.Success,
            player.Error,
            player.Value is null
                ? string.Empty
                : "entity=" + player.Value.EntityId.ToString(CultureInfo.InvariantCulture) +
                  ", name=" + player.Value.CharacterName +
                  ", position=" + FormatPosition(player.Value.Position) +
                  ", actorYaw=" + (player.Value.ActorYawDegrees?.ToString("0.###", CultureInfo.InvariantCulture) ?? "none"));
        requiredReadsPassed &= player.Success;

        var playerAbnormal = await api.ReadPlayerAbnormalStatusesAsync(context).ConfigureAwait(false);
        PrintResult(
            "PlayerAbnormalStatuses",
            playerAbnormal.Success,
            playerAbnormal.Error,
            "count=" + (playerAbnormal.Value?.Entries.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= playerAbnormal.Success;

        var summonedPet = await api.ReadSummonedPetAsync(context).ConfigureAwait(false);
        PrintResult(
            "SummonedPet",
            summonedPet.Success,
            summonedPet.Error,
            summonedPet.Value is null
                ? string.Empty
                : "summoned=" + (summonedPet.Value.IsSummoned ? "yes" : "no") +
                  ", serverId=" + summonedPet.Value.ServerObjectId.ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= summonedPet.Success;

        var petRoster = await api.ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        PrintResult(
            "SummonedPetRoster",
            petRoster.Success,
            petRoster.Error,
            petRoster.Value is null
                ? string.Empty
                : "localPet=" + (petRoster.Value.LocalPlayerPet.Pet.IsSummoned ? "yes" : "no") +
                  ", teamPets=" + petRoster.Value.PartyMemberPets.Count.ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= petRoster.Success;

        var party = await api.ReadPartyAsync(context).ConfigureAwait(false);
        PrintResult(
            "Party",
            party.Success,
            party.Error,
            "members=" + (party.Value?.Members.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= party.Success;

        var tacticsSigns = await api.ReadTacticsSignsAsync(context).ConfigureAwait(false);
        PrintResult(
            "TacticsSigns",
            tacticsSigns.Success,
            tacticsSigns.Error,
            "active=" + (tacticsSigns.Value?.ServerObjectIds.Count(id => id != 0) ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= tacticsSigns.Success;

        var channel = await api.ReadChannelAsync(context).ConfigureAwait(false);
        PrintResult(
            "Channel",
            channel.Success,
            channel.Error,
            channel.Value is null
                ? string.Empty
                : "number=" + channel.Value.Number.ToString(CultureInfo.InvariantCulture) +
                  ", count=" + channel.Value.Count.ToString(CultureInfo.InvariantCulture) +
                  ", mapId=" + channel.Value.MapId.ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= channel.Success;

        var lockedTarget = await api.ReadLockedTargetAsync(context).ConfigureAwait(false);
        PrintResult(
            "LockedTarget",
            lockedTarget.Success,
            lockedTarget.Error,
            lockedTarget.Value is null
                ? string.Empty
                : "hasTarget=" + (lockedTarget.Value.HasTarget ? "yes" : "no") +
                  ", entity=" + lockedTarget.Value.TargetEntityId.ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= lockedTarget.Success;

        var targetAbnormal = await api.ReadLockedTargetAbnormalStatusesAsync(context).ConfigureAwait(false);
        PrintResult(
            "LockedTargetAbnormalStatuses",
            targetAbnormal.Success,
            targetAbnormal.Error,
            "count=" + (targetAbnormal.Value?.Entries.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= targetAbnormal.Success;

        var skills = await api.ReadSkillsAsync(context).ConfigureAwait(false);
        PrintResult(
            "Skills",
            skills.Success,
            skills.Error,
            "count=" + (skills.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= skills.Success;

        var inventory = await api.ReadInventoryAsync(context).ConfigureAwait(false);
        PrintResult(
            "Inventory",
            inventory.Success,
            inventory.Error,
            "count=" + (inventory.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= inventory.Success;

        var money = await api.ReadInventoryMoneyAsync(context).ConfigureAwait(false);
        PrintResult(
            "InventoryMoney",
            money.Success,
            money.Error,
            "value=" + money.Value.ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= money.Success;

        var capacity = await api.ReadInventoryCapacityAsync(context).ConfigureAwait(false);
        PrintResult(
            "InventoryCapacity",
            capacity.Success,
            capacity.Error,
            "slots=" + capacity.Value.ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= capacity.Success;

        var worldObjects = await api.ReadWorldObjectsAsync(context).ConfigureAwait(false);
        PrintResult(
            "WorldObjects",
            worldObjects.Success,
            worldObjects.Error,
            "count=" + (worldObjects.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= worldObjects.Success;

        var gather = await api.ReadGatherSnapshotAsync(context).ConfigureAwait(false);
        PrintResult(
            "Gather",
            gather.Success,
            gather.Error,
            gather.Value is null
                ? string.Empty
                : "objects=" + gather.Value.Objects.Count.ToString(CultureInfo.InvariantCulture) +
                  ", nearbyPlayers=" + gather.Value.NearbyPlayers.Count.ToString(CultureInfo.InvariantCulture) +
                  ", nearbyMonsters=" + gather.Value.NearbyMonsters.Count.ToString(CultureInfo.InvariantCulture) +
                  ", monsterData=" + (gather.Value.MonsterDataAvailable ? "yes" : "no") +
                  ", competitionData=" + (gather.Value.CompetitionDataAvailable ? "yes" : "no") +
                  ", localProgressData=" + (gather.Value.LocalGathering.DataAvailable ? "yes" : "no") +
                  ", localGathering=" + (gather.Value.LocalGathering.IsActive ? "yes" : "no") +
                  ", localGatherSource=" + gather.Value.LocalGathering.GatherSourceId.ToString(CultureInfo.InvariantCulture) +
                  ", gatherDialogVisible=" + (gather.Value.LocalGathering.IsDialogVisible ? "yes" : "no"));
        if (gather.Value is not null)
        {
            foreach (var item in gather.Value.Objects.Take(5))
            {
                Console.WriteLine(
                    "  Gather ServerId=" + item.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                    " SourceId=" + item.GatherSourceId.ToString(CultureInfo.InvariantCulture) +
                    " Name=\"" + item.Name + "\"" +
                    " Distance=" + (item.DistanceToLocalPlayer?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a") +
                    " AvailabilityRaw=" + item.RuntimeAvailabilityRaw.ToString(CultureInfo.InvariantCulture) +
                    " InteractionState=" + item.InteractionState.ToString(CultureInfo.InvariantCulture) +
                    " Static=" + (item.Source is null ? "missing" : "ok"));
            }

            foreach (var nearbyPlayer in gather.Value.NearbyPlayers.Take(5))
            {
                Console.WriteLine(
                    "  Player ServerId=" + nearbyPlayer.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                    " Name=\"" + nearbyPlayer.Name + "\"" +
                    " Distance=" + (nearbyPlayer.DistanceToLocalPlayer?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a") +
                    " GatherStateRaw=" + nearbyPlayer.GatherActionStateRaw.ToString(CultureInfo.InvariantCulture) +
                    " GatherActionId=" + nearbyPlayer.GatherActionIdRaw.ToString(CultureInfo.InvariantCulture) +
                    " GatherSourceCandidate=" + nearbyPlayer.GatherSourceIdCandidateRaw.ToString(CultureInfo.InvariantCulture) +
                    " GatheringCandidate=" + (nearbyPlayer.IsGatheringActionCandidate ? "yes" : "no"));
            }

            foreach (var monster in gather.Value.NearbyMonsters.Take(5))
            {
                Console.WriteLine(
                    "  Monster ServerId=" + monster.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                    " Name=\"" + monster.Name + "\"" +
                    " Distance=" + (monster.DistanceToLocalPlayer?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a") +
                    " Hp=" + monster.CurrentHp.ToString(CultureInfo.InvariantCulture) +
                    "/" + monster.MaxHp.ToString(CultureInfo.InvariantCulture) +
                    " TargetServerId=" + monster.TargetServerObjectId.ToString(CultureInfo.InvariantCulture) +
                    " AggressiveKnown=" + (monster.AggressiveKnown ? "yes" : "no") +
                    " Aggressive=" + (monster.IsAggressiveToPlayer ? "yes" : "no"));
            }
        }
        requiredReadsPassed &= gather.Success;

        var corpses = await api.ReadLootCorpsesAsync(context).ConfigureAwait(false);
        PrintResult(
            "LootCorpses",
            corpses.Success,
            corpses.Error,
            "count=" + (corpses.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        requiredReadsPassed &= corpses.Success;

        var inventoryWindow = await api.ReadInventoryWindowAsync(context).ConfigureAwait(false);
        PrintResult(
            "InventoryWindow",
            inventoryWindow.Success,
            inventoryWindow.Error,
            inventoryWindow.Value is null
                ? string.Empty
                : "open=" + (inventoryWindow.Value.IsOpen ? "yes" : "no"));
        requiredReadsPassed &= inventoryWindow.Success;

        var discardConfirm = await api.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
        PrintResult(
            "InventoryDiscardConfirm",
            discardConfirm.Success,
            discardConfirm.Error,
            discardConfirm.Value is null
                ? string.Empty
                : "open=" + (discardConfirm.Value.IsOpen ? "yes" : "no") +
                  ", kind=" + discardConfirm.Value.Kind);
        requiredReadsPassed &= discardConfirm.Success;

        var addressesPassed = true;
#if DEBUG
        var addresses = await api.ProbeAddressesAsync(context).ConfigureAwait(false);
        addressesPassed = addresses.Success && addresses.Value is not null && addresses.Value.All(check => check.Success);
        PrintResult(
            "AddressProbe",
            addressesPassed,
            addresses.Error,
            addresses.Value is null
                ? string.Empty
                : "passed=" + addresses.Value.Count(check => check.Success).ToString(CultureInfo.InvariantCulture) +
                  "/" + addresses.Value.Count.ToString(CultureInfo.InvariantCulture));
        if (addresses.Value is not null)
        {
            foreach (var failed in addresses.Value.Where(check => !check.Success))
            {
                Console.WriteLine("  FAIL " + failed.Name + ": " + failed.Detail);
            }
        }
#endif

        return requiredReadsPassed && addressesPassed ? 0 : 1;
    }

    private static void PrintResult(string name, bool success, string? error, string detail)
    {
        Console.WriteLine(
            (success ? "PASS " : "FAIL ") + name +
            (string.IsNullOrWhiteSpace(detail) ? string.Empty : ": " + detail) +
            (string.IsNullOrWhiteSpace(error) ? string.Empty : "; error=" + error));
    }

    private static string FormatPosition(Roadhog.Core.Model.Vector3Snapshot? position)
    {
        if (position is not { } value)
        {
            return "none";
        }

        return value.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
               value.Y.ToString("0.###", CultureInfo.InvariantCulture) + "," +
               value.Z.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string ReadOption(string[] args, string prefix, string fallback)
    {
        var value = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(value) ? fallback : value[prefix.Length..].Trim();
    }

    private static int ReadIntOption(string[] args, string prefix)
    {
        var value = ReadOption(args, prefix, string.Empty);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
    }
}
