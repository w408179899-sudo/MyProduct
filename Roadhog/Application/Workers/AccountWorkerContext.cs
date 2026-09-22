using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Application.SemiAuto;

namespace Roadhog.Application.Workers;

public sealed class AccountWorkerContext
{
    public AccountWorkerContext(
        AccountConfig config,
        IRoadhogSnapshotReaderFactory snapshotReaders,
        IRoadhogLogger logger,
        AccountRuntimeManager runtimeStates,
        AccountWorkerOptions options,
        CancellationToken stopToken,
        CleanupRequestMailbox? cleanupRequests = null)
    {
        Config = config;
        Logger = logger;
        RuntimeStates = runtimeStates;
        Options = options;
        StopToken = stopToken;
        Snapshots = snapshotReaders.Create(config, logger, stopToken);
        CleanupRequests = cleanupRequests ?? new();
    }

    public AccountConfig Config { get; }

    public IRoadhogSnapshotReader Snapshots { get; }

    public IRoadhogLogger Logger { get; }

    public AccountRuntimeManager RuntimeStates { get; }

    public AccountWorkerOptions Options { get; }

    public CancellationToken StopToken { get; }

    public CleanupRequestMailbox CleanupRequests { get; }
    public bool WorkflowOwnsCleanup { get; set; }

    public SkillKeyBindings? SkillBindings { get; private set; }

    public async Task PrepareSkillBindingsAsync()
    {
        if (SkillBindings is not null) return;
        // Readiness is a gameplay boundary; DMA retries and publication remain below this API.
        while (!(await Snapshots.ReadChannelTransitionAsync().ConfigureAwait(false)).Value.IsReady)
            await Task.Delay(100, StopToken).ConfigureAwait(false);
        var quickbar = (await Snapshots.ReadQuickbarAsync().ConfigureAwait(false)).Value;
        var skills = (await Snapshots.ReadSkillsAsync().ConfigureAwait(false)).Value;
        // Full skill lists group ranks for the editor. Include exact lower-rank skills on the bar too.
        var missingIds = quickbar.Slots.Where(s => s.SkillId != 0 && !skills.Any(skill => skill.SkillId == s.SkillId))
            .Select(s => s.SkillId).Distinct().ToArray();
        if (missingIds.Length > 0)
            skills = skills.Concat((await Snapshots.ReadSkillsAsync(missingIds).ConfigureAwait(false)).Value)
                .DistinctBy(s => s.SkillId).ToArray();
        var bindings = new SkillKeyBindings(quickbar, skills);
        Config.ScriptSettings = bindings.ApplyTo(Config.ScriptSettings ?? new(), ReportMissingSkillBinding);
        SkillBindings = bindings;
        Logger.Info("skills.bindings.loaded", new Dictionary<string, object?>
        {
            ["account"] = Config.AccountName, ["page"] = quickbar.Page + 1,
            ["bindings"] = string.Join("; ", quickbar.Slots.Where(s => s.SkillId != 0)
                .Select(s => bindings.Resolve(s.SkillId, "")).OfType<BoundSkill>().Select(s => s.SkillId + ":" + s.Name + "=" + s.Key))
        });
    }

    public void ReportMissingSkillBinding(string message) => Logger.Warn("skills.binding.missing", new Dictionary<string, object?>
        { ["account"] = Config.AccountName, ["reason"] = message });

    private AccountWorkerContext(AccountWorkerContext source, AccountConfig config)
    {
        Config = config; Snapshots = source.Snapshots; Logger = source.Logger; RuntimeStates = source.RuntimeStates;
        Options = source.Options; StopToken = source.StopToken; CleanupRequests = source.CleanupRequests;
        WorkflowOwnsCleanup = source.WorkflowOwnsCleanup;
        SkillBindings = source.SkillBindings;
    }
    public AccountWorkerContext ForCleanup(ScriptSettings settings)
    {
        var config = Config.Clone();
        config.ScriptSettings ??= new();
        config.ScriptSettings.Maintenance = settings.Maintenance.Clone();
        config.ScriptSettings.Paths = settings.Paths.Clone();
        config.ScriptSettings.Paths.BagCleanupReturnByReversePath = true;
        if (SkillBindings is not null) config.ScriptSettings = SkillBindings.ApplyTo(config.ScriptSettings, ReportMissingSkillBinding);
        return new(this, config);
    }
}
