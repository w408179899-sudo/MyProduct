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

        var worldObjects = await api.ReadWorldObjectsAsync(context).ConfigureAwait(false);
        PrintResult(
            "WorldObjects",
            worldObjects.Success,
            worldObjects.Error,
            "count=" + (worldObjects.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture));

        var corpses = await api.ReadLootCorpsesAsync(context).ConfigureAwait(false);
        PrintResult(
            "LootCorpses",
            corpses.Success,
            corpses.Error,
            "count=" + (corpses.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture));

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

        return player.Success && worldObjects.Success && corpses.Success && addressesPassed ? 0 : 1;
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
