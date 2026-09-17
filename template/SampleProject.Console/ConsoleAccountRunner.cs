using Smart.Hosting;
namespace SampleProject.ConsoleHost;

public sealed record ConsoleAccountResult(string Id, RuntimeMode Mode, SessionStatus Status, string? Failure);

// Owns execution duration, while the workspace owns account/session disposal.
public static class ConsoleAccountRunner
{
    public static async Task<IReadOnlyList<ConsoleAccountResult>> RunAsync(IReadOnlyList<ManagedAccount> accounts,
        ConsoleRunOptions options, CancellationToken cancellationToken = default,
        Action<ConsoleAccountResult>? reportFailure = null)
    {
        var selected = options.SelectAccounts(accounts);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Duration is { } duration) lifetime.CancelAfter(duration);
        void RecordFailures()
        {
            foreach (var account in selected.Where(x => x.Status.State == SessionState.Faulted))
                if (failures.TryAdd(account.Profile.Id, account.Status.Error ?? "Account faulted."))
                    reportFailure?.Invoke(new(account.Profile.Id, account.Profile.Mode, account.Status, failures[account.Profile.Id]));
        }
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            foreach (var account in selected) account.Start();
            while (true)
            {
                RecordFailures();
                if (selected.All(x => x.Status.State == SessionState.Faulted)) break;
                await Task.Delay(100, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            // User cancellation ends the run, not cleanup: wait for physical input/session release.
            try { RecordFailures(); }
            finally { await Task.WhenAll(selected.Select(x => x.StopAsync(TimeSpan.FromSeconds(10)))).ConfigureAwait(false); }
        }
        return selected.Select(x => new ConsoleAccountResult(x.Profile.Id, x.Profile.Mode, x.Status,
            failures.GetValueOrDefault(x.Profile.Id))).ToArray();
    }
}
