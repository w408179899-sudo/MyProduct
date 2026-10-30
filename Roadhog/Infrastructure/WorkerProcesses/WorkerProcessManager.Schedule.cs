using System.Text.Json;
using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Hardware;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed partial class WorkerProcessManager
{
    private sealed class ScheduleRuntime
    {
        public DailyGameScheduleState State = new();
        public string Signature = "";
        public DateTimeOffset ObservedAt;
        public DateTimeOffset RetryAt;
        public DailyGameScheduleDecision? Pending;
        public DailyGameScheduleAction? Active;
        public Task? Task;
        public CancellationTokenSource? Cancellation;
    }

    private readonly object _scheduleFileSync = new();
    private Dictionary<string, DailyGameScheduleState> _savedSchedules = new(StringComparer.OrdinalIgnoreCase);
    private TimeProvider _scheduleTime = TimeProvider.System;
    private Task? _scheduleMonitor;
    private bool _scheduleStoreInvalid;

    private void LoadScheduleStates()
    {
        var path = Path.Combine(_root, "daily-schedules.json");
        try
        {
            if (File.Exists(path)) _savedSchedules = JsonSerializer.Deserialize<Dictionary<string, DailyGameScheduleState>>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception error)
        {
            _scheduleStoreInvalid = true;
            _logger.Error("schedule.state.invalid", error);
        }
    }

    private void SaveScheduleStates()
    {
        lock (_scheduleFileSync)
        {
            var states = new Dictionary<string, DailyGameScheduleState>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Entries()) lock (item.Sync)
                if (item.Config.DailySchedule.Enabled || item.Schedule.State.ManualResumeAt.HasValue)
                    states[item.Config.InstanceId] = new()
                    {
                        LastOnlineStart = item.Schedule.State.LastOnlineStart, LastOfflineStart = item.Schedule.State.LastOfflineStart,
                        LastRelogin = item.Schedule.State.LastRelogin, ManualResumeAt = item.Schedule.State.ManualResumeAt
                    };
            if (states.Count == 0 && _savedSchedules.Count == 0) return;
            var path = Path.Combine(_root, "daily-schedules.json");
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(states, Json));
            File.Move(temp, path, true);
            _savedSchedules = states;
            _scheduleStoreInvalid = false;
        }
    }

    private void RefreshSchedule(Entry entry, bool created)
    {
        var runtime = entry.Schedule;
        if (created && _savedSchedules.TryGetValue(entry.Config.InstanceId, out var saved)) runtime.State = saved;
        var signature = JsonSerializer.Serialize(entry.Config.DailySchedule, Json);
        if (signature == runtime.Signature) return;
        if (runtime.Signature.Length != 0)
        {
            runtime.State.LastOnlineStart = null; runtime.State.LastOfflineStart = null;
            if (runtime.State.ManualResumeAt > _scheduleTime.GetUtcNow())
                runtime.State.ManualResumeAt = DailyGameSchedule.NextResume(entry.Config.DailySchedule, _scheduleTime.GetUtcNow());
        }
        runtime.Signature = signature;
        runtime.ObservedAt = _scheduleTime.GetUtcNow();
        runtime.Pending = null; runtime.RetryAt = default;
    }

    private void PauseSchedule(Entry entry)
    {
        lock (entry.Sync)
        {
            entry.Schedule.State.ManualResumeAt = DailyGameSchedule.NextResume(entry.Config.DailySchedule, _scheduleTime.GetUtcNow());
            entry.Schedule.Pending = null;
            entry.Schedule.Cancellation?.Cancel();
        }
    }

    private async Task<OperationResult> ManualScheduleCommandAsync(Entry entry, Func<Task<OperationResult>> action, bool pause = false)
    {
        long generation;
        lock (entry.Sync) generation = entry.StopGeneration;
        var result = await action().ConfigureAwait(false);
        if (result.Success)
        {
            lock (entry.Sync) if (entry.StopGeneration == generation)
                entry.Schedule.State.ManualResumeAt = pause
                    ? DailyGameSchedule.NextResume(entry.Config.DailySchedule, _scheduleTime.GetUtcNow()) : null;
            try { await Task.Run(SaveScheduleStates).ConfigureAwait(false); }
            catch (Exception error) { _logger.Error("schedule.state.save_failed", error); }
        }
        return result;
    }

    private bool ScheduleAllowsStart(Entry entry) => !entry.Config.DailySchedule.OnlineEnabled ||
        DailyGameSchedule.Window(entry.Config.DailySchedule, _scheduleTime.GetUtcNow()).Online;

    // Closing a stopped account's settings window releases its idle backend, not the user's schedule.
    public Task<OperationResult> ReleaseIdleWorkerAsync(string instanceId)
    {
        var entry = Get(instanceId);
        lock (entry.Sync) return entry.Desired ? Task.FromResult(OperationResult.Ok()) : BeginStop(entry);
    }

    private async Task ScheduleMonitorAsync()
    {
        var active = new HashSet<Task>();
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                active.RemoveWhere(t => t.IsCompleted);
                if (Volatile.Read(ref _shuttingDown) == 0 && !_scheduleStoreInvalid)
                    foreach (var entry in Entries())
                    {
                        Task? task;
                        lock (entry.Sync) task = TickSchedule(entry);
                        if (task != null) active.Add(task);
                    }
                await Task.Delay(_launch.PollInterval, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { await Task.WhenAll(active).ConfigureAwait(false); }
    }

    // Called under the account lock. Dispatch never runs inline while holding that lock.
    private Task? TickSchedule(Entry entry)
    {
        var runtime = entry.Schedule;
        var settings = entry.Config.DailySchedule;
        var now = _scheduleTime.GetUtcNow();
        var observed = runtime.ObservedAt;
        runtime.ObservedAt = now;
        if (!settings.Enabled || !entry.Config.Enabled || !HardwareVerificationSession.IsCurrent(entry.Config) || runtime.State.ManualResumeAt > now)
            return null;
        var outside = settings.OnlineEnabled && !DailyGameSchedule.Window(settings, now).Online;
        if (runtime.Task is { IsCompleted: false })
        {
            if (!outside || runtime.Active == DailyGameScheduleAction.Exit) return null;
            // An online deadline can interrupt a long login or an in-progress relogin.
            runtime.Cancellation?.Cancel();
        }
        var due = DailyGameSchedule.Decide(settings, runtime.State, observed, now);
        if (due?.Action == DailyGameScheduleAction.Relogin) runtime.Pending = due;
        if (outside) runtime.Pending = null;
        var decision = outside ? due : runtime.Pending ?? due;
        if (decision == null || now < runtime.RetryAt) return null;
        if (entry.StopTask is { IsCompleted: false }) return null;
        var stopFirst = outside && (entry.StartingRequest || runtime.Task is { IsCompleted: false } ||
            entry.Status?.PendingGameSessionOperation == GameSessionOperation.Relogin);
        var stopping = stopFirst ? BeginStop(entry) : Task.FromResult(OperationResult.Ok());
        var generation = entry.StopGeneration;
        var operation = entry.Operation;
        var signature = runtime.Signature;
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        runtime.Cancellation = cancel; runtime.Active = decision.Action;
        runtime.Task = Task.Run(() => RunScheduledAsync(entry, decision, generation, operation, signature, stopping, cancel));
        return runtime.Task;
    }

    private async Task RunScheduledAsync(Entry entry, DailyGameScheduleDecision decision, long generation,
        CancellationTokenSource operation, string signature, Task<OperationResult> stopping, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            var stopped = await stopping.ConfigureAwait(false);
            if (!stopped.Success) throw new InvalidOperationException(stopped.Error);
            token.ThrowIfCancellationRequested();
            lock (entry.Sync)
            {
                if (entry.StopGeneration != generation || entry.Schedule.Signature != signature || entry.Schedule.State.ManualResumeAt > _scheduleTime.GetUtcNow()) return;
                if (decision.Action == DailyGameScheduleAction.Relogin)
                    DailyGameSchedule.Record(entry.Schedule.State, decision); // Persist before dispatch; reopening cannot replay a slot.
            }
            await Task.Run(SaveScheduleStates, token).ConfigureAwait(false);
            var sessionOperation = decision.Action == DailyGameScheduleAction.Start ? (GameSessionOperation?)null :
                decision.Action == DailyGameScheduleAction.Exit ? GameSessionOperation.Exit : GameSessionOperation.Relogin;
            var result = await StartAccountAsync(entry.Config.InstanceId, false, token, null, generation, operation,
                gameSessionOperation: sessionOperation, expectedScheduleSignature: signature).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException(result.Error);
            // The request is accepted, but keep ownership until the manager finishes the game-session handoff.
            while (sessionOperation.HasValue)
            {
                token.ThrowIfCancellationRequested();
                lock (entry.Sync)
                {
                    if (decision.Action == DailyGameScheduleAction.Exit && !entry.Desired && entry.Process == null) break;
                    if (decision.Action == DailyGameScheduleAction.Relogin && !entry.StartingRequest && entry.Desired &&
                        entry.Status is { IsRunning: true, PendingGameSessionOperation: null, GameSessionCompletedRequestId: null }) break;
                    if (entry.State == "failed") throw new InvalidOperationException(entry.Error ?? "定时操作未完成。");
                }
                await Task.Delay(250, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            lock (entry.Sync)
            {
                if (entry.Schedule.Signature != signature) return;
                DailyGameSchedule.Record(entry.Schedule.State, decision);
                if (decision.Action == DailyGameScheduleAction.Relogin && entry.Config.DailySchedule.OnlineEnabled &&
                    DailyGameSchedule.Window(entry.Config.DailySchedule, _scheduleTime.GetUtcNow()) is { Online: true } window)
                    entry.Schedule.State.LastOnlineStart = window.Boundary;
                entry.Schedule.Pending = null; entry.Schedule.RetryAt = default;
            }
            await Task.Run(SaveScheduleStates, token).ConfigureAwait(false);
            _logger.Info("schedule.completed", new Dictionary<string, object?>
                { ["account"] = entry.Config.AccountName, ["action"] = decision.Action.ToString(), ["due"] = decision.Occurrence });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (entry.Sync) if (ReferenceEquals(entry.Schedule.Cancellation, cancellation) && entry.Schedule.Signature == signature)
                entry.Schedule.RetryAt = _scheduleTime.GetUtcNow().AddSeconds(30);
            _logger.Warn("schedule.retry", new Dictionary<string, object?>
                { ["account"] = entry.Config.AccountName, ["action"] = decision.Action.ToString(), ["error"] = error.Message });
        }
        finally
        {
            lock (entry.Sync) if (ReferenceEquals(entry.Schedule.Cancellation, cancellation)) entry.Schedule.Cancellation = null;
            cancellation.Dispose();
        }
    }
}
