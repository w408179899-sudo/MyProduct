using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class CompositionTests
{
    private sealed record Options(int Multiplier);
    private sealed class Source(int value) : IRawChannelReader<int, NoPartition>
    {
        public ValueTask<RawRead<int>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token) => ValueTask.FromResult(RawRead<int>.Complete(value));
    }
    private sealed class Module(ModuleActivationContext activation, SnapshotChannel<int, NoPartition> channel,
        Options options, ConcurrentDictionary<string, int> values) : IAccountModule
    {
        public string Id => "compute"; public int Priority => 1;
        public IReadOnlyList<string> RequiredChannels => [channel.Id];
        public async ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token)
        {
            values[activation.AccountId] = (await context.Snapshots.ReadAsync(channel, default, token)).Value * options.Multiplier;
            return new(TimeSpan.FromMilliseconds(25));
        }
    }
    [Fact] public async Task SharedFactoryCreatesIndependentTypedChannelsAndOptionsForEveryAccountAndRestart()
    {
        var values = new ConcurrentDictionary<string, int>(); var channels = new ConcurrentBag<SnapshotChannel<int, NoPartition>>();
        var factory = new MockSessionFactory(new(), composition =>
        {
            var channel = composition.Channels.Register<int, NoPartition, int>("counter", new Source(composition.Profile.Id == "a" ? 2 : 3),
                new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.FromMilliseconds(50));
            channels.Add(channel);
            composition.AddModule("compute", [channel.Id], new Options(1), options =>
            { if (options.Multiplier <= 0) throw new ArgumentException("Multiplier must be positive."); },
                (context, options) => new Module(context, channel, options, values));
        });
        AccountProfile Profile(string id, int multiplier) => new(id, ModuleSettings:
            ImmutableDictionary<string, JsonElement>.Empty.Add("compute", JsonSerializer.SerializeToElement(new Options(multiplier))));
        await using var first = new ManagedAccount(Profile("a", 5), factory);
        await using var second = new ManagedAccount(Profile("b", 7), factory);
        first.Start(); second.Start(); await SessionTests.Until(() => values.Count == 2);
        Assert.Equal(10, values["a"]); Assert.Equal(21, values["b"]);
        await first.StopAsync(TimeSpan.FromSeconds(2)); first.Start(); await SessionTests.Until(() => first.Status.Generation == 2);
        Assert.Equal(3, channels.Count); Assert.Equal(3, channels.Distinct().Count());
    }
    [Fact] public async Task UnknownModuleSettingsFaultOnceInsteadOfReconnectingForever()
    {
        var calls = 0;
        var factory = new MockSessionFactory(new(), _ => calls++);
        await using var account = new ManagedAccount(new("a", RetryDelayMs: 100, ModuleSettings:
            ImmutableDictionary<string, JsonElement>.Empty.Add("unknown", JsonSerializer.SerializeToElement(new { Value = 1 }))), factory);
        account.Start(); await SessionTests.Until(() => account.Status.State == SessionState.Faulted);
        Assert.Equal(1, calls); Assert.Contains("unknown module", account.Status.Error);
    }
    [Fact] public void ModuleCannotCaptureAnUndeclaredReaderDuringActivation()
    {
        var composition = new SessionComposition(new("a"));
        var channel = composition.Channels.Register<int, NoPartition, int>("counter", new Source(1), new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        composition.AddModule("compute", [], context =>
        {
            context.Snapshots.ReadAsync(channel, default).GetAwaiter().GetResult();
            throw new InvalidOperationException("unreachable");
        });
        composition.Seal();
        using var session = new SnapshotProvider(composition.Channels).OpenSession(new("d", "c", "a", "w", 1, "p", "m"));
        Assert.Throws<SessionConfigurationException>(() => composition.Activate(session.Reader, Guid.NewGuid()));
        Assert.Equal(0, session.Metrics.Captures);
    }
    [Theory]
    [InlineData("{\"Multiplier\":0}")]
    [InlineData("{\"Multplier\":5}")]
    [InlineData("null")]
    public void InvalidOrMisspelledModuleOptionsFailBeforeActivation(string json)
    {
        var composition = new SessionComposition(new("a", ModuleSettings:
            ImmutableDictionary<string, JsonElement>.Empty.Add("compute", JsonSerializer.Deserialize<JsonElement>(json))));
        var activated = false;
        Assert.Throws<SessionConfigurationException>(() => composition.AddModule("compute", [], new Options(1),
            options => { if (options.Multiplier <= 0) throw new ArgumentException("Multiplier"); },
            (_, _) => { activated = true; throw new InvalidOperationException(); }));
        Assert.False(activated);
    }
    [Fact] public void ConfigurationCannotBeSilentlyIgnoredByAParameterlessModuleRegistration()
    {
        var composition = new SessionComposition(new("a", ModuleSettings:
            ImmutableDictionary<string, JsonElement>.Empty.Add("compute", JsonSerializer.SerializeToElement(new Options(4)))));
        Assert.Throws<SessionConfigurationException>(() => composition.AddModule("compute", [], _ => throw new InvalidOperationException()));
    }
}
