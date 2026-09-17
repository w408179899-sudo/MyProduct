using Smart.Contracts;
namespace Smart.Runtime;

public sealed record WorkerOptions(TimeSpan PollInterval, TimeSpan DecisionBudget, int OperationBudget = 4096)
{
    public static WorkerOptions Default { get; } = new(TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(5));
}
public sealed record WorkerMetrics(long Ticks, long BudgetOverruns, long Actions);
public sealed class InputExecutionException(string message) : IOException(message);

// One pending tick per module. Pending async reads never block the other modules.
// Synchronous computation must cooperate with WorkBudget and bounded input sizes.
public sealed class AccountWorker
{
    private sealed class Slot(IAccountModule module, ISnapshotReader snapshots)
    {
        public IAccountModule Module { get; } = module;
        public TimeSpan Due;
        public ActionFeedback? Feedback;
        public Task<ModuleResult>? Pending;
        public WorkBudget? Budget;
        public ISnapshotReader Reader { get; } = new ModuleSnapshotReader(snapshots, module);
    }
    private sealed record Active(Slot Slot, ActionPlan Plan, CancellationTokenSource Stop, Task<ActionFeedback> Task);
    private readonly string _account;
    private readonly ISnapshotReader _snapshots;
    private readonly IActionExecutor _executor;
    private readonly Slot[] _slots;
    private readonly IReadOnlyList<IAccountModule> _initialization;
    private readonly WorkerOptions _options;
    private readonly TimeProvider _time;
    private readonly IEventSink _events;
    private readonly Guid _runId;
    private Active? _active;
    private long _ticks, _overruns, _actions;
    private int _running;
    private int _inputCleanupComplete;
    internal bool InputCleanupComplete => Volatile.Read(ref _inputCleanupComplete) != 0;
    public AccountWorker(string account, ISnapshotReader snapshots, IActionExecutor executor,
        IEnumerable<IAccountModule> modules, WorkerOptions? options = null, TimeProvider? time = null,
        IEventSink? events = null, IReadOnlyCollection<string>? registeredChannels = null, Guid? runId = null)
    {
        _account = account; _snapshots = snapshots; _executor = executor; _runId = runId ?? Guid.NewGuid();
        _initialization = ModuleCatalog.Validate(modules, registeredChannels ?? Array.Empty<string>());
        _slots = _initialization.OrderByDescending(x => x.Priority).ThenBy(x => x.Id, StringComparer.Ordinal).Select(x => new Slot(x, snapshots)).ToArray();
        _options = options ?? WorkerOptions.Default;
        if (_options.PollInterval <= TimeSpan.Zero || _options.DecisionBudget <= TimeSpan.Zero || _options.OperationBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        _time = time ?? TimeProvider.System; _events = events ?? NullEventSink.Instance;
    }
    public WorkerMetrics Metrics => new(Interlocked.Read(ref _ticks), Interlocked.Read(ref _overruns), Interlocked.Read(ref _actions));
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) throw new InvalidOperationException("Worker is single-use.");
        var started = _time.GetTimestamp();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var initialized = new List<IAccountModule>();
        var reason = ModuleStopReason.Stopped;
        Exception? runError = null;
        try
        {
            foreach (var module in _initialization)
            {
                initialized.Add(module);
                await module.InitializeAsync(new(_account, _slots.Single(x => x.Module == module).Reader, _runId), lifetime.Token).ConfigureAwait(false);
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_active?.Task.IsCompleted == true) await FinishActiveAsync().ConfigureAwait(false);
                foreach (var slot in _slots)
                {
                    if (slot.Pending is null && slot.Due <= _time.GetElapsedTime(started))
                    {
                        slot.Budget = new(_options.OperationBudget, _options.DecisionBudget, _time);
                        slot.Pending = slot.Module.TickAsync(new(_account, new BudgetedSnapshotReader(slot.Reader, slot.Budget),
                            _time.GetElapsedTime(started), slot.Feedback, slot.Budget), lifetime.Token).AsTask();
                    }
                    if (slot.Pending is not { IsCompleted: true } pending) continue;
                    var result = await pending.ConfigureAwait(false);
                    slot.Pending = null;
                    if (slot.Budget!.IsExpired) Interlocked.Increment(ref _overruns);
                    if (result.NextRunIn <= TimeSpan.Zero) throw new InvalidOperationException("Module delay must be positive: " + slot.Module.Id);
                    slot.Due = _time.GetElapsedTime(started) + result.NextRunIn;
                    if (result.ReleaseInput)
                    {
                        if (_active?.Slot == slot) await CancelActiveAsync().ConfigureAwait(false);
                        await _executor.ReleaseOwnerAsync(slot.Module.Id, lifetime.Token).ConfigureAwait(false);
                    }
                    if (result.Action is { } plan) await SubmitAsync(slot, plan, lifetime.Token).ConfigureAwait(false);
                }
                Interlocked.Increment(ref _ticks);
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var due = _slots.Where(x => x.Pending is null).Select(x => x.Due).DefaultIfEmpty(_time.GetElapsedTime(started) + TimeSpan.FromSeconds(1)).Min();
                var wait = due - _time.GetElapsedTime(started);
                if (wait < _options.PollInterval) wait = _options.PollInterval;
                if (wait > TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
                var delay = Task.Delay(wait, _time, wake.Token);
                var tasks = _slots.Where(x => x.Pending is not null).Select(x => (Task)x.Pending!).Append(delay);
                if (_active is not null) tasks = tasks.Append(_active.Task);
                await Task.WhenAny(tasks).ConfigureAwait(false);
                wake.Cancel();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { reason = ModuleStopReason.Faulted; runError = ex; throw; }
        finally
        {
            // Signal first, but never let extension cancellation callbacks skip input or module cleanup.
            var cancellation = lifetime.CancelAsync();
            List<Exception>? errors = null;
            try
            {
                if (_active is not null) await CancelActiveAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { await CleanupInputAsync().ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { await cancellation.ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            foreach (var slot in _slots)
                if (slot.Pending is { } task) { try { await task.ConfigureAwait(false); } catch { } }
            foreach (var module in initialized.AsEnumerable().Reverse())
            {
                try
                {
                    await using var deadline = new CancellationDeadline(TimeSpan.FromSeconds(2), _time);
                    try { await module.StopAsync(reason, deadline.Token).ConfigureAwait(false); }
                    catch (Exception ex) { (errors ??= []).Add(ex); }
                }
                catch (Exception ex) { (errors ??= []).Add(ex); }
            }
            if (errors is not null)
            {
                if (runError is not null) errors.Insert(0, runError);
                throw new AggregateException("Worker cleanup failed.", errors);
            }
        }
    }
    private async Task SubmitAsync(Slot slot, ActionPlan proposed, CancellationToken token)
    {
        var plan = proposed with { Owner = slot.Module.Id, Priority = slot.Module.Priority };
        if (_active is not null && slot.Module.Priority <= _active.Slot.Module.Priority)
        {
            if (_active.Slot != slot || _active.Plan.Id != plan.Id)
                slot.Feedback = new(plan.Id, ActionState.Rejected, "A higher or equal priority action is running.");
            return;
        }
        if (_active is not null) await CancelActiveAsync().ConfigureAwait(false);
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        slot.Feedback = new(plan.Id, ActionState.Running);
        _active = new(slot, plan, stop, _executor.ExecuteAsync(plan, stop.Token).AsTask());
        Interlocked.Increment(ref _actions);
        Emit("action.started", slot.Module.Id, plan.Id, System.Text.Json.JsonSerializer.Serialize(new
        { plan.Resources, plan.Commands, plan.Timeout, plan.Retention, plan.Priority, plan.ExpiresAt }));
    }
    private async Task CancelActiveAsync()
    {
        Exception? cancellationError = null;
        try { await _active!.Stop.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) { cancellationError = ex; }
        try { await FinishActiveAsync().ConfigureAwait(false); }
        catch (Exception ex) when (cancellationError is not null)
        { throw new AggregateException("Action cancellation and completion failed.", cancellationError, ex); }
        if (cancellationError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancellationError).Throw();
    }
    private async Task FinishActiveAsync()
    {
        var active = _active!;
        try
        {
            try { active.Slot.Feedback = await active.Task.ConfigureAwait(false); }
            catch (OperationCanceledException) when (active.Stop.IsCancellationRequested)
            { active.Slot.Feedback = new(active.Plan.Id, ActionState.Cancelled); }
            Emit("action.completed", active.Slot.Module.Id, active.Plan.Id, System.Text.Json.JsonSerializer.Serialize(active.Slot.Feedback));
            if (active.Slot.Feedback.State == ActionState.Failed)
            {
                if (active.Slot.Feedback.Failure == ActionFailureKind.Precondition)
                    throw new InvalidOperationException("Action precondition failed: " + active.Slot.Feedback.Detail);
                throw new InputExecutionException(active.Slot.Feedback.Detail ?? "Input action failed.");
            }
        }
        finally { active.Stop.Dispose(); _active = null; }
    }
    public async Task CleanupInputAsync()
    {
        if (InputCleanupComplete) return;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _executor.DisposeAsync().ConfigureAwait(false);
                Volatile.Write(ref _inputCleanupComplete, 1);
                return;
            }
            catch when (attempt < 2) { await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false); }
        }
    }
    private void Emit(string name, string module, string action, string detail)
    {
        try { _events.Write(new(_time.GetUtcNow(), name, _account, detail, _runId.ToString("N"), module, action)); } catch { }
    }
}
