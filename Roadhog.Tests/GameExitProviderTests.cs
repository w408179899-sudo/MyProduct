using System.Drawing;
using System.Text;
using Roadhog.Application.GameSessions;
using Roadhog.Infrastructure.GameSessions;
using Roadhog.Infrastructure.Vmm;

internal static class GameExitProviderTests
{
    private const ulong Base = 0x180000000, Menu = 0x20000, Countdown = 0x30000;

    public static Task GeometryAndCancelAsync()
    {
        var memory = new Memory();
        memory.Widget(Menu, "runtime_menu", 3, 100, 80, 300, 400, Base + 0x7FCE80);
        memory.Widget(0x21000, "quit_game", 3, 20, 30, 100, 24);
        memory.Widget(0x22000, "goto_select", 3, 20, 80, 100, 24);
        memory.Children(Menu, 0x21000, 0x22000);
        var ui = memory.Decode();
        Require(ui.Quit == new Roadhog.Core.Model.GameUiPoint(170, 122), "Quit uses current menu geometry, not character selection.");
        Require(!ui.CountdownOpen && ui.Cancel == null, "A closed countdown cannot authorize cancel.");

        memory.Widget(Countdown, "runtime_countdown", 3, 400, 300, 250, 100, Base + 0x7E9748);
        memory.Widget(0x31000, "cancel", 3, 200, 0, 16, 16);
        memory.Widget(0x32000, "cancel", 3, 80, 60, 90, 24);
        memory.Children(Countdown, 0x31000, 0x32000);
        ui = memory.Decode();
        Require(ui.Quit == null && ui.Cancel == new Roadhog.Core.Model.GameUiPoint(525, 372),
            "Countdown owns input; duplicate close/body cancel controls select one stable validated target.");
        memory.Put(0x32000 + 0x28, BitConverter.GetBytes(1UL));
        Require(memory.Decode().Cancel == new Roadhog.Core.Model.GameUiPoint(608, 308), "A disabled body button cannot be clicked; the valid close button remains usable.");
        return Task.CompletedTask;
    }

    public static Task WrongClassAndModalAsync()
    {
        var memory = new Memory();
        memory.Widget(Menu, "anything", 3, 10, 20, 300, 400, Base + 0x7FCE80);
        memory.Widget(0x21000, "quit_game", 3, 20, 30, 100, 24);
        memory.Children(Menu, 0x21000);
        memory.Put(Base + 0xD63990 + 336 * 8, BitConverter.GetBytes(0x40000UL));
        memory.Widget(0x40000, "foreign_modal", 3, 0, 0, 100, 100);
        var ui = memory.Decode();
        Require(ui.OtherModalOpen && ui.Quit == null, "A foreign modal prevents click-through.");
        memory.Put(Menu, BitConverter.GetBytes(Base + 0x7E9748));
        var rejected = false;
        try { memory.Decode(); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "A reused slot with the wrong class is rejected before selecting a control.");
        return Task.CompletedTask;
    }

    public static Task PublicationLifecycleAndBindingAsync()
    {
        using var publisher = new GameExitSnapshotPublisher(new("test", 1, "Aion.bin", "fake"));
        var first = Observation("old", new(100, 100, 800, 600));
        var bindings = 0;
        var ready = publisher.Capture(life => { life("old"); return first; }, _ => bindings++);
        Require(ready == first && bindings == 1, "A complete capture publishes and binds its input target.");
        var held = publisher.Capture(life => { life("old"); throw new InvalidDataException("short UI read"); }, _ => bindings++);
        Require(held == first && bindings == 1, "Failed reads retain the official snapshot but cannot bind old geometry.");
        var cold = publisher.Capture(life => { life("new"); throw new InvalidDataException("new process pending"); }, _ => bindings++);
        Require(cold == null && bindings == 1, "A new process lifecycle cannot inherit the old official scene.");
        var absent = first with { Snapshot = new("gone", 0, false, false, false, false, false, 0, false) };
        var completed = publisher.Capture(life => { life("gone"); return absent; });
        Require(completed?.Snapshot.ProcessPresent == false, "A validated absence is official data, not a read failure.");
        return Task.CompletedTask;
    }

    public static Task InputBindingGuardsAsync()
    {
        var original = Observation("session", new(100, 100, 800, 600));
        Require(VmmGameExitSession.SameTarget(original, original, GameExitAction.Quit), "The original validated target is executable.");
        Require(!VmmGameExitSession.SameTarget(original, original with { Bounds = new(101, 100, 800, 600) }, GameExitAction.Quit),
            "Moving the game window invalidates old screen coordinates.");
        Require(!VmmGameExitSession.SameTarget(original, original with { Snapshot = original.Snapshot with { Session = "new" } }, GameExitAction.Quit),
            "PID reuse or module replacement invalidates a pending click.");
        Require(!VmmGameExitSession.SameTarget(original, original with { Snapshot = original.Snapshot with { CurrentHp = 99 } }, GameExitAction.Quit),
            "Incoming damage invalidates an exit click before input.");
        var cancel = original with { Snapshot = original.Snapshot with { CountdownOpen = true, CanCancel = true },
            Ui = original.Ui with { CountdownOpen = true, Cancel = new(10, 10), Quit = null } };
        Require(VmmGameExitSession.SameTarget(cancel, cancel with { Snapshot = cancel.Snapshot with { CurrentHp = 50 } }, GameExitAction.Cancel),
            "Incoming damage must not prevent cancellation of the exit countdown.");
        return Task.CompletedTask;
    }

    public static async Task TimeoutDrainsBeforeRetryAsync()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = 0; var calls = 0;
        var executor = new GameExitOperationExecutor(TimeSpan.FromMilliseconds(100), () =>
        { releases++; return Task.CompletedTask; });
        var timedOut = executor.RunTaskAsync(_ => { calls++; entered.TrySetResult(); return pending.Task; }, "test", CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await ExpectTimeoutAsync(timedOut);
        await ExpectTimeoutAsync(executor.RunTaskAsync(_ => { calls++; return Task.FromResult(2); }, "test", CancellationToken.None));
        Require(calls == 1 && releases == 0, "A timed-out operation must finish before any retry or release-after-drain callback.");
        pending.SetResult(1);
        await executor.DrainCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        var value = await executor.RunTaskAsync(_ => { calls++; return Task.FromResult(2); }, "test", CancellationToken.None);
        Require(value == 2 && calls == 2 && releases == 1, "The executor recovers after the old task drains and releases input.");
        executor.Close(() => { });
    }

    public static async Task LateCaptureCannotPublishAsync()
    {
        using var publisher = new GameExitSnapshotPublisher(new("test", 1, "Aion.bin", "fake"));
        var first = Observation("old", new(100, 100, 800, 600));
        publisher.Capture(life => { life("old"); return first; });
        var executor = new GameExitOperationExecutor(TimeSpan.FromMilliseconds(100));
        using var blocked = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bindings = 0;
        var late = first with { Snapshot = first.Snapshot with { CurrentHp = 1 } };
        var read = executor.RunAsync(token => publisher.Capture(life =>
        {
            life("old"); entered.TrySetResult(); blocked.Wait(); return late;
        }, _ => bindings++, token), "test", CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await ExpectTimeoutAsync(read);
            Require(publisher.ReadPublished() == first, "The official store remains readable while a raw capture is still blocked.");
        }
        finally { blocked.Set(); }
        await executor.DrainCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Require(publisher.ReadPublished() == first && bindings == 0, "A late cancelled capture must neither publish nor bind physical input.");
        var resumed = await executor.RunAsync(token => publisher.Capture(life => { life("old"); return late; }, _ => bindings++, token),
            "test", CancellationToken.None);
        Require(resumed == late && bindings == 1, "A new valid capture can publish after retirement without replacing the account session.");
        executor.Close(() => { });
    }

    public static async Task CancelledInputCannotResumeAsync()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        var executor = new GameExitOperationExecutor(TimeSpan.FromMilliseconds(100));
        var action = executor.RunTaskAsync(async token =>
        {
            entered.TrySetResult();
            await pending.Task;
            token.ThrowIfCancellationRequested();
            sends++;
            return true;
        }, "input", CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await ExpectTimeoutAsync(action);
        pending.SetResult();
        await executor.DrainCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Require(sends == 0, "An expired input operation cannot continue sending after its awaited work eventually returns.");
        executor.Close(() => { });
        var disposed = false;
        try { await executor.RunTaskAsync(_ => { sends++; return Task.FromResult(true); }, "input", CancellationToken.None); }
        catch (ObjectDisposedException) { disposed = true; }
        Require(disposed && sends == 0, "Explicit disposal remains permanent even after a timed-out operation drains.");

        using var stop = new CancellationTokenSource();
        var stopped = new GameExitOperationExecutor(TimeSpan.FromSeconds(30));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = stopped.RunTaskAsync(async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return true; }, "stop", stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();
        var cancelled = false;
        try { await waiting.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "User Stop cancels promptly without waiting for the ordinary operation deadline.");
        stopped.Close(() => { });
    }

    public static Task ModuleUnloadIsNotProcessExitAsync()
    {
        using var publisher = new GameExitSnapshotPublisher(new("test", 1, "Aion.bin", "fake"));
        var before = Observation("process:module", new(100, 100, 800, 600));
        publisher.Capture(life => { life("process:module"); return before; });
        var unloading = publisher.Capture(life =>
        { life("process:0"); throw new InvalidOperationException("等待游戏模块完成退出。"); });
        Require(unloading == null && publisher.ReadPublished() == null,
            "A missing module invalidates the old scene but cannot publish ProcessPresent=false while the process still exists.");
        var absent = before with { Snapshot = new("gone", 0, false, false, false, false, false, 0, false) };
        Require(publisher.Capture(life => { life("gone"); return absent; })?.Snapshot.ProcessPresent == false,
            "Only the separate confirmed process-absence capture can complete exit.");
        return Task.CompletedTask;
    }

    private static async Task ExpectTimeoutAsync(Task task)
    {
        var timedOut = false;
        try { await task; } catch (TimeoutException) { timedOut = true; }
        Require(timedOut, "The test operation must report its bounded timeout.");
    }

    private static GameExitObservation Observation(string session, Rectangle bounds) => new(
        new(session, 100, true, true, false, true, false, 100, false), 200, "0x100", bounds,
        new(800, 600, true, false, false, Menu, Countdown, new(50, 50), null));

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Memory
    {
        private readonly Dictionary<ulong, byte> _bytes = [];
        private ulong _next = 0x50000;
        internal Memory()
        {
            Put(Base + 0xDACF00, BitConverter.GetBytes(800D).Concat(BitConverter.GetBytes(600D)).ToArray());
            Put(Base + 0xD63990 + 30 * 8, BitConverter.GetBytes(Menu));
            Put(Base + 0xD63990 + 215 * 8, BitConverter.GetBytes(Countdown));
        }
        internal void Put(ulong address, byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++) _bytes[address + (ulong)i] = bytes[i];
        }
        internal void Widget(ulong address, string name, ulong flags, double x, double y, double width, double height, ulong vtable = 0)
        {
            Put(address, BitConverter.GetBytes(vtable));
            var nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length < 16) Put(address + 8, nameBytes);
            else { var text = _next; _next += 0x100; Put(text, nameBytes); Put(address + 8, BitConverter.GetBytes(text)); }
            Put(address + 24, BitConverter.GetBytes((ulong)nameBytes.Length));
            Put(address + 32, BitConverter.GetBytes((ulong)Math.Max(15, nameBytes.Length)));
            Put(address + 0x28, BitConverter.GetBytes(flags));
            Put(address + 0x58, new[] { x, y, width, height, 0D, 0D, 0D, 0D }.SelectMany(BitConverter.GetBytes).ToArray());
        }
        internal void Children(ulong parent, params ulong[] children)
        {
            var head = _next; _next += 0x100;
            var links = children.Select(_ => { var at = _next; _next += 0x100; return at; }).ToArray();
            Put(parent + 0x238, BitConverter.GetBytes(head));
            Put(head, BitConverter.GetBytes(links.Length == 0 ? head : links[0]));
            for (var i = 0; i < links.Length; i++)
            {
                Put(links[i], BitConverter.GetBytes(i + 1 < links.Length ? links[i + 1] : head));
                Put(links[i] + 0x10, BitConverter.GetBytes(children[i]));
            }
        }
        internal GameExitUi Decode() => new InventoryInteractionDecoder((address, count) =>
            Enumerable.Range(0, count).Select(i => _bytes.GetValueOrDefault(address + (ulong)i)).ToArray()).ReadGameExit(Base);
    }
}
