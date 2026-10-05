using System.Reflection;
using Roadhog.Application;
using Roadhog.Application.JumpAssist;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class JumpAssistSkillRankTests
{
    public static async Task BoundLowerRankStopsTeamJumpAsync()
    {
        var api = new FakeGameApi { Skills = new[] { Skill(103), Skill(104) },
            Quickbar = new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Alt, 0, 21, 103) }) };
        var factory = new RankReaderFactory(api);
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var context = new AccountWorkerContext(new AccountConfig { AccountName = "jump-rank", ScriptSettings = new() },
            factory, logger, new AccountRuntimeManager(logger), new(), stop.Token);
        await context.PrepareSkillBindingsAsync();
        var keyboard = new RecordingKeyboardInput();
        await using var jump = new CombatJumpAssistSession(context, keyboard, teamFollower: true,
            jumpInterval: TimeSpan.FromSeconds(5), cooldownPollInterval: TimeSpan.FromMilliseconds(5),
            keyHoldDuration: TimeSpan.FromMilliseconds(1));
        await jump.EnterTeamGroupAsync();
        api.Skills = new[] { Skill(103, 7000), Skill(104) };
        await WaitAsync(() => jump.TeamCooldownConfirmed);
        Check(jump.Mode == JumpAssistMode.None && keyboard.Keys.Count == 0 && factory.ExactReads >= 2,
            "the actual lower-rank Alt-bar cooldown stops jumping before the first Space despite the unchanged highest rank");
        Check(logger.Entries.Any(entry => entry.EventName == "jump_assist.team_cooldown_confirmed"),
            "the actual cooldown observation remains diagnostic evidence");
    }

    public static async Task HighestRankAndUnboundCompatibilityAsync()
    {
        foreach (var prepare in new[] { false, true })
        {
            var api = new FakeGameApi { Skills = new[] { Skill(104) },
                Quickbar = new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Main, 0, 21, 104) }) };
            var factory = new RankReaderFactory(api);
            var logger = new InMemoryRoadhogLogger();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var context = new AccountWorkerContext(new AccountConfig { AccountName = "jump-compatible", ScriptSettings = new() },
                factory, logger, new AccountRuntimeManager(logger), new(), stop.Token);
            if (prepare) await context.PrepareSkillBindingsAsync();
            var keyboard = new RecordingKeyboardInput();
            await using var jump = new CombatJumpAssistSession(context, keyboard, teamFollower: true,
                jumpInterval: TimeSpan.FromSeconds(5), cooldownPollInterval: TimeSpan.FromMilliseconds(5),
                keyHoldDuration: TimeSpan.FromMilliseconds(1));
            await jump.EnterTeamGroupAsync();
            api.Skills = new[] { Skill(104, 7000) };
            await WaitAsync(() => jump.TeamCooldownConfirmed);
            Check(jump.Mode == JumpAssistMode.None && keyboard.Keys.Count == 0 && factory.ExactReads == 0,
                "highest-rank and unprepared/manual cooldown observations preserve the full-list behavior without redundant scoped reads");
        }
    }

    private static SkillSnapshot Skill(uint id, uint end = 0) => new(id, "Heal " + id, 1, 1,
        "Heal", id == 103 ? 3 : 4, false, 10000, end);
    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline) throw new InvalidOperationException("team jump did not observe the released rank cooldown");
            await Task.Delay(5);
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RankReaderFactory(FakeGameApi api) : IRoadhogSnapshotReaderFactory
    {
        private RankSnapshotProxy? proxy;
        public int ExactReads => proxy?.ExactReads ?? 0;
        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken token = default)
        {
            var reader = DispatchProxy.Create<IRoadhogSnapshotReader, RankSnapshotProxy>();
            proxy = (RankSnapshotProxy)(object)reader;
            proxy.Inner = api.Create(config, logger, token);
            return reader;
        }
    }
    public class RankSnapshotProxy : DispatchProxy
    {
        public IRoadhogSnapshotReader Inner { get; set; } = null!;
        public int ExactReads;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != nameof(IRoadhogSnapshotReader.ReadSkillsAsync)) return method.Invoke(Inner, args);
            if (args![0] is not null)
            {
                Interlocked.Increment(ref ExactReads);
                return method.Invoke(Inner, args);
            }
            return HighestAsync((Task<PublishedGameSnapshot<IReadOnlyList<SkillSnapshot>>>)method.Invoke(Inner, args)!);
        }
        private static async Task<PublishedGameSnapshot<IReadOnlyList<SkillSnapshot>>> HighestAsync(
            Task<PublishedGameSnapshot<IReadOnlyList<SkillSnapshot>>> read)
        {
            var published = await read;
            return published with { Value = published.Value.Where(skill => skill.SkillId != 103).ToArray() };
        }
    }
}
