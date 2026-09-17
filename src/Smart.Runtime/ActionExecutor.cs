using Smart.Contracts;
namespace Smart.Runtime;

public sealed class ActionExecutor : IActionExecutor
{
    private readonly IInputDevice _device;
    private readonly IDisposable _lease;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _cancellationSync = new();
    private Task? _cancellation;
    private int _disposed;
    private string? _retainedOwner;
    private int _retainedPriority;
    private bool _quarantined, _released, _deviceDisposed, _cleanupComplete;
    public ActionExecutor(IInputDevice device, InputLeaseRegistry leases, TimeProvider? time = null)
    {
        _device = device; _lease = leases.Acquire(device.DeviceId); _time = time ?? TimeProvider.System;
    }

    public async ValueTask<ActionFeedback> ExecuteAsync(ActionPlan plan, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Validate(plan);
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new(plan.Id, ActionState.Rejected, "Input executor is busy.");
        CancellationDeadline? timeout = null;
        CancellationTokenSource? linked = null;
        var result = new ActionFeedback(plan.Id, ActionState.Succeeded);
        var touched = false;
        var retainedThisAction = false;
        var checkingCondition = false;
        try
        {
            timeout = new(plan.Timeout, _time);
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token, _lifetime.Token);
            result = await RunPlanAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked?.IsCancellationRequested == true || timeout?.Token.IsCancellationRequested == true)
        {
            result = new(plan.Id, cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested ?
                ActionState.Cancelled : ActionState.TimedOut);
        }
        catch (Exception ex) { result = new(plan.Id, ActionState.Failed, ex.Message,
            checkingCondition ? ActionFailureKind.Precondition : ActionFailureKind.Input); }
        finally
        {
            try
            {
                timeout?.Disarm();
                if (result.State == ActionState.Succeeded && timeout?.Token.IsCancellationRequested == true)
                    result = new(plan.Id, cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested ?
                        ActionState.Cancelled : ActionState.TimedOut);
                try
                {
                    if (touched && result.State == ActionState.Succeeded && plan.Retention == InputRetention.UntilOwnerChanges)
                    { _retainedOwner = plan.Owner; _retainedPriority = plan.Priority; retainedThisAction = true; }
                    else if (touched)
                        await ReleaseInputWithDeadlineAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { _quarantined = true; result = new(plan.Id, ActionState.Failed, "Input release failed: " + ex.Message, ActionFailureKind.Input); }
                // Release input before waiting for extension callbacks. Keep the executor gate until
                // those callbacks end, so they cannot run concurrently with the next device owner.
                try { if (timeout is not null) await timeout.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    var detail = "Action deadline cancellation failed: " + ex.Message;
                    // A timer can win the final completion race after the last token check. A
                    // callback fault must not turn retained input into a failed-but-still-held action.
                    if (retainedThisAction)
                    {
                        try { await ReleaseInputWithDeadlineAsync().ConfigureAwait(false); }
                        catch (Exception release) { _quarantined = true; detail += "; Input release failed: " + release.Message; }
                    }
                    if (result.Detail is not null) detail = result.Detail + "; " + detail;
                    result = new(plan.Id, ActionState.Failed, detail,
                        checkingCondition ? ActionFailureKind.Precondition : ActionFailureKind.Input);
                }
            }
            finally
            {
                try { linked?.Dispose(); }
                finally { _gate.Release(); }
            }
        }
        return result;

        void CheckCancellation(CancellationToken token)
        {
            // The deadline token changes immediately; delivery into a linked token is asynchronous.
            timeout!.Token.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
        }
        async ValueTask<ActionFeedback> RunPlanAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_quarantined) return new(plan.Id, ActionState.Rejected, "Input cleanup must complete before reuse.");
            if (_retainedOwner is not null && _retainedOwner != plan.Owner && plan.Priority <= _retainedPriority)
                return new(plan.Id, ActionState.Rejected, "Input remains owned by an equal or higher priority module.");
            if (plan.ExpiresAt is { } expires && _time.GetUtcNow() >= expires)
                return new(plan.Id, ActionState.Rejected, "Action expired.");
            if (plan.Precondition is { } condition)
            {
                checkingCondition = true;
                if (!await condition.EvaluateAsync(token).ConfigureAwait(false))
                    return new(plan.Id, ActionState.Rejected, "Action precondition no longer holds.");
                checkingCondition = false;
            }
            CheckCancellation(token);
            if (plan.ExpiresAt is { } afterValidation && _time.GetUtcNow() >= afterValidation)
                return new(plan.Id, ActionState.Rejected, "Action expired during validation.");
            touched = true;
            if (_retainedOwner is not null && _retainedOwner != plan.Owner)
            {
                await _device.ReleaseAllAsync(token).ConfigureAwait(false);
                _retainedOwner = null;
            }
            foreach (var command in plan.Commands)
            {
                CheckCancellation(token);
                if (command.Operation == InputOperation.Delay)
                    await Task.Delay(command.Duration, _time, token).ConfigureAwait(false);
                else await _device.SendAsync(command, token).ConfigureAwait(false);
            }
            CheckCancellation(token);
            return new(plan.Id, ActionState.Succeeded);
        }
    }

    private async ValueTask ReleaseInputWithDeadlineAsync()
    {
        Exception? releaseError = null;
        try
        {
            await using var cleanup = new CancellationDeadline(TimeSpan.FromSeconds(2), _time);
            try
            {
                await _device.ReleaseAllAsync(cleanup.Token).ConfigureAwait(false);
                _retainedOwner = null;
            }
            catch (Exception ex) { releaseError = ex; }
        }
        catch (Exception ex) when (releaseError is not null)
        { throw new AggregateException("Input release and its deadline cancellation failed.", releaseError, ex); }
        if (releaseError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(releaseError).Throw();
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        // A faulty adapter's cancellation callback must not bypass physical cleanup.
        // All disposers await the same callbacks before allowing another owner to lease the device.
        Task cancellation;
        lock (_cancellationSync) cancellation = _cancellation ??= _lifetime.CancelAsync();
        Exception? cancellationError = null;
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception ex) { cancellationError = ex; }
        await _gate.WaitAsync().ConfigureAwait(false);
        Exception? cleanupError = null;
        try
        {
            if (_cleanupComplete) return;
            await using (var cleanup = new CancellationDeadline(TimeSpan.FromSeconds(2), _time))
            {
                try
                {
                    if (!_released)
                    {
                        await _device.ReleaseAllAsync(cleanup.Token).ConfigureAwait(false);
                        _released = true; _retainedOwner = null;
                    }
                    if (!_deviceDisposed) { await _device.DisposeAsync().ConfigureAwait(false); _deviceDisposed = true; }
                }
                catch (Exception ex) { cleanupError = ex; }
            }
            if (cleanupError is null)
            {
                _lease.Dispose();
                _cleanupComplete = true;
            }
        }
        catch (Exception ex) { cleanupError = cleanupError is null ? ex : new AggregateException("Input cleanup and its deadline cancellation failed.", cleanupError, ex); }
        finally { _gate.Release(); }
        if (cancellationError is not null && cleanupError is not null)
            throw new AggregateException("Input cancellation and cleanup failed.", cancellationError, cleanupError);
        if (cancellationError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancellationError).Throw();
        if (cleanupError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError).Throw();
    }

    public async ValueTask ReleaseOwnerAsync(string owner, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_retainedOwner != owner) return;
            await _device.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
            _retainedOwner = null;
        }
        catch { _quarantined = true; throw; }
        finally { _gate.Release(); }
    }

    private static void Validate(ActionPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Id);
        if (!Enum.IsDefined(plan.Retention) ||
            (plan.Retention == InputRetention.UntilOwnerChanges && string.IsNullOrWhiteSpace(plan.Owner)))
            throw new ArgumentException("Persistent input requires a named owner.");
        if (plan.Timeout <= TimeSpan.Zero || plan.Timeout > TimeSpan.FromMinutes(1) ||
            plan.Commands.IsDefaultOrEmpty || plan.Commands.Length > 128)
            throw new ArgumentException("Action must have 1..128 commands and a timeout of at most one minute.");
        foreach (var command in plan.Commands)
        {
            var required = command.Operation switch
            {
                InputOperation.KeyDown or InputOperation.KeyUp => InputResource.Keyboard,
                InputOperation.MouseDown or InputOperation.MouseUp or InputOperation.MoveRelative or InputOperation.MoveAbsolute or InputOperation.Scroll => InputResource.Mouse,
                InputOperation.Delay => InputResource.None,
                _ => throw new ArgumentException("Unknown input operation.")
            };
            if ((plan.Resources & required) != required) throw new ArgumentException("Missing input resource claim.");
            if (required == InputResource.Keyboard && command.Code is < 0 or > 255)
                throw new ArgumentException("HID code out of range.");
            if (command.Operation is InputOperation.MouseDown or InputOperation.MouseUp &&
                !Enum.IsDefined((MouseButton)command.Code)) throw new ArgumentException("Unknown mouse button.");
            if (command.Duration < TimeSpan.Zero || command.Duration > plan.Timeout)
                throw new ArgumentException("Invalid command duration.");
        }
    }
}
