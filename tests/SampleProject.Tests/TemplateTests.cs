using SampleProject.Application;
using SampleProject.Bootstrap;
using Smart.Adapters.Dma;
using Smart.Data;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Smart.Runtime;
using Xunit;
namespace SampleProject.Tests;

public sealed class TemplateTests
{
    [Fact] public async Task ProjectStartsWithMockAndCanRestart()
    {
        var leasePath = Path.Combine(Path.GetTempPath(), "smart-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var host = new ProjectHost(leaseDirectory: leasePath);
            await using var account = new ManagedAccount(new("template"), host);
            account.Start();
            using var token = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (account.Status.State != SessionState.Running) await Task.Delay(10, token.Token);
            await account.PauseAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(SessionState.Paused, account.Status.State);
            account.Start();
            while (account.Status.Generation < 2) await Task.Delay(10, token.Token);
            await account.StopAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(SessionState.Stopped, account.Status.State);
        }
        finally { if (Directory.Exists(leasePath)) Directory.Delete(leasePath, true); }
    }
    [Fact] public void ProjectCompositionRegistersValidChannelsAndModules()
    {
        var composition = new SessionComposition(new("a")); ProjectComposition.ConfigureMock(composition); composition.Seal();
        using var session = new SnapshotProvider(composition.Channels).OpenSession(new("d", "c", "a", "w", 1, "p", "m"));
        var modules = composition.Activate(session.Reader, Guid.NewGuid());
        ModuleCatalog.Validate(modules, composition.Channels.Channels.Select(channel => channel.Id).ToArray());
        if (IsTemplateSource()) { Assert.Empty(composition.Channels.Channels); Assert.Empty(modules); }
    }
    private static bool IsTemplateSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Smart.slnx")))
                return File.Exists(Path.Combine(directory.FullName, ".template.config", "template.json"));
        throw new DirectoryNotFoundException("Solution root was not found.");
    }
    [Fact] public void ProcessSelectionRejectsAmbiguousNamesAndMissingModule()
    {
        var settings = new DmaSettings("", "", [], null, "program", "module");
        ProcessBinding[] processes = [new(1, "program", "first", 4096), new(2, "program", "second", 8192)];
        Assert.Throws<IOException>(() => HardwareSessionFactory.Select(processes, settings));
        Assert.Equal(2, HardwareSessionFactory.Select(processes, settings with { ProcessId = 2 }).ProcessId);
        Assert.Throws<IOException>(() => HardwareSessionFactory.Select([new(1, "program", "first", 0)], settings));
    }
}
