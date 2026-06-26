using System;
using System.Collections.Generic;

namespace Tool.Tests
{
    internal static class PathFollowMoveControlTests
    {
        public static bool RunAll()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("moving yaw over restart threshold requests restart", MovingYawOverRestartThresholdRequestsRestart),
                new KeyValuePair<string, Action>("moving yaw equal threshold does not restart", MovingYawEqualThresholdDoesNotRestart),
                new KeyValuePair<string, Action>("standing yaw over threshold does not request moving restart", StandingYawOverThresholdDoesNotRestart),
                new KeyValuePair<string, Action>("close target disables move adjust", CloseTargetDisablesMoveAdjust),
                new KeyValuePair<string, Action>("restart still turns even when close target disables adjust", RestartStillTurnsWhenCloseTargetDisablesAdjust)
            };

            int failed = 0;
            Console.WriteLine("Path follow move control tests.");
            for (int i = 0; i < tests.Count; i++)
            {
                string name = tests[i].Key;
                try
                {
                    tests[i].Value();
                    Console.WriteLine("[PASS] " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("[FAIL] " + name + " :: " + ex.Message);
                }
            }

            Console.WriteLine("PathFollowMoveControlTests Result=" + (failed == 0 ? "passed" : "failed") +
                              " Total=" + tests.Count +
                              " Failed=" + failed);
            return failed == 0;
        }

        private static void MovingYawOverRestartThresholdRequestsRestart()
        {
            AssertTrue(PathFollowMoveControl.ShouldRestartMoveForYaw(true, 6.1, 6.0), "positive yaw");
            AssertTrue(PathFollowMoveControl.ShouldRestartMoveForYaw(true, -6.1, 6.0), "negative yaw");
        }

        private static void MovingYawEqualThresholdDoesNotRestart()
        {
            AssertFalse(PathFollowMoveControl.ShouldRestartMoveForYaw(true, 6.0, 6.0), "equal threshold");
        }

        private static void StandingYawOverThresholdDoesNotRestart()
        {
            AssertFalse(PathFollowMoveControl.ShouldRestartMoveForYaw(false, 30.0, 6.0), "not moving");
        }

        private static void CloseTargetDisablesMoveAdjust()
        {
            AssertTrue(PathFollowMoveControl.ShouldDisableMoveAdjustByDistance(true, 15.0, 15.0), "at boundary");
            AssertFalse(PathFollowMoveControl.ShouldDisableMoveAdjustByDistance(true, 15.1, 15.0), "outside boundary");
        }

        private static void RestartStillTurnsWhenCloseTargetDisablesAdjust()
        {
            bool restart = PathFollowMoveControl.ShouldRestartMoveForYaw(true, 7.0, 6.0);
            bool disabled = PathFollowMoveControl.ShouldDisableMoveAdjustByDistance(true, 8.0, 15.0);
            bool shouldTurn = PathFollowMoveControl.ShouldTurn(restart, disabled, 7.0, 0.0, 3.0, 5.0);
            AssertTrue(shouldTurn, "restart should force formal turn even when close target disables move adjust");
        }

        private static void AssertTrue(bool condition, string name)
        {
            if (!condition)
            {
                throw new InvalidOperationException(name);
            }
        }

        private static void AssertFalse(bool condition, string name)
        {
            if (condition)
            {
                throw new InvalidOperationException(name);
            }
        }
    }
}
