using Smart.Hosting;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class SessionTests
{
    private sealed class Factory : IRuntimeSessionFactory
    {
        private readonly MockSessionFactory _inner = new(new InputLeaseRegistry());
        public int Opens, Disposals;
        public bool Replace;
        public int OpenFailures;
        public int DisposeFailures;
        public async ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token)
        {
            if (Interlocked.Decrement(ref OpenFailures) >= 0) throw new IOException("offline");
            var id = Interlocked.Increment(ref Opens);
            return new Session(this, await _inner.OpenAsync(profile, token), id);
        }
        private sealed class Session(Factory factory, IRuntimeSession inner, int id) : IRuntimeSession
        {
            public AccountWorker Worker => inner.Worker;
            public void Invalidate() => inner.Invalidate();
            public ValueTask<bool> IsCurrentAsync(CancellationToken token) => ValueTask.FromResult(!(factory.Replace && id == 1));
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Decrement(ref factory.DisposeFailures) >= 0) throw new IOException("cleanup failed");
                await inner.DisposeAsync(); Interlocked.Increment(ref factory.Disposals);
            }
        }
    }
    internal static async Task Until(Func<bool> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!check()) await Task.Delay(10, deadline.Token);
    }
    [Fact] public async Task ReplacementDisposesOldScopeBeforeOpeningNewGeneration()
    {
        var factory = new Factory();
        await using var account = new ManagedAccount(new("a", ProbeIntervalMs: 100, RetryDelayMs: 100), factory);
        account.Start(); await Until(() => account.Status.State == SessionState.Running);
        factory.Replace = true;
        await Until(() => account.Status.State == SessionState.Running && account.Status.Generation == 2);
        Assert.Equal(1, factory.Disposals);
        await account.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, factory.Disposals); Assert.Equal(SessionState.Stopped, account.Status.State);
    }
    [Fact] public async Task ConnectionFailureRetriesAndPauseResumeCreatesFreshScope()
    {
        var factory = new Factory { OpenFailures = 1 };
        await using var account = new ManagedAccount(new("a", ProbeIntervalMs: 100, RetryDelayMs: 100), factory);
        account.Start(); account.Start();
        await Until(() => account.Status.State == SessionState.Running);
        await account.PauseAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SessionState.Paused, account.Status.State);
        account.Start(); await Until(() => account.Status.Generation == 2);
        await account.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, factory.Opens);
    }
    [Fact] public async Task FailedCleanupRetainsScopeAndCanRetryBeforeRestart()
    {
        var factory = new Factory { DisposeFailures = 1, Replace = true };
        await using var account = new ManagedAccount(new("a", ProbeIntervalMs: 100, RetryDelayMs: 100), factory);
        account.Start(); await Until(() => account.Status.State == SessionState.Faulted);
        Assert.Throws<InvalidOperationException>(account.Start);
        await account.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, factory.Disposals);
        account.Start(); await Until(() => account.Status.State == SessionState.Running);
        await account.StopAsync(TimeSpan.FromSeconds(2));
    }
    [Fact] public async Task AccountWorkspacePersistsIndependentProfilesAndRejectsEditingRunningAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-workspace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigStore<HostSettings>(Path.Combine(root, "accounts.json"), 1, x => x.Validate());
            await using var workspace = new AccountWorkspace(store, new MockSessionFactory(new()));
            await workspace.SaveAsync(new([new("one"), new("two")]));
            workspace.Accounts[0].Start(); await Until(() => workspace.Accounts[0].Status.State == SessionState.Running);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SaveAsync(HostSettings.Empty));
            Assert.Equal(2, (await store.LoadAsync()).Accounts.Length);
            await workspace.StopAllAsync(TimeSpan.FromSeconds(2));
            await workspace.SaveAsync(new([new("two")]));
            Assert.Equal("two", Assert.Single(workspace.Accounts).Profile.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task FailedWorkspaceCleanupDoesNotCommitNewFileOrDisposeUnchangedAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-atomic-workspace-" + Guid.NewGuid().ToString("N"));
        var factory = new Factory { DisposeFailures = 10, Replace = true };
        try
        {
            var store = new JsonConfigStore<HostSettings>(Path.Combine(root, "accounts.json"), 1, x => x.Validate());
            await using var workspace = new AccountWorkspace(store, factory);
            await workspace.SaveAsync(new([new("one", ProbeIntervalMs: 100), new("two", ProbeIntervalMs: 100)]));
            var unchanged = workspace.Accounts[0];
            workspace.Accounts[1].Start(); await Until(() => workspace.Accounts[1].Status.State == SessionState.Faulted);
            try
            {
                await Assert.ThrowsAsync<IOException>(() => workspace.SaveAsync(HostSettings.Empty));
                Assert.Equal(2, (await store.LoadAsync()).Accounts.Length);
                Assert.Same(unchanged, workspace.Accounts[0]);
                unchanged.Start(); await Until(() => unchanged.Status.State == SessionState.Running);
            }
            finally { factory.DisposeFailures = 0; await workspace.StopAllAsync(TimeSpan.FromSeconds(2)); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task ConcurrentAndRepeatedDisposalClosesScopeExactlyOnceAndStopRemainsIdempotent()
    {
        var factory = new Factory(); var account = new ManagedAccount(new("a"), factory);
        account.Start(); await Until(() => account.Status.State == SessionState.Running);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => account.DisposeAsync().AsTask()));
        await account.DisposeAsync(); await account.StopAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, factory.Disposals); Assert.Equal(SessionState.Stopped, account.Status.State);
        Assert.Throws<ObjectDisposedException>(account.Start);
    }
}
