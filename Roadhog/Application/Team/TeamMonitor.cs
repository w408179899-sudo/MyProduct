using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public sealed class TeamMonitor
{
    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger? _logger;

    public TeamMonitor(IRoadhogGameApi gameApi, IRoadhogLogger? logger = null)
    {
        _gameApi = gameApi;
        _logger = logger;
    }

    public async Task<OperationResult<TeamSnapshot>> ReadSnapshotAsync(
        GameApiReadContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var partyResult = await ReadPartyAsync(context, cancellationToken).ConfigureAwait(false);
        if (!partyResult.Success || partyResult.Value is null)
        {
            return OperationResult<TeamSnapshot>.Fail(partyResult.Error ?? "Party snapshot read failed.");
        }

        var rosterResult = await ReadSummonedPetRosterAsync(context, cancellationToken).ConfigureAwait(false);
        if (!rosterResult.Success || rosterResult.Value is null)
        {
            return OperationResult<TeamSnapshot>.Fail(rosterResult.Error ?? "Summoned pet roster read failed.");
        }

        var snapshot = BuildSnapshot(partyResult.Value, rosterResult.Value);
        _logger?.Info("team_monitor.snapshot.read", new Dictionary<string, object?>
        {
            ["account"] = context?.AccountName,
            ["memberCount"] = snapshot.Members.Count,
            ["localServerObjectId"] = snapshot.Party.LocalServerObjectId,
            ["leaderServerObjectId"] = snapshot.Party.LeaderServerObjectId,
            ["localIsLeader"] = snapshot.Party.LocalIsLeader,
            ["partyPetCount"] = snapshot.PartyMemberPetCount
        });

        return OperationResult<TeamSnapshot>.Ok(snapshot);
    }

    private async Task<OperationResult<PartySnapshot>> ReadPartyAsync(
        GameApiReadContext? context,
        CancellationToken cancellationToken)
    {
        if (context is not null && _gameApi is IRoadhogScopedPartyGameApi scopedPartyApi)
        {
            return await scopedPartyApi.ReadPartyAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (_gameApi is IRoadhogPartyGameApi partyApi)
        {
            return await partyApi.ReadPartyAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult<PartySnapshot>.Fail("Party snapshot API is not available.");
    }

    private async Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        GameApiReadContext? context,
        CancellationToken cancellationToken)
    {
        if (context is not null && _gameApi is IRoadhogScopedGameApi scopedApi)
        {
            return await scopedApi.ReadSummonedPetRosterAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return await _gameApi.ReadSummonedPetRosterAsync(cancellationToken).ConfigureAwait(false);
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
