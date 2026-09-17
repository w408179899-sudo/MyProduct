namespace Smart.Hosting;

// Host operations serialize profile changes. Running accounts must stop before replacement/removal.
public sealed class AccountWorkspace(JsonConfigStore<HostSettings> store, IRuntimeSessionFactory factory,
    Smart.Contracts.IEventSink? events = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ManagedAccount[] _accounts = [];
    private bool _disposeStarted, _disposeComplete;
    public IReadOnlyList<ManagedAccount> Accounts => Array.AsReadOnly(Volatile.Read(ref _accounts));
    public async Task LoadAsync(CancellationToken token = default) => await ReplaceAsync(await store.LoadAsync(token).ConfigureAwait(false), false, token).ConfigureAwait(false);
    public Task SaveAsync(HostSettings settings, CancellationToken token = default) => ReplaceAsync(settings, true, token);
    private async Task ReplaceAsync(HostSettings settings, bool save, CancellationToken token)
    {
        settings.Validate();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var holds = new List<IDisposable>();
        try
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
            var previous = _accounts;
            foreach (var account in previous) holds.Add(account.HoldConfiguration());
            var replacement = settings.Accounts.Select(x => new ManagedAccount(x, factory, events)).ToArray();
            // Preparation may fail. Existing accounts stay restartable and the persisted document stays unchanged.
            foreach (var account in previous) await account.StopAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            if (save) await store.SaveAsync(settings, token).ConfigureAwait(false);
            Volatile.Write(ref _accounts, replacement);
            foreach (var account in previous) await account.DisposeAsync().ConfigureAwait(false);
        }
        finally { foreach (var hold in holds) hold.Dispose(); _gate.Release(); }
    }
    public Task StopAllAsync(TimeSpan timeout, CancellationToken token = default) =>
        Task.WhenAll(Accounts.Select(x => x.StopAsync(timeout, token)));
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposeComplete) return;
            _disposeStarted = true;
            List<Exception>? errors = null;
            foreach (var account in _accounts)
                try { await account.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { (errors ??= []).Add(ex); }
            // Accounts can retain owned native work after a failed stop. Their shared factory must outlive it.
            if (errors is not null) throw new AggregateException(errors);
            await factory.DisposeAsync().ConfigureAwait(false);
            Volatile.Write(ref _accounts, []);
            _disposeComplete = true;
        }
        finally { _gate.Release(); }
    }
}
