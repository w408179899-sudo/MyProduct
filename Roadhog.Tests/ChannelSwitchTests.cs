using Roadhog.Application.Channels;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;
using Roadhog.Core.Diagnostics;

internal static class ChannelSwitchTests
{
    public static async Task LoadingOutlivesNavigationDeadlineAsync()
    {
        var game = new SimulatedUi { Dialog = true, Selected = 3 };
        var logger = new InMemoryRoadhogLogger();
        var calls = 0;
        game.Api.TransitionReadAsync = async token =>
        {
            if (++calls == 1) return new(false, null, null, DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(46), token);
            return new(true, game.Api.Player, game.Api.Channel, DateTimeOffset.UtcNow);
        };
        var result = await new DataDrivenChannelSwitchExecutor(game.Input, game.Api, logger).ExecuteAsync(
            new FixedChannelSwitchRequest("test", 3, 100, 1, Array.Empty<FixedChannelClickPoint>()) { Config = new AccountConfig() });
        Require(result.Success && game.Submits == 1, "navigation timer must not cancel a slow load after submitting");
        Require(calls == 2, "only one recovery read may be pending");
        foreach (var key in new[] { "W", "A", "S", "D" })
            Require(game.Input.KeyUps.Contains(key), "must release movement before submitting");
    }

    public static async Task SlowAutomaticAttemptAsync()
    {
        var game = new SimulatedUi();
        var logger = new InMemoryRoadhogLogger();
        var firstCheck = true;
        var submittedAt = TimeSpan.Zero;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var handleMouseUp = game.Input.AfterMouseUp;
        game.Input.AfterMouseUp = button =>
        {
            handleMouseUp?.Invoke(button);
            if (game.Submits == 1) submittedAt = elapsed.Elapsed;
        };
        var result = await new DataDrivenChannelSwitchExecutor(game.Input, game.Api, logger).ExecuteAsync(
            new FixedChannelSwitchRequest("test", 3, 100, 1, Array.Empty<FixedChannelClickPoint>())
            {
                Config = new AccountConfig(),
                CanUseMouseAsync = async _ =>
                {
                    if (firstCheck) { firstCheck = false; await Task.Delay(TimeSpan.FromSeconds(16)); }
                    return true;
                }
            });
        Require(result.Success && game.Submits == 1, result.Error ?? "slow automatic attempt must reach move");
        Require(submittedAt > TimeSpan.FromSeconds(15), "must reproduce submission beyond the old deadline");
        Require(elapsed.Elapsed - submittedAt < TimeSpan.FromSeconds(3), "must resume once the new character is ready without fixed ten second sleep");
    }

    public static async Task SequenceAsync()
    {
        var game = new SimulatedUi();
        var result = await game.Run(3);
        Require(result.Success, result.Error ?? "switch failed");
        Require(game.Api.Channel.Number == 3 && game.Submits == 1, "must confirm actual channel with one submit");
        Require(game.Selected == 3, "target dropdown row must be selected");
        Require(game.Input.MouseCommands.Count(c => c.StartsWith("move:")) > 5, "must converge using scaled cursor feedback");
    }

    public static async Task OpenDropdownAsync()
    {
        var game = new SimulatedUi { Dialog = true, Expanded = true, Selected = 2 };
        var result = await game.Run(3);
        Require(result.Success && game.Submits == 1, result.Error ?? "cannot resume open dropdown");
        Require(game.Input.MouseCommands.Count(c => c == "down:Left") == 2, "open dropdown needs only row and move clicks");
    }

    public static async Task AutomaticReusesDialogAsync()
    {
        var game = new SimulatedUi { Dialog = true, Selected = 3, AcceptSubmit = false };
        Require(!(await game.Run(3)).Success, "rejected submission must not be treated as success");
        Require(game.Api.Channel.Number == 1 && game.Dialog, "rejected submit must preserve dialog");
        Require(!(await game.Run(3)).Success, "retry must reuse same dialog and await outcome");
        Require(game.Submits == 2 && game.Input.MouseCommands.Count(c => c == "down:Left") == 2,
            "each automatic attempt should click only move when selection is retained");
    }

    public static async Task AutomaticStopsForCombatAsync()
    {
        var game = new SimulatedUi { Dialog = true, Selected = 3, AcceptSubmit = false };
        var safe = true;
        game.Input.AfterMove = (_, _) => safe = false;
        var logger = new InMemoryRoadhogLogger();
        var result = await new ChannelSwitchSequence(game.Input, logger, TimeSpan.Zero).RunAsync(
            game.Api.Create(new AccountConfig(), logger, CancellationToken.None), "test", 3,
            CancellationToken.None, _ => Task.FromResult(safe));
        Require(!result.Success && game.Submits == 0, "new combat during cursor movement must prevent submit");
    }

    public static async Task NoInputAsync()
    {
        var game = new SimulatedUi();
        Require((await game.Run(1)).Success, "already current should succeed");
        Require(!(await game.Run(4)).Success, "unavailable target must fail");
        Require(game.Input.MouseCommands.Count == 0, "both cases must avoid input");
    }

    public static async Task UnconfirmedAsync()
    {
        var game = new SimulatedUi { Dialog = true, Selected = 3, AcceptSubmit = false };
        var result = await game.Run(3);
        Require(!result.Success && game.Submits == 1 && game.Api.Channel.Number == 1,
            "server rejection must not report success or resubmit");
    }

    public static Task StableSnapshotAsync()
    {
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.ChannelSwitchUi;
        var context = new GameApiReadContext("test", 1, "Aion.bin", "fake");
        var now = DateTimeOffset.Now;
        var snapshot = new SimulatedUi().Snapshot();
        var failed = OperationResult<ChannelSwitchUiSnapshot>.Fail("partial tree");
        Require(!store.Resolve("session1", channel, context, failed, now).Result.Success, "cold start must have no fabricated closed UI");
        store.Resolve("session1", channel, context, OperationResult<ChannelSwitchUiSnapshot>.Ok(snapshot), now);
        Require(ReferenceEquals(snapshot, store.Resolve("session1", channel, context, failed, now.AddMinutes(10)).Result.Value), "failed UI traversal must retain complete last good tree");
        Require(!store.Resolve("session2", channel, context, failed, now).Result.Success, "different session must not inherit geometry");
        var changed = snapshot with { DialogOpen = true };
        Require(ReferenceEquals(changed, store.Resolve("session1", channel, context, OperationResult<ChannelSwitchUiSnapshot>.Ok(changed), now).Result.Value), "success must publish immediately");
        store.ClearSession("session1");
        Require(!store.Resolve("session1", channel, context, failed, now).Result.Success, "session reset must clear geometry");
        return Task.CompletedTask;
    }

    public static async Task CancellationAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var game = new SimulatedUi();
        game.Input.AfterMouseDown = _ => cancellation.Cancel();
        try
        {
            await game.Run(3, cancellation.Token);
            throw new InvalidOperationException("cancellation must stop the sequence");
        }
        catch (OperationCanceledException) { }
        Require(game.Input.MouseCommands.Last() == "up:Left" && game.Submits == 0, "cancelled press must release left mouse without submitting");
    }

    public static async Task<int> ProbeAsync(string[] args)
    {
        string Option(string key) => args.Single(a => a.StartsWith(key, StringComparison.Ordinal))[key.Length..];
        var device = Option("--device=");
        var api = new AionVmmGameApi(new AionVmmGameApiOptions
        {
            DefaultVmmDeviceName = device, MemProcFsHome = Option("--home=")
        }, new InMemoryRoadhogLogger());
        var result = await api.ReadChannelSwitchUiAsync(new GameApiReadContext("channel-ui-probe", 0, "Aion.bin", device));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
    }

    public static Task DecoderFaultsAsync()
    {
        const ulong gameBase = 0x10000000;
        var memory = new Dictionary<ulong, byte[]>
        {
            [gameBase + 0xDACF00] = BitConverter.GetBytes(1024d),
            [gameBase + 0xDACF08] = BitConverter.GetBytes(768d),
            [gameBase + 0xDAD020] = BitConverter.GetBytes(10),
            [gameBase + 0xDAD024] = BitConverter.GetBytes(20)
        };
        foreach (var id in new[] { 17, 376, 377, 227 }) memory[gameBase + 0xD63990 + (ulong)id * 8] = new byte[8];
        byte[] Read(ulong address, int size) => memory[address];
        Require(!new ChannelSwitchUiDecoder(Read).Read(gameBase).DialogOpen, "confirmed null roots should publish closed state");
        var root = gameBase + 0xD63990 + 227 * 8;
        var calls = 0;
        try
        {
            new ChannelSwitchUiDecoder((address, size) => address == root && ++calls == 2 ? BitConverter.GetBytes(0x20000000UL) : Read(address, size)).Read(gameBase);
            throw new InvalidOperationException("changing dialog root must not publish a false closed state");
        }
        catch (InvalidDataException) { }
        memory[root] = new byte[4];
        try
        {
            new ChannelSwitchUiDecoder(Read).Read(gameBase);
            throw new InvalidOperationException("short pointer read must not publish a false closed state");
        }
        catch (InvalidDataException) { }
        return Task.CompletedTask;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class SimulatedUi
    {
        internal FakeGameApi Api = new() { Channel = new ChannelSnapshot(0, 3, 100, DateTimeOffset.Now) };
        internal RecordingKeyboardInput Input = new();
        internal bool Menu, Service, Dialog, Expanded, AcceptSubmit = true;
        internal int Selected = 1, Submits;
        private int _loadingReads;
        private ChannelUiPoint _cursor = new(500, 300);
        private static readonly ChannelUiPoint Start = new(775, 739), ServicePoint = new(800, 646), Switch = new(914, 671), Drop = new(956, 539), Submit = new(899, 573);
        private static ChannelUiPoint Row(int n) => new(895, 558 + (n - 1) * 13);
        private bool At(ChannelUiPoint p) => Math.Abs(_cursor.X - p.X) <= 2 && Math.Abs(_cursor.Y - p.Y) <= 2;
        internal SimulatedUi()
        {
            Api.ChannelUiRead = Snapshot;
            Api.TransitionRead = () => _loadingReads-- > 0
                ? new(false, null, null, DateTimeOffset.UtcNow)
                : new(true, Api.Player, Api.Channel, DateTimeOffset.UtcNow);
            Input.AfterMove = (dx, dy) =>
            {
                _cursor = new(_cursor.X + (int)Math.Round(dx * .75), _cursor.Y + (int)Math.Round(dy * .75));
                if (Menu && At(ServicePoint)) Service = true;
                // A diagonal crossing a different parent row loses the submenu.
                if (Menu && Service && _cursor.X < 874 && _cursor.Y > 655) Service = false;
            };
            Input.AfterMouseUp = button =>
            {
                if (button != RoadhogMouseButton.Left) return;
                if (Dialog)
                {
                    if (Expanded)
                    {
                        for (var n = 1; n <= 3; n++) if (At(Row(n))) { Selected = n; Expanded = false; return; }
                    }
                    else if (At(Drop)) Expanded = true;
                    else if (At(Submit))
                    {
                        Submits++;
                        if (AcceptSubmit) { _loadingReads = 1; Api.Channel = Api.Channel with { Index = Selected - 1 }; Dialog = false; }
                    }
                }
                else if (Service && At(Switch)) { Dialog = true; Menu = false; Service = false; }
                else if (At(Start)) Menu = !Menu;
            };
        }
        internal ChannelSwitchUiSnapshot Snapshot() => new(1024, 768, _cursor, Start,
            Menu ? ServicePoint : null, Service ? Switch : null, Dialog, Expanded, Selected,
            Dialog ? Drop : null, Dialog ? Submit : null,
            Enumerable.Range(1, 3).Select(n => new ChannelUiOption(n, true, Expanded ? Row(n) : null)).ToArray(), DateTimeOffset.Now);
        internal Task<OperationResult> Run(int target, CancellationToken token = default)
        {
            var logger = new InMemoryRoadhogLogger();
            return new ChannelSwitchSequence(Input, logger, TimeSpan.FromMilliseconds(1)).RunAsync(
                Api.Create(new AccountConfig(), logger, token), "test", target, token);
        }
    }
}
