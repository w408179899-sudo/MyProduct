param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $smartRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
$consumerRoot = [IO.Path]::GetFullPath($Destination)
if (-not $consumerRoot.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath (Join-Path $consumerRoot 'template\SmokeProject.Domain\SmokeProject.Domain.csproj')) -or
    (Test-Path -LiteralPath (Join-Path $consumerRoot '.template.config\template.json'))) {
    throw 'Consumer fixtures may only be written into a generated SmokeProject under artifacts.'
}
function Write-Fixture([string]$RelativePath, [string]$Content) {
    [IO.File]::WriteAllText((Join-Path $consumerRoot $RelativePath), $Content, [Text.UTF8Encoding]::new($false))
}
Write-Fixture 'template\SmokeProject.Domain\SignalValue.cs' @'
namespace SmokeProject.Domain;
public sealed record SignalValue(int Number);
'@
Write-Fixture 'template\SmokeProject.Application\ProjectChannels.cs' @'
using Smart.Contracts;
using SmokeProject.Domain;
namespace SmokeProject.Application;
public sealed record ProjectChannels(SnapshotChannel<SignalValue, NoPartition> Signal);
'@
Write-Fixture 'template\SmokeProject.Application\SignalModule.cs' @'
using Smart.Contracts;
using SmokeProject.Domain;
namespace SmokeProject.Application;
public sealed record SignalOptions(int Minimum, int KeyCode);
public sealed class SignalModule(SnapshotChannel<SignalValue, NoPartition> signal, SignalOptions options) : IAccountModule
{
    private int _stage;
    public string Id => "signal";
    public int Priority => 1;
    public IReadOnlyList<string> RequiredChannels => [signal.Id];
    public async ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token)
    {
        var value = (await context.Snapshots.ReadAsync(signal, default, token)).Value;
        string? action = null;
        if (_stage == 0 && value.Number >= options.Minimum) { _stage = 1; action = "signal-send"; }
        else if (_stage == 1 && context.LastAction?.State == ActionState.Succeeded)
        { _stage = 2; action = "signal-feedback"; }
        return new(TimeSpan.FromMilliseconds(25), action is null ? null : new ActionPlan(action,
            InputResource.Keyboard, [InputCommand.PressDown(options.KeyCode)], TimeSpan.FromSeconds(1),
            Retention: InputRetention.UntilOwnerChanges));
    }
}
'@
Write-Fixture 'template\SmokeProject.Infrastructure\ProjectReaders.cs' @'
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using SmokeProject.Application;
using SmokeProject.Domain;
namespace SmokeProject.Infrastructure;
public static class ProjectReaders
{
    private sealed class SignalReader : IRawChannelReader<SignalValue, NoPartition>
    {
        private bool _published;
        public ValueTask<RawRead<SignalValue>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_published) return ValueTask.FromResult(RawRead<SignalValue>.Failed("Controlled hold after first publication."));
            _published = true;
            return ValueTask.FromResult(RawRead<SignalValue>.Complete(new(7)));
        }
    }
    public static ProjectChannels RegisterMock(SnapshotCatalog catalog) => new(
        catalog.Register<SignalValue, NoPartition, SignalValue>("signal", new SignalReader(),
            new ReplaceMerger<SignalValue>(value => value.Number >= 0), SnapshotMergePolicy.Replace, TimeSpan.Zero));
    public static ProjectChannels RegisterHardware(SnapshotCatalog catalog, DmaDispatcher dispatcher, ProcessBinding process) =>
        throw new NotSupportedException("The generated validation fixture only supports Mock input and data.");
}
'@
Write-Fixture 'template\SmokeProject.Bootstrap\ProjectComposition.cs' @'
using Smart.Adapters.Dma;
using Smart.Hosting;
using SmokeProject.Application;
using SmokeProject.Infrastructure;
namespace SmokeProject.Bootstrap;
public static class ProjectComposition
{
    public static void ConfigureMock(SessionComposition composition) =>
        RegisterModules(composition, ProjectReaders.RegisterMock(composition.Channels));
    public static void ConfigureHardware(SessionComposition composition, DmaDispatcher dispatcher, ProcessBinding process) =>
        RegisterModules(composition, ProjectReaders.RegisterHardware(composition.Channels, dispatcher, process));
    private static void RegisterModules(SessionComposition composition, ProjectChannels channels) =>
        composition.AddModule("signal", [channels.Signal.Id], new SignalOptions(1, 4), options =>
        {
            if (options.Minimum < 0 || options.KeyCode is < 1 or > 255) throw new ArgumentException("Invalid signal options.");
        }, (_, options) => new SignalModule(channels.Signal, options));
}
'@
Write-Fixture 'tests\SmokeProject.Tests\GeneratedCompositionTests.cs' @'
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Smart.Contracts;
using Smart.Hosting;
using Smart.Runtime;
using SmokeProject.Application;
using SmokeProject.Bootstrap;
using Xunit;
namespace SmokeProject.Tests;
public sealed class GeneratedCompositionTests
{
    private sealed class Events : IEventSink
    {
        public ConcurrentQueue<DiagnosticEvent> Entries { get; } = new();
        public void Write(DiagnosticEvent entry) => Entries.Enqueue(entry);
    }
    [Fact] public async Task GeneratedConsumerUsesAllFourLayersAndReceivesActionFeedbackForIsolatedAccounts()
    {
        var leasePath = Path.Combine(Path.GetTempPath(), "smart-consumer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var events = new Events();
            await using var host = new ProjectHost(events, leasePath, recordSnapshots: true);
            AccountProfile Profile(string id, int minimum, int key) => new(id, ModuleSettings:
                ImmutableDictionary<string, JsonElement>.Empty.Add("signal", JsonSerializer.SerializeToElement(new SignalOptions(minimum, key))));
            await using var first = new ManagedAccount(Profile("fixture-a", 3, 4), host, events);
            await using var second = new ManagedAccount(Profile("fixture-b", 5, 5), host, events);
            first.Start(); second.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (events.Entries.Count(entry => entry.Name == "action.completed" && entry.ActionId == "signal-feedback") < 2)
                await Task.Delay(10, deadline.Token);
            Assert.Equal(SessionState.Running, first.Status.State); Assert.Equal(SessionState.Running, second.Status.State);
            await Task.WhenAll(first.StopAsync(TimeSpan.FromSeconds(2)), second.StopAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(SessionState.Stopped, first.Status.State); Assert.Equal(SessionState.Stopped, second.Status.State);
            foreach (var (account, key) in new[] { ("fixture-a", 4), ("fixture-b", 5) })
            {
                var entries = events.Entries.Where(entry => entry.Scope == account).ToArray();
                var publication = Assert.Single(entries, entry => entry.Name == "snapshot.published");
                using (var json = JsonDocument.Parse(publication.Detail))
                    Assert.Equal(7, json.RootElement.GetProperty("Snapshot").GetProperty("Value").GetProperty("Number").GetInt32());
                var completed = entries.Where(entry => entry.Name == "action.completed").ToArray();
                Assert.Equal(2, completed.Length);
                Assert.All(completed, entry => Assert.Equal(ActionState.Succeeded, JsonSerializer.Deserialize<ActionFeedback>(entry.Detail)!.State));
                Assert.Contains(completed, entry => entry.ActionId == "signal-feedback");
                foreach (var started in entries.Where(entry => entry.Name == "action.started"))
                {
                    using var json = JsonDocument.Parse(started.Detail);
                    Assert.Equal(key, Assert.Single(json.RootElement.GetProperty("Commands").EnumerateArray()).GetProperty("Code").GetInt32());
                }
            }
            Assert.Equal(2, events.Entries.Where(entry => entry.Name == "snapshot.published").Select(entry => entry.RunId).Distinct().Count());
            // The real Mock host releases its retained key and disposes the executor before surrendering these leases.
            var leases = new InputLeaseRegistry(leasePath);
            using var firstLease = leases.Acquire("mock:fixture-a");
            using var secondLease = leases.Acquire("mock:fixture-b");
        }
        finally { if (Directory.Exists(leasePath)) Directory.Delete(leasePath, true); }
    }
}
'@
Write-Output ('CONSUMER_FIXTURE_READY ' + $consumerRoot)
