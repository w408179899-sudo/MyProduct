using Smart.Hosting;
using Xunit;

namespace Smart.Runtime.Tests;

public sealed class HostingShutdownTests
{
    private sealed class Factory : IRuntimeSessionFactory
    {
        private readonly MockSessionFactory _inner = new(new InputLeaseRegistry());
        public bool FailCleanup, ThrowOnCancellation, ThrowOnInvalidation, Replace;
        public int SessionDisposals, FactoryDisposals;
        public TaskCompletionSource Probed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? CleanupGate;
        public async ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token) =>
            new Session(this, await _inner.OpenAsync(profile, token));
        public ValueTask DisposeAsync() { Interlocked.Increment(ref FactoryDisposals); return ValueTask.CompletedTask; }

        private sealed class Session(Factory owner, IRuntimeSession inner) : IRuntimeSession
        {
            private bool _registered;
            public AccountWorker Worker => inner.Worker;
            public void Invalidate()
            {
                inner.Invalidate();
                if (owner.ThrowOnInvalidation) throw new IOException("invalidator failed");
            }
            public ValueTask<bool> IsCurrentAsync(CancellationToken token)
            {
                if (!_registered)
                {
                    _registered = true;
                    if (owner.ThrowOnCancellation) token.Register(() => throw new InvalidOperationException("callback failed"));
                }
                owner.Probed.TrySetResult();
                return ValueTask.FromResult(!owner.Replace);
            }
            public async ValueTask DisposeAsync()
            {
                if (owner.FailCleanup) throw new IOException("cleanup failed");
                owner.CleanupStarted.TrySetResult();
                if (owner.CleanupGate is { } gate) await gate.Task;
                await inner.DisposeAsync();
                Interlocked.Increment(ref owner.SessionDisposals);
            }
        }
    }

    [Fact]
    public async Task FailedWorkspaceDisposalRetainsFactoryAndBlocksConfigurationUntilCleanupIsRetried()
    {
        var root = Path.Combine(Path.GetTempPath(), "smart-shutdown-" + Guid.NewGuid().ToString("N"));
        var factory = new Factory();
        var workspace = new AccountWorkspace(new(Path.Combine(root, "accounts.json"), 1, x => x.Validate()), factory);
        try
        {
            await workspace.SaveAsync(new([new("one", ProbeIntervalMs: 100)]));
            workspace.Accounts[0].Start(); await factory.Probed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            factory.FailCleanup = true;
            await Assert.ThrowsAsync<AggregateException>(() => workspace.DisposeAsync().AsTask());
            Assert.Equal(0, factory.FactoryDisposals);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => workspace.SaveAsync(HostSettings.Empty));
            factory.FailCleanup = false;
            await workspace.DisposeAsync();
            Assert.Equal(1, factory.SessionDisposals); Assert.Equal(1, factory.FactoryDisposals);
            Assert.Empty(workspace.Accounts);
        }
        finally
        {
            factory.FailCleanup = false; await workspace.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ConcurrentWorkspaceDisposalClosesFactoryOnceAndCannotResurrectAccounts()
    {
        var factory = new Factory();
        var workspace = new AccountWorkspace(new("unused.json", 1, x => x.Validate()), factory);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => workspace.DisposeAsync().AsTask()));
        await workspace.DisposeAsync();
        Assert.Equal(1, factory.FactoryDisposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => workspace.SaveAsync(HostSettings.Empty));
    }

    [Fact]
    public async Task ThrowingCancellationCallbackCannotSkipSessionCleanup()
    {
        var factory = new Factory { ThrowOnCancellation = true, CleanupGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var account = new ManagedAccount(new("a", ProbeIntervalMs: 100), factory);
        account.Start(); await factory.Probed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stopping = account.StopAsync(TimeSpan.FromSeconds(2));
        try
        {
            await factory.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(stopping.IsCompleted);
        }
        finally { factory.CleanupGate.TrySetResult(); }
        await Assert.ThrowsAnyAsync<Exception>(() => stopping);
        Assert.Equal(1, factory.SessionDisposals);
        await account.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SessionState.Stopped, account.Status.State);
    }

    [Fact]
    public async Task InvalidationFailureStillClosesSessionDuringBackgroundStop()
    {
        var factory = new Factory { ThrowOnInvalidation = true, Replace = true };
        await using var account = new ManagedAccount(new("a", ProbeIntervalMs: 100), factory);
        account.Start(); await factory.Probed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await SessionTests.Until(() => account.Status.State == SessionState.Faulted);
        Assert.Equal(1, factory.SessionDisposals);
        await account.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SessionState.Stopped, account.Status.State);
    }
}
