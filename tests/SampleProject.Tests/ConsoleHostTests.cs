using SampleProject.Bootstrap;
using SampleProject.ConsoleHost;
using Smart.Hosting;
using Xunit;
namespace SampleProject.Tests;

public sealed class ConsoleHostTests
{
    [Fact] public void DemoDurationContinuousRunAndLongBoundedRunAreExplicit()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ConsoleRunOptions.Parse([]).Duration);
        Assert.Null(ConsoleRunOptions.Parse(["--run"]).Duration);
        Assert.Equal(TimeSpan.FromHours(1), ConsoleRunOptions.Parse(["--duration-ms", "3600000"]).Duration);
    }

    [Theory]
    [InlineData("--run", "--duration-ms", "1000")]
    [InlineData("--duration-ms", "0")]
    [InlineData("--duration-ms", "2147483648")]
    [InlineData("--config")]
    [InlineData("--account", "--run")]
    [InlineData("--run", "--run")]
    [InlineData("--unknown")]
    [InlineData("--probe", "a", "--run-hardware")]
    [InlineData("--list-devices", "--run")]
    public void MalformedOrConflictingOptionsCannotSilentlyRun(params string[] arguments) =>
        Assert.Throws<ArgumentException>(() => ConsoleRunOptions.Parse(arguments));

    private sealed class FailingFactory : IRuntimeSessionFactory
    {
        public int Opens;
        public ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token)
        { Interlocked.Increment(ref Opens); throw new ArgumentException("invalid module configuration"); }
    }

    [Fact] public async Task HardwareStillRequiresExplicitAuthorizationAndUnknownAccountsFailBeforeOpening()
    {
        var factory = new FailingFactory();
        await using var account = new ManagedAccount(new("physical", RuntimeMode.Hardware), factory);
        var options = ConsoleRunOptions.Parse(["--run"]);
        await Assert.ThrowsAsync<ArgumentException>(() => ConsoleAccountRunner.RunAsync([account], options));
        Assert.Same(account, Assert.Single(ConsoleRunOptions.Parse(["--run", "--run-hardware"]).SelectAccounts([account])));
        await Assert.ThrowsAsync<ArgumentException>(() => ConsoleAccountRunner.RunAsync([account], options with { AccountId = "missing" }));
        Assert.Equal(0, factory.Opens);
    }

    [Fact] public async Task ContinuousRunStopsSelectedAccountsOnCancellationWithoutStartingOthers()
    {
        var leases = Path.Combine(Path.GetTempPath(), "smart-console-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var host = new ProjectHost(leaseDirectory: leases);
            await using var selected = new ManagedAccount(new("selected"), host);
            await using var other = new ManagedAccount(new("other"), host);
            using var cancellation = new CancellationTokenSource();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var execution = ConsoleAccountRunner.RunAsync([selected, other], ConsoleRunOptions.Parse(["--run", "--account", "selected"]), cancellation.Token);
            while (selected.Status.State != SessionState.Running) await Task.Delay(10, deadline.Token);
            Assert.False(execution.IsCompleted);
            await cancellation.CancelAsync();
            var result = Assert.Single(await execution.WaitAsync(deadline.Token));
            Assert.Equal(SessionState.Stopped, selected.Status.State);
            Assert.Equal(1, result.Status.Generation); Assert.Null(result.Failure);
            Assert.Equal(0, other.Status.Generation);
        }
        finally { if (Directory.Exists(leases)) Directory.Delete(leases, true); }
    }

    [Fact] public async Task FaultedContinuousAccountEndsWithFailurePreservedAfterCleanup()
    {
        var factory = new FailingFactory();
        await using var account = new ManagedAccount(new("invalid"), factory);
        var reported = new List<ConsoleAccountResult>();
        var results = await ConsoleAccountRunner.RunAsync([account], ConsoleRunOptions.Parse(["--run"]),
            reportFailure: reported.Add).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("invalid module configuration", Assert.Single(results).Failure);
        Assert.Equal(SessionState.Stopped, account.Status.State);
        Assert.Equal(SessionState.Faulted, Assert.Single(reported).Status.State);
    }
}
