using System;
using System.Collections.Generic;

namespace Tool.Tests
{
    internal static class CameraTurnVerificationTests
    {
        public static bool RunAll()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("detects yaw improvement", DetectsYawImprovement),
                new KeyValuePair<string, Action>("detects yaw overshoot with improvement", DetectsYawOvershootWithImprovement),
                new KeyValuePair<string, Action>("detects no improvement", DetectsNoImprovement)
            };

            int failed = 0;
            Console.WriteLine("Camera turn verification tests.");
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

            Console.WriteLine("CameraTurnVerificationTests Result=" + (failed == 0 ? "passed" : "failed") +
                              " Total=" + tests.Count +
                              " Failed=" + failed);
            return failed == 0;
        }

        private static void DetectsYawImprovement()
        {
            CameraTurnVerificationResult result = CameraTurnVerification.Verify(30.0, 0.0, 8.0, 0.0);
            AssertTrue(result.YawImproved, "yaw improved");
            AssertTrue(result.AnyImproved, "any improved");
            AssertFalse(result.YawOvershot, "yaw overshot");
        }

        private static void DetectsYawOvershootWithImprovement()
        {
            CameraTurnVerificationResult result = CameraTurnVerification.Verify(30.0, 0.0, -5.0, 0.0);
            AssertTrue(result.YawImproved, "yaw improved");
            AssertTrue(result.AnyImproved, "any improved");
            AssertTrue(result.YawOvershot, "yaw overshot");
        }

        private static void DetectsNoImprovement()
        {
            CameraTurnVerificationResult result = CameraTurnVerification.Verify(30.0, 2.0, 35.0, 3.0);
            AssertFalse(result.YawImproved, "yaw improved");
            AssertFalse(result.PitchImproved, "pitch improved");
            AssertFalse(result.AnyImproved, "any improved");
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
