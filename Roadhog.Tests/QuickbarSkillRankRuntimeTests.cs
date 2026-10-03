using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class QuickbarSkillRankRuntimeTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static SkillSnapshot Rank(uint id, int tier) => new(id, "裂破击 " + tier, tier, 1, "裂破击", tier, false, 30000, 0, XmlCounterSkill: "Parry");

    public static async Task ExactRankAsync()
    {
        var api = new FakeGameApi { Skills = new[] { Rank(1227, 1), Rank(1271, 3) } };
        var logger = new InMemoryRoadhogLogger();
        var factory = new Factory(api);
        var accounts = new AccountRuntimeManager(logger);
        var saved = new InMemoryAccountConfigStore(new AccountConfig
        {
            AccountName = "rank-account", ProcessId = 812, TargetProcessName = "Aion.bin", VmmDeviceName = "fpga://devindex=4"
        });
        var runtime = new RoadhogRuntime(factory, logger, accounts, null!, saved);
        var full = await runtime.RefreshSkillsAsync("rank-account");
        Check(full.Count == 2 && api.LastRequestedSkillIds is null, "existing full refresh retains provider full-list semantics");
        var exact = await runtime.RefreshSkillsByIdsAsync(new uint[] { 0, 1227, 1227 }, "rank-account");
        Check(exact.Single().SkillId == 1227 && exact.Single().DisplayTier == 1, "exact bar rank I never silently selects learned rank III");
        Check(api.LastRequestedSkillIds!.SequenceEqual(new uint[] { 1227 }), "zero and duplicates do not broaden exact request");
        Check(api.LastSkillsContext?.ProcessId == 812 && api.LastSkillsContext.VmmDeviceName == "fpga://devindex=4", "exact refresh retains saved account process and DMA scope");
        Check((await runtime.RefreshSkillsByIdsAsync(new uint[] { 999 }, "rank-account")).Count == 0, "unknown id does not fall back to same-name highest rank");
        var creates = factory.Creates;
        Check((await runtime.RefreshSkillsByIdsAsync(Array.Empty<uint>(), "rank-account")).Count == 0 && factory.Creates == creates, "empty exact request causes no hardware/session creation");
        Check((await runtime.RefreshSkillsByIdsAsync(new uint[] { 0 }, "rank-account")).Count == 0 && factory.Creates == creates, "zero-only exact request does not become a full refresh");
        using (var stop = new CancellationTokenSource())
        {
            stop.Cancel();
            try { await runtime.RefreshSkillsByIdsAsync(new uint[] { 1227 }, "rank-account", stop.Token); throw new Exception("cancellation expected"); }
            catch (OperationCanceledException) { }
            Check(factory.Creates == creates, "cancelled exact read creates no new session");
        }
        try { await runtime.RefreshSkillsByIdsAsync(null!, "rank-account"); throw new Exception("null request expected"); }
        catch (ArgumentNullException) { }
        accounts.MarkStarting(new AccountConfig
            { AccountName = "rank-account", ProcessId = 900, TargetProcessName = "Aion.bin", VmmDeviceName = "fpga://devindex=7" });
        await runtime.RefreshSkillsByIdsAsync(new uint[] { 1227 }, "rank-account");
        Check(api.LastSkillsContext?.ProcessId == 900 && api.LastSkillsContext.VmmDeviceName == "fpga://devindex=7", "live account binding overrides old saved scope for exact refresh");
        full = await runtime.RefreshSkillsAsync("rank-account");
        Check(full.Count == 2 && api.LastRequestedSkillIds is null, "exact reads do not change subsequent legacy full-list request");
    }

    private sealed class Factory(FakeGameApi api) : IRoadhogSnapshotReaderFactory
    {
        public int Creates;
        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken cancellationToken = default)
        {
            Creates++;
            return api.Create(config, logger, cancellationToken);
        }
    }
}
