using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public sealed class TeamMonitor
{
    private readonly IRoadhogSnapshotReader _snapshots;
    private readonly IRoadhogLogger? _logger;

    public TeamMonitor(IRoadhogSnapshotReader snapshots, IRoadhogLogger? logger = null)
    {
        _snapshots = snapshots;
        _logger = logger;
    }

    public async Task<TeamSnapshot> ReadSnapshotAsync()
    {
        var party = await _snapshots.ReadPartyAsync().ConfigureAwait(false);
        var roster = await _snapshots.ReadSummonedPetRosterAsync().ConfigureAwait(false);
        var snapshot = BuildSnapshot(party.Value, roster.Value);
        _logger?.Info("team_monitor.snapshot.read", new Dictionary<string, object?>
        {
            ["memberCount"] = snapshot.Members.Count,
            ["localServerObjectId"] = snapshot.Party.LocalServerObjectId,
            ["leaderServerObjectId"] = snapshot.Party.LeaderServerObjectId,
            ["localIsLeader"] = snapshot.Party.LocalIsLeader,
            ["partyPetCount"] = snapshot.PartyMemberPetCount
        });

        return snapshot;
    }

    private static TeamSnapshot BuildSnapshot(PartySnapshot party, SummonedPetRosterSnapshot roster)
    {
        var petsByOwner = roster.PartyMemberPets
            .GroupBy(pet => pet.OwnerServerObjectId)
            .ToDictionary(group => group.Key, group => group.First());

        var members = new List<TeamMemberSnapshot>(party.Members.Count);
        var nextOtherMemberFunctionKey = 2;
        foreach (var member in party.Members)
        {
            var functionKey = member.IsSelf ? 1 : nextOtherMemberFunctionKey++;
            petsByOwner.TryGetValue(member.ServerObjectId, out var pet);
            if (member.IsSelf && roster.LocalPlayerPet.OwnerServerObjectId == member.ServerObjectId)
            {
                pet = roster.LocalPlayerPet;
            }

            members.Add(new TeamMemberSnapshot(member, functionKey, pet));
        }

        return new TeamSnapshot(party, roster, members, DateTimeOffset.Now);
    }
}
