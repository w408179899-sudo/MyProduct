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
    public Task SaveAccountAsync(AccountProfile profile, string? previousId = null, CancellationToken token = default) =>
        ChangeAccountAsync(previousId, profile, token);
    public Task DeleteAccountAsync(string id, CancellationToken token = default) => ChangeAccountAsync(id, null, token);
    private async Task ChangeAccountAsync(string? previousId, AccountProfile? profile, CancellationToken token)
    {
        profile?.Validate();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
            var previous = _accounts;
            var index = previousId is null ? -1 : Array.FindIndex(previous, x => x.Profile.Id == previousId);
            if (previousId is not null && index < 0) throw new ArgumentException("The account no longer exists.");
            if (profile is not null && previous.Where((_, i) => i != index).Any(x => x.Profile.Id == profile.Id))
                throw new ArgumentException("Account ID already exists.");
            var old = index < 0 ? null : previous[index];
            using var hold = old?.HoldConfiguration();
            var profiles = previous.Where((_, i) => i != index).Select(x => x.Profile).ToList();
            if (profile is not null) profiles.Insert(index < 0 ? profiles.Count : index, profile);
            var settings = new HostSettings(System.Collections.Immutable.ImmutableArray.CreateRange(profiles));
            settings.Validate();
            if (old is not null) await old.StopAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            await store.SaveAsync(settings, token).ConfigureAwait(false);
            var replacement = previous.Where((_, i) => i != index).ToList();
            if (profile is not null) replacement.Insert(index < 0 ? replacement.Count : index, new(profile, factory, events));
            Volatile.Write(ref _accounts, replacement.ToArray());
            if (old is not null) await old.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
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
