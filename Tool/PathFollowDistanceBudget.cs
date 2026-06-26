using System;

namespace Tool
{
    internal struct PathFollowBudgetPoint
    {
        public double X;
        public double Y;

        public PathFollowBudgetPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    internal enum PathFollowDistanceBudgetDecision
    {
        Continue,
        Arrived,
        TravelBudgetExceeded
    }

    internal struct PathFollowDistanceBudgetResult
    {
        public PathFollowDistanceBudgetDecision Decision;
        public double TotalDistance;
        public double MovedDistance;
        public double DistanceToTarget;
        public double RemainingBudget;
    }

    internal sealed class PathFollowDistanceBudget
    {
        private PathFollowBudgetPoint _lastPoint;

        public PathFollowBudgetPoint StartPoint { get; private set; }
        public PathFollowBudgetPoint TargetPoint { get; private set; }
        public double TotalDistance { get; private set; }
        public double MovedDistance { get; private set; }

        public PathFollowDistanceBudget(PathFollowBudgetPoint startPoint, PathFollowBudgetPoint targetPoint)
        {
            StartPoint = startPoint;
            TargetPoint = targetPoint;
            _lastPoint = startPoint;
            TotalDistance = Distance(startPoint, targetPoint);
            MovedDistance = 0.0;
        }

        public PathFollowDistanceBudgetResult Update(PathFollowBudgetPoint currentPoint, double reachDistance)
        {
            double stepDistance = Distance(_lastPoint, currentPoint);
            if (stepDistance > 0.0)
            {
                MovedDistance += stepDistance;
                _lastPoint = currentPoint;
            }

            double distanceToTarget = Distance(currentPoint, TargetPoint);
            double remainingBudget = Math.Max(0.0, TotalDistance - MovedDistance);
            PathFollowDistanceBudgetDecision decision = PathFollowDistanceBudgetDecision.Continue;
            if (distanceToTarget <= reachDistance)
            {
                decision = PathFollowDistanceBudgetDecision.Arrived;
            }
            else if (MovedDistance >= TotalDistance)
            {
                decision = PathFollowDistanceBudgetDecision.TravelBudgetExceeded;
            }

            return new PathFollowDistanceBudgetResult
            {
                Decision = decision,
                TotalDistance = TotalDistance,
                MovedDistance = MovedDistance,
                DistanceToTarget = distanceToTarget,
                RemainingBudget = remainingBudget
            };
        }

        public static double Distance(PathFollowBudgetPoint a, PathFollowBudgetPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
