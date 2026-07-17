using System.Globalization;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Composition;

internal static class TeamMonitorLiveProbe
{
    public static bool ShouldRun(string[] args)
    {
        if (args.Any(arg =>
                string.Equals(arg, "team_monitor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "team_snapshot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--team-monitor", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var mode = Environment.GetEnvironmentVariable("ROADHOG_TEST_MODE")
                   ?? Environment.GetEnvironmentVariable("AION_TEST_MODE");
        return string.Equals(mode, "team_monitor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "team_snapshot", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var clientRoot = ReadOption(args, "--root=", "ROADHOG_CLIENT_ROOT", string.Empty);
        if (!string.IsNullOrWhiteSpace(clientRoot))
        {
            var fullRoot = Path.GetFullPath(clientRoot);
            Environment.SetEnvironmentVariable(RoadhogServiceOptions.ClientRootEnvironmentVariable, fullRoot);
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEMPROCFS_HOME")))
            {
                Environment.SetEnvironmentVariable("MEMPROCFS_HOME", fullRoot);
            }
        }

        var account = ReadOption(args, "--account=", "ROADHOG_TEAM_MONITOR_ACCOUNT", "account1");
        Console.WriteLine("Roadhog team monitor live probe.");
        Console.WriteLine("Account=" + account);
        Console.WriteLine("ClientRoot=" + (Environment.GetEnvironmentVariable(RoadhogServiceOptions.ClientRootEnvironmentVariable) ?? "<default>"));
        Console.WriteLine("MemProcFsHome=" + (Environment.GetEnvironmentVariable("MEMPROCFS_HOME") ?? "<default>"));

        using var services = RoadhogServices.Create(RoadhogServiceOptions.FromEnvironment());
        if (ReadBoolOption(args, "--attack-watch", "ROADHOG_TEAM_ATTACK_WATCH", false))
        {
            var watchMs = ReadIntOption(args, "--watch-ms=", "ROADHOG_TEAM_ATTACK_WATCH_MS", 60_000);
            var intervalMs = ReadIntOption(args, "--interval-ms=", "ROADHOG_TEAM_ATTACK_WATCH_INTERVAL_MS", 500);
            return await WatchAttackersAsync(services, account, watchMs, intervalMs).ConfigureAwait(false);
        }

        var result = await services.Runtime.ReadTeamSnapshotAsync(account).ConfigureAwait(false);
        if (!result.Success || result.Value is null)
        {
            Console.Error.WriteLine("Team snapshot read failed: " + (result.Error ?? "unknown error"));
            return 2;
        }

        PrintSnapshot(result.Value);
        return 0;
    }

    private static async Task<int> WatchAttackersAsync(
        RoadhogServices services,
        string account,
        int watchMs,
        int intervalMs)
    {
        var startedAt = DateTimeOffset.Now;
        var endsAt = startedAt.AddMilliseconds(Math.Max(1, watchMs));
        intervalMs = Math.Clamp(intervalMs, 100, 5000);

        Console.WriteLine(
            "AttackWatch=started DurationMs=" + watchMs.ToString(CultureInfo.InvariantCulture) +
            " IntervalMs=" + intervalMs.ToString(CultureInfo.InvariantCulture));

        var hitCount = 0;
        while (DateTimeOffset.Now < endsAt)
        {
            var teamResult = await services.Runtime.ReadTeamSnapshotAsync(account).ConfigureAwait(false);
            if (!teamResult.Success || teamResult.Value is null)
            {
                Console.WriteLine("AttackWatchRead=team_failed Error=\"" + (teamResult.Error ?? "unknown error") + "\"");
                await Task.Delay(intervalMs).ConfigureAwait(false);
                continue;
            }

            var worldResult = await services.Runtime.RefreshWorldObjectsAsync(account).ConfigureAwait(false);
            if (!worldResult.Success || worldResult.Value is null)
            {
                Console.WriteLine("AttackWatchRead=world_failed Error=\"" + (worldResult.Error ?? "unknown error") + "\"");
                await Task.Delay(intervalMs).ConfigureAwait(false);
                continue;
            }

            var team = teamResult.Value;
            var watched = BuildWatchedTargets(team);
            var matches = worldResult.Value
                .Where(target => target.TargetServerObjectId != 0 && watched.ContainsKey(target.TargetServerObjectId))
                .OrderBy(target => target.DistanceToLocalPlayer ?? double.MaxValue)
                .ToArray();

            var elapsedMs = (int)(DateTimeOffset.Now - startedAt).TotalMilliseconds;
            if (matches.Length == 0)
            {
                Console.WriteLine(
                    "AttackWatchTick ElapsedMs=" + elapsedMs.ToString(CultureInfo.InvariantCulture) +
                    " WatchedTargets=" + watched.Count.ToString(CultureInfo.InvariantCulture) +
                    " MatchCount=0");
            }
            else
            {
                foreach (var match in matches)
                {
                    hitCount++;
                    var watchedTarget = watched[match.TargetServerObjectId];
                    Console.WriteLine(
                        "AttackWatchHit#" + hitCount.ToString("00", CultureInfo.InvariantCulture) +
                        " ElapsedMs=" + elapsedMs.ToString(CultureInfo.InvariantCulture) +
                        " TargetKind=" + watchedTarget.Kind +
                        " TargetOwner=\"" + watchedTarget.OwnerName + "\"" +
                        " TargetServerId=" + watchedTarget.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                        " MonsterServerId=" + match.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                        " MonsterEntityId=" + match.EntityId.ToString(CultureInfo.InvariantCulture) +
                        " MonsterName=\"" + match.Name + "\"" +
                        " ObjectKind=\"" + match.ObjectKind + "\"" +
                        " MonsterHp=" + match.CurrentHp.ToString(CultureInfo.InvariantCulture) + "/" + match.MaxHp.ToString(CultureInfo.InvariantCulture) +
                        " Distance=" + FormatDistance(match.DistanceToLocalPlayer));
                }
            }

            await Task.Delay(intervalMs).ConfigureAwait(false);
        }

        Console.WriteLine("AttackWatch=completed HitCount=" + hitCount.ToString(CultureInfo.InvariantCulture));
        return hitCount > 0 ? 0 : 1;
    }

    private static Dictionary<uint, WatchedTeamTarget> BuildWatchedTargets(TeamSnapshot team)
    {
        var result = new Dictionary<uint, WatchedTeamTarget>();
        foreach (var member in team.Members)
        {
            if (member.ServerObjectId != 0)
            {
                result[member.ServerObjectId] = new WatchedTeamTarget(
                    member.ServerObjectId,
                    member.Name,
                    member.Name,
                    member.IsSelf ? "self" : "member");
            }

            if (member.SummonedPet?.Pet is { IsSummoned: true, ServerObjectId: not 0 } pet)
            {
                result[pet.ServerObjectId] = new WatchedTeamTarget(
                    pet.ServerObjectId,
                    pet.Name,
                    member.Name,
                    member.IsSelf ? "self_pet" : "member_pet");
            }
        }

        return result;
    }

    private static void PrintSnapshot(TeamSnapshot snapshot)
    {
        var party = snapshot.Party;
        Console.WriteLine(
            "TeamSnapshot" +
            " CapturedAt=" + snapshot.CapturedAt.ToString("O", CultureInfo.InvariantCulture) +
            " PartyId=" + party.PartyId.ToString(CultureInfo.InvariantCulture) +
            " Flags=0x" + party.PartyFlags.ToString("X", CultureInfo.InvariantCulture) +
            " PrimaryCount=" + party.PrimaryPartyCount.ToString(CultureInfo.InvariantCulture) +
            " LocalServerId=" + party.LocalServerObjectId.ToString(CultureInfo.InvariantCulture) +
            " LocalName=\"" + party.LocalName + "\"" +
            " LeaderServerId=" + party.LeaderServerObjectId.ToString(CultureInfo.InvariantCulture) +
            " LeaderName=\"" + (party.LeaderMember?.Name ?? string.Empty) + "\"" +
            " LocalIsLeader=" + FormatBool(party.LocalIsLeader) +
            " VisiblePlayerActors=" + party.VisiblePlayerActorCount.ToString(CultureInfo.InvariantCulture) +
            " PartyPetCount=" + snapshot.PartyMemberPetCount.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(party.MemberReadError))
        {
            Console.WriteLine("PartyMemberReadError=\"" + party.MemberReadError + "\"");
        }

        if (!string.IsNullOrWhiteSpace(party.LiveActorReadError))
        {
            Console.WriteLine("LiveActorReadError=\"" + party.LiveActorReadError + "\"");
        }

        Console.WriteLine("Members Count=" + snapshot.Members.Count.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < snapshot.Members.Count; i++)
        {
            var member = snapshot.Members[i];
            var partyMember = member.PartyMember;
            Console.WriteLine(
                "TeamMember#" + (i + 1).ToString("00", CultureInfo.InvariantCulture) +
                " F" + member.FunctionKeyNumber.ToString(CultureInfo.InvariantCulture) +
                " IsSelf=" + FormatBool(member.IsSelf) +
                " IsLeader=" + FormatBool(member.IsLeader) +
                " ServerId=" + partyMember.ServerObjectId.ToString(CultureInfo.InvariantCulture) +
                " Name=\"" + partyMember.Name + "\"" +
                " ClassId=" + partyMember.ClassId.ToString(CultureInfo.InvariantCulture) +
                " Level=" + partyMember.Level.ToString(CultureInfo.InvariantCulture) +
                " HP=" + partyMember.CurrentHp.ToString(CultureInfo.InvariantCulture) + "/" + partyMember.MaxHp.ToString(CultureInfo.InvariantCulture) +
                " MP=" + partyMember.CurrentMp.ToString(CultureInfo.InvariantCulture) + "/" + partyMember.MaxMp.ToString(CultureInfo.InvariantCulture) +
                " Alive=" + FormatBool(partyMember.IsAlive) +
                " Visibility=" + partyMember.VisibilityState +
                " Distance=" + FormatDistance(partyMember.DistanceToLocalPlayer) +
                " LiveTargetId=" + partyMember.LiveTargetServerObjectId.ToString(CultureInfo.InvariantCulture) +
                " AbnormalCount=" + partyMember.AbnormalStatuses.Count.ToString(CultureInfo.InvariantCulture) +
                " PhysicalCount=" + partyMember.PhysicalAbnormalCount.ToString(CultureInfo.InvariantCulture) +
                " HasPet=" + FormatBool(member.HasSummonedPet) +
                " PetServerId=" + (member.SummonedPet?.Pet.ServerObjectId ?? 0).ToString(CultureInfo.InvariantCulture) +
                " PetName=\"" + (member.SummonedPet?.Pet.Name ?? string.Empty) + "\"");
        }
    }

    private static string FormatDistance(double? distance)
    {
        return distance.HasValue
            ? distance.Value.ToString("F2", CultureInfo.InvariantCulture)
            : "Unknown";
    }

    private static string FormatBool(bool value)
    {
        return value ? "yes" : "no";
    }

    private static string ReadOption(string[] args, string prefix, string envName, string defaultValue)
    {
        var argValue = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argValue is not null)
        {
            return argValue[prefix.Length..].Trim().Trim('"');
        }

        var envValue = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrWhiteSpace(envValue) ? defaultValue : envValue.Trim();
    }

    private static int ReadIntOption(string[] args, string prefix, string envName, int defaultValue)
    {
        var text = ReadOption(args, prefix, envName, string.Empty);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static bool ReadBoolOption(string[] args, string option, string envName, bool defaultValue)
    {
        if (args.Any(arg => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var value = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" or "on" => true,
            "0" or "false" or "no" or "n" or "off" => false,
            _ => defaultValue
        };
    }

    private sealed record WatchedTeamTarget(
        uint ServerObjectId,
        string Name,
        string OwnerName,
        string Kind);
}
