using System;

namespace Tool
{
    internal static class PathFollowMoveControl
    {
        public static bool ShouldRestartMoveForYaw(bool isMoving, double yawErrorDegrees, double restartYawThresholdDegrees)
        {
            return isMoving && Math.Abs(yawErrorDegrees) > restartYawThresholdDegrees;
        }

        public static bool ShouldDisableMoveAdjustByDistance(bool isMoving, double distanceToTarget, double disableMoveAdjustDistance)
        {
            return isMoving && distanceToTarget <= disableMoveAdjustDistance;
        }

        public static bool ShouldTurn(
            bool restartMoveForLargeYaw,
            bool moveAdjustDisabledByDistance,
            double yawErrorDegrees,
            double pitchErrorDegrees,
            double yawToleranceDegrees,
            double pitchToleranceDegrees)
        {
            return restartMoveForLargeYaw ||
                   (!moveAdjustDisabledByDistance &&
                    (Math.Abs(yawErrorDegrees) > yawToleranceDegrees ||
                     Math.Abs(pitchErrorDegrees) > pitchToleranceDegrees));
        }
    }
}
