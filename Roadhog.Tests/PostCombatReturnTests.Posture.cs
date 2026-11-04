using Roadhog.Core.Common;
using Roadhog.Core.Model;

internal static partial class PostCombatReturnTests
{
    public static async Task ReturnHomePostureAsync()
    {
        using var timing = new FastTiming();
        foreach (var mode in new[] { "post_combat", "outside_radius", "no_target" })
        {
            using var game = new Fixture(mode == "outside_radius" ? 65 : 8);
            game.Settings.Combat.ReturnHomeAfterCombat = mode == "post_combat";
            game.Settings.Combat.ReturnHomeWhenNoTarget = mode == "no_target";
            await game.FinishFight();
            game.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
            game.Api.TargetEntityId = 0;
            game.Api.Player = game.Api.Player with { StanceFlags = 5, MotionMode = 1 };
            Check(!game.SemiAuto.IsMaintenanceResting, "fixture reproduces cleared rest flag with seated posture");

            await game.Tick();
            Check(game.Input.Keys.Contains("X"), mode + ": seated return presses X");
            Check(!game.State.IsMovingForward && !game.Input.KeyDowns.Contains("W"),
                mode + ": key acknowledgement alone must not start movement");

            // A stale navigation timer must not turn an uncompleted stand action into recall.
            game.State.MarkReturnHomeProgress(game.Api.Player.Position!.Value, DateTimeOffset.Now.AddSeconds(-20));
            game.SemiAuto.MarkMaintenanceKeyAttempted("X", DateTimeOffset.Now);
            var standCount = game.Input.Keys.Count(key => key == "X");
            await game.Tick();
            Check(game.Input.Keys.Count(key => key == "X") == standCount, mode + ": stand retry is throttled");
            Check(game.State.ReturnHomeLastProgressAt == DateTimeOffset.MinValue && !game.State.IsMovingForward,
                mode + ": sitting resets progress tracking and holds movement");
            Check(!game.Input.Keys.Contains("Space") && !game.Input.Keys.Contains("OemPlus") &&
                !game.Logger.Entries.Any(e => e.EventName == "town_return.stuck_recovery.started"),
                mode + ": sitting cannot trigger stuck jumps or recall");

            game.SemiAuto.MarkMaintenanceKeyAttempted("X", DateTimeOffset.Now.AddSeconds(-2));
            await game.Tick();
            Check(game.Input.Keys.Count(key => key == "X") == standCount + 1 && !game.State.IsMovingForward,
                mode + ": still seated retries X without walking");
            game.Api.Player = game.Api.Player with { StanceFlags = 0, MotionMode = 0 };
            standCount = game.Input.Keys.Count(key => key == "X");
            await game.Tick();
            Check(game.State.IsMovingForward && game.Input.KeyDowns.Contains("W"), mode + ": standing resumes return");
            Check(game.Input.Keys.Count(key => key == "X") == standCount, mode + ": standing sends no extra X");
        }
    }

    public static async Task ReturnHomeStandFailureAsync()
    {
        using var timing = new FastTiming();
        using var game = new Fixture(8);
        await game.FinishFight();
        await game.Tick();
        Check(game.State.IsMovingForward, "fixture starts an active return");
        game.Api.Player = game.Api.Player with { StanceFlags = 5, MotionMode = 1 };
        game.Input.PressResult = key => key == "X" ? OperationResult.Fail("stand rejected") : OperationResult.Ok();
        game.State.MarkReturnHomeProgress(game.Api.Player.Position!.Value, DateTimeOffset.Now.AddSeconds(-20));
        await game.Tick();
        Check(game.Input.Keys.Contains("X") && game.Input.KeyUps.Contains("W") && !game.State.IsMovingForward,
            "failed stand stops previously held movement");
        Check(game.State.PostCombatReturningHome && game.State.ReturnHomeLastProgressAt == DateTimeOffset.MinValue,
            "failed stand preserves return intent without counting it as navigation failure");
        Check(game.Logger.Entries.Any(e => e.EventName == "stationary_combat.return_home.stand_failed"),
            "failed stand has a diagnostic");
        game.Input.PressResult = null;
        game.SemiAuto.MarkMaintenanceKeyAttempted("X", DateTimeOffset.Now.AddSeconds(-2));
        game.Input.AfterPress = key =>
        {
            if (key == "X") game.Api.Player = game.Api.Player with { StanceFlags = 0, MotionMode = 0 };
        };
        await game.Tick();
        Check(!game.State.IsMovingForward, "successful retry still waits for the next observed posture");
        await game.Tick();
        Check(game.State.IsMovingForward, "confirmed standing resumes the same return after input failure");
    }
}
