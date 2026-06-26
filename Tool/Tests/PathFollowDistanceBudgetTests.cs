using System;
using System.Collections.Generic;

namespace Tool.Tests
{
    internal static class PathFollowDistanceBudgetTests
    {
        public static bool RunAll()
        {
            var tests = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("arrives when current position is within reach distance", ArrivesWithinReachDistance),
                new KeyValuePair<string, Action>("continues while moved distance is below segment budget", ContinuesBeforeBudgetIsUsed),
                new KeyValuePair<string, Action>("stops when moved distance exceeds original P-to-B budget", StopsWhenTravelBudgetIsUsed),
                new KeyValuePair<string, Action>("flow test: close enough to B wins before budget stop", FlowArrivesBeforeBudgetStop),
                new KeyValuePair<string, Action>("flow test: jump past B triggers budget stop", FlowStopsAfterOvershoot)
            };

            int failed = 0;
            Console.WriteLine("Path follow distance budget tests.");
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

            Console.WriteLine("PathFollowDistanceBudgetTests Result=" + (failed == 0 ? "passed" : "failed") +
                              " Total=" + tests.Count +
                              " Failed=" + failed);
            return failed == 0;
        }

        private static void ArrivesWithinReachDistance()
        {
            var budget = new PathFollowDistanceBudget(P(0, 0), P(10, 0));
            PathFollowDistanceBudgetResult result = budget.Update(P(7.2, 0), 3.0);

            AssertEqual(PathFollowDistanceBudgetDecision.Arrived, result.Decision, "decision");
            AssertNear(7.2, result.MovedDistance, 0.0001, "moved distance");
            AssertNear(2.8, result.DistanceToTarget, 0.0001, "distance to target");
        }

        private static void ContinuesBeforeBudgetIsUsed()
        {
            var budget = new PathFollowDistanceBudget(P(0, 0), P(10, 0));
            PathFollowDistanceBudgetResult result = budget.Update(P(5, 0), 3.0);

            AssertEqual(PathFollowDistanceBudgetDecision.Continue, result.Decision, "decision");
            AssertNear(5.0, result.MovedDistance, 0.0001, "moved distance");
            AssertNear(5.0, result.DistanceToTarget, 0.0001, "distance to target");
        }

        private static void StopsWhenTravelBudgetIsUsed()
        {
            var budget = new PathFollowDistanceBudget(P(0, 0), P(10, 0));
            budget.Update(P(0, 4), 3.0);
            budget.Update(P(0, 8), 3.0);
            PathFollowDistanceBudgetResult result = budget.Update(P(0, 11), 3.0);

            AssertEqual(PathFollowDistanceBudgetDecision.TravelBudgetExceeded, result.Decision, "decision");
            AssertNear(11.0, result.MovedDistance, 0.0001, "moved distance");
            AssertTrue(result.DistanceToTarget > 3.0, "distance to target should still be outside reach distance");
        }

        private static void FlowArrivesBeforeBudgetStop()
        {
            var budget = new PathFollowDistanceBudget(P(0, 0), P(10, 0));
            AssertEqual(PathFollowDistanceBudgetDecision.Continue, budget.Update(P(4, 0), 3.0).Decision, "step 1");
            AssertEqual(PathFollowDistanceBudgetDecision.Arrived, budget.Update(P(7.1, 0), 3.0).Decision, "step 2");
        }

        private static void FlowStopsAfterOvershoot()
        {
            var budget = new PathFollowDistanceBudget(P(0, 0), P(10, 0));
            AssertEqual(PathFollowDistanceBudgetDecision.Continue, budget.Update(P(6, 0), 3.0).Decision, "step 1");
            PathFollowDistanceBudgetResult result = budget.Update(P(14, 0), 3.0);

            AssertEqual(PathFollowDistanceBudgetDecision.TravelBudgetExceeded, result.Decision, "step 2");
            AssertNear(14.0, result.MovedDistance, 0.0001, "moved distance");
            AssertNear(4.0, result.DistanceToTarget, 0.0001, "distance to target");
        }

        private static PathFollowBudgetPoint P(double x, double y)
        {
            return new PathFollowBudgetPoint(x, y);
        }

        private static void AssertEqual<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(name + " expected " + expected + " but got " + actual);
            }
        }

        private static void AssertNear(double expected, double actual, double tolerance, string name)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(name + " expected " + expected.ToString("F4") + " but got " + actual.ToString("F4"));
            }
        }

        private static void AssertTrue(bool condition, string name)
        {
            if (!condition)
            {
                throw new InvalidOperationException(name);
            }
        }
    }
}
