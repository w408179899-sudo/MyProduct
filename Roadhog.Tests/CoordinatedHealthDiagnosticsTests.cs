using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class CoordinatedHealthDiagnosticsTests
{
    public static async Task LifeGuardObservationIsPassiveAsync(CombatFlowVersion version)
    {
        var capturedAt = DateTimeOffset.Now.AddSeconds(-2);
        var api = new FakeGameApi
        {
            Player = new(1, 21, "test", 42, 100, 100, 100, 0, new(1, 2, 3), capturedAt)
        };
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var settings = new ScriptSettings();
        var context = new AccountWorkerContext(
            new AccountConfig { AccountName = "test", CombatFlowVersion = version, ScriptSettings = settings }, api, logger,
            new AccountRuntimeManager(logger), new AccountWorkerOptions(), CancellationToken.None);
        var state = new StationaryCombatState
        {
            Fighting = true, CurrentTargetEntityId = 21, CurrentTargetServerObjectId = 2100
        };
        var semiAuto = new SemiAutoCombatState();
        semiAuto.BeginSpiritmasterSummonVerification(DateTimeOffset.Now, TimeSpan.FromSeconds(5));
        var controller = new StationaryCombatController(input, new SemiAutoCombatController(input));

        var result = await controller.TickPlayerLifeGuardAsync(context,
            SemiAutoSkillPlan.FromSettings(settings.Skills), semiAuto, state,
            followRevivePath: false);

        Check(result is null && api.PlayerReadCount == 1 && api.WorldObjectReadCount == 0 &&
              input.Keys.Count == 0 && input.KeyDowns.Count == 0 && input.KeyUps.Count == 0,
            "Observation must preserve the live-player result, read count and input trace.");
        Check(state.Fighting && state.CurrentTargetEntityId == 21 &&
              state.TopLevelState == StationaryCombatTopLevelState.Normal &&
              semiAuto.HasPendingSpiritmasterSummonVerification,
            "Observation must not change combat or summon state.");
        var entries = logger.Entries.Where(e => e.EventName == "combat_flow.health.observed").ToArray();
        if (version == CombatFlowVersion.Legacy)
        {
            Check(entries.Length == 0, "Legacy must not enable V2 diagnostics.");
            return;
        }

        Check(entries.Length == 1, "V2 must provide one health observation at the life guard.");
        var fields = entries[0].Fields;
        Check(Equals(fields["hp"], 42u) && Equals(fields["maxHp"], 100u) &&
              Equals(fields["playerCapturedAt"], capturedAt) &&
              Equals(fields["boundary"], "life_guard") && Equals(fields["fighting"], true) &&
              Equals(fields["summonVerificationPending"], true) &&
              Equals(fields["targetServerObjectId"], 2100u),
            "The diagnostic must correlate health, original snapshot time and flow state.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
