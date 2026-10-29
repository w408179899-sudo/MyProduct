using System.Drawing;
using System.Text.Json;
using Roadhog.Application.Login;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;

internal static class UuLoginCandidatesTests
{
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static JsonElement Fixture(string name) => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AutoLoginTests.Fixtures, name + ".json"))).RootElement.Clone();
    private static JsonElement Page(string name) => Fixture(name).EnumerateArray()
        .Single(page => page.GetProperty("Controls").GetArrayLength() > 100);
    private static JsonElement Window(string hwnd = "0x100", bool visible = true, bool disabled = false,
        string style = "0x94000000") => LoginNativeReader.Json(new
        { Hwnd = hwnd, Visible = visible, Disabled = disabled, Style = style, Manager = "0x1000", ClientRect = new[] { 0, 0, 1000, 688 } });
    private static JsonElement EmptyPage() => LoginNativeReader.Json(new { Controls = Array.Empty<object>() });
    private static LoginObservation? Capture(UuLoginCandidates candidates) => candidates.ReadReconnect() ?? candidates.ReadMain();
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }

    internal static Task ExtraProcessesAsync()
    {
        var ready = Page("11-uu-ui-after-acc");
        foreach (uint[] pids in new uint[][] { [20], [10, 20], [30, 20, 10], [20, 30, 10, 20] })
        {
            var candidates = new UuLoginCandidates(pids,
                pid => pid == 10 ? [] : [Window()],
                (pid, _) => pid == 30 ? throw new IOException("auxiliary UI not ready") : ready, "session", 1);
            var selected = Capture(candidates)!;
            Require(selected.Pid == 20 && selected.Snapshot.Action == LoginAction.LaunchGame,
                "Extra, headless and unreadable UU processes must not block a validated launch page.");
        }
        var failedInventory = new UuLoginCandidates([10, 20],
            pid => pid == 10 ? throw new IOException("temporary process inventory") : [Window()],
            (_, _) => ready, "session", 1);
        Require(Capture(failedInventory)!.Pid == 20, "A separate process read failure cannot invalidate the usable candidate.");
        return Task.CompletedTask;
    }

    internal static Task PageAndWindowPriorityAsync()
    {
        var ready = Page("11-uu-ui-after-acc"); var home = Page("05-uu-ui");
        foreach (uint[] pids in new uint[][] { [10, 20, 30], [30, 20, 10] })
        {
            var candidates = new UuLoginCandidates(pids, _ => [Window()],
                (pid, _) => pid == 10 ? home : ready, "session", 1);
            var selected = Capture(candidates)!;
            Require(selected.Pid == 20 && selected.Snapshot.Action == LoginAction.LaunchGame,
                "Launch-ready beats home; equally usable processes have stable ordering independent of enumeration/focus.");
        }
        var windows = new UuLoginCandidates([20], _ => [Window("0x300"), Window("0x100"), Window("0x200")],
            (_, window) => LoginNativeReader.Text(window,"Hwnd") == "0x100" ? EmptyPage() : ready, "session", 1);
        Require(Capture(windows)!.Window == "0x200", "Multiple main windows select a stable usable page, without Single() rejection.");
        var hidden = new UuLoginCandidates([10, 20], pid => [Window(visible: pid != 10)],
            (pid, _) => { Require(pid == 20, "Hidden window controls cannot authorize input."); return home; }, "session", 1);
        Require(Capture(hidden)!.Snapshot.Action == LoginAction.Accelerate, "A hidden ready page cannot beat a visible home page.");
        return Task.CompletedTask;
    }

    internal static Task ReconnectAcrossProcessesAsync()
    {
        var popup = Fixture("uu-reconnect-40002");
        var candidates = new UuLoginCandidates([30, 10, 20],
            pid => pid == 10 ? [] : [popup.GetProperty("Window")], (_, _) => popup.GetProperty("Ui"), "session", 1);
        var selected = candidates.ReadReconnect()!;
        Require(selected.Pid == 20 && selected.Snapshot.Action == LoginAction.Reaccelerate,
            "Extra processes and a second independent reconnect popup do not suppress recovery.");
        var mixed = new UuLoginCandidates([10, 20], pid => pid == 10 ? [Window()] : [popup.GetProperty("Window")],
            (pid, _) => pid == 10 ? throw new InvalidOperationException("Main UI must not be read before reconnect") : popup.GetProperty("Ui"),
            "session", 1);
        Require(Capture(mixed)!.Pid == 20, "Reconnect retains priority over any main launch window.");
        return Task.CompletedTask;
    }

    internal static Task AbsenceAndReadFaultsAsync()
    {
        Require(Capture(new UuLoginCandidates([], _ => throw new Exception(), (_, _) => throw new Exception(), "session", 1)) is null,
            "Complete process absence permits opening UU.");
        Require(Capture(new UuLoginCandidates([10, 20], _ => [], (_, _) => throw new Exception(), "session", 1)) is null,
            "Complete absence of windows permits restoring UU.");
        Reject(() => Capture(new UuLoginCandidates([10, 20], _ => throw new IOException("window read"),
            (_, _) => EmptyPage(), "session", 1)), "All failed inventories must retry, not claim UU is absent.");
        Reject(() => Capture(new UuLoginCandidates([10, 20], pid => pid == 10 ? [] : [Window()],
            (_, _) => throw new IOException("UI read"), "session", 1)), "A headless process cannot turn a failed main read into absence.");
        Reject(() => Capture(new UuLoginCandidates([10], _ => [Window()],
            (_, _) => JsonDocument.Parse("{}").RootElement.Clone(), "session", 1)), "Malformed UI must remain a provider failure.");
        bool cancelled = false;
        try { Capture(new UuLoginCandidates([10, 20], _ => throw new OperationCanceledException(), (_, _) => EmptyPage(), "session", 1)); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "Cancellation cannot be swallowed as an unusable candidate.");
        return Task.CompletedTask;
    }

    internal static Task PublicationAndTargetBindingAsync()
    {
        var ready = Page("11-uu-ui-after-acc"); bool failed = true; string session = "first";
        using var publisher = new LoginSnapshotPublisher(new("script3", 0, "", "device3", true));
        LoginObservation? Read(Action<LoginObservation>? bind = null) => publisher.Capture(lifecycle =>
        {
            lifecycle(session);
            return Capture(new UuLoginCandidates([10, 20], pid => pid == 10 ? [] : [Window()],
                (_, _) => failed ? throw new IOException("partial page") : ready, session, 1))!;
        }, bind);
        Require(Read() is null, "Cold read failure cannot fabricate a desktop/blocked scene.");
        failed = false; var official = Read()!;
        Require(official.Snapshot.Action == LoginAction.LaunchGame, "First valid page publishes immediately.");
        failed = true; bool bound = false;
        Require(Read(_ => bound = true) == official && !bound, "Held scene cannot bind stale click coordinates.");
        session = "replacement";
        Require(Read() is null, "Process/session replacement invalidates the previous official page.");
        Reject(() => LoginInputTarget.Validate(official, official with { Pid = 30 }), "Process changes must reject old input.");
        Reject(() => LoginInputTarget.Validate(official, official with { Window = "0x200" }), "Window changes must reject old input.");
        Reject(() => LoginInputTarget.Validate(official, official with { Bounds = new Rectangle(1, 1, 1000, 688) }), "Window movement must rebind input.");
        return Task.CompletedTask;
    }

    private sealed class Session : IAutoLoginSession
    {
        private readonly JsonElement _ready = Page("11-uu-ui-after-acc");
        internal readonly List<LoginAction> Actions = [];
        internal bool Released;
        private bool _launched;
        internal LoginObservation Current() => Capture(new UuLoginCandidates([10, 20, 30],
            pid => pid == 10 ? [] : [Window()], (pid, _) => pid == 30 ? throw new IOException("extra UU") : _ready, "session", 1))!;
        public Task<LoginSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(_launched
            ? new LoginSnapshot("session", LoginStage.InWorld, LoginAction.None, "world", "online", "target", 99, 1)
            : Current().Snapshot);
        public async Task<bool> ExecuteAsync(LoginSnapshot expected, LoginAction action, CancellationToken token)
        {
            var observation = Current(); Require(observation.Snapshot == expected, "Use the current selected page.");
            uint foreground = 999;
            await LoginWindowFocus.EnsureAsync(observation.Pid, () => foreground,
                () => { LoginInputTarget.Validate(observation, Current()); return Task.CompletedTask; },
                (_, _) => { foreground = observation.Pid; return Task.CompletedTask; }, _ => Task.CompletedTask, null, token);
            LoginInputTarget.Validate(observation, Current(), compareAction: true);
            Require(foreground == observation.Pid && action == LoginAction.LaunchGame,
                "A background UU is focused and validated without reopening its shortcut.");
            Actions.Add(action); _launched = true; return true;
        }
        public Task ReleaseInputAsync() { Released = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal static async Task BackgroundLaunchFlowAsync()
    {
        await using var session = new Session();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new AutoLoginRunner(new() { PollInterval = TimeSpan.FromMilliseconds(1) })
            .RunAsync(session, "target", null, timeout.Token);
        Require(result.Success && result.Value == 99 && session.Actions.SequenceEqual(new[] { LoginAction.LaunchGame }) && session.Released,
            "Multiple UU processes must complete login using the available background launch page and release input.");
    }
}
