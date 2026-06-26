using System;

namespace Tool
{
    internal struct CameraTurnVerificationResult
    {
        public bool YawImproved;
        public bool PitchImproved;
        public bool AnyImproved;
        public bool YawOvershot;
        public bool PitchOvershot;
        public double BeforeYawAbs;
        public double AfterYawAbs;
        public double BeforePitchAbs;
        public double AfterPitchAbs;
    }

    internal static class CameraTurnVerification
    {
        public static CameraTurnVerificationResult Verify(
            double beforeYawError,
            double beforePitchError,
            double afterYawError,
            double afterPitchError)
        {
            double beforeYawAbs = Math.Abs(beforeYawError);
            double afterYawAbs = Math.Abs(afterYawError);
            double beforePitchAbs = Math.Abs(beforePitchError);
            double afterPitchAbs = Math.Abs(afterPitchError);

            bool yawWasMeaningful = beforeYawAbs > 0.0001;
            bool pitchWasMeaningful = beforePitchAbs > 0.0001;
            bool yawImproved = !yawWasMeaningful || afterYawAbs < beforeYawAbs;
            bool pitchImproved = !pitchWasMeaningful || afterPitchAbs < beforePitchAbs;

            return new CameraTurnVerificationResult
            {
                YawImproved = yawImproved,
                PitchImproved = pitchImproved,
                AnyImproved = (yawWasMeaningful && yawImproved) || (pitchWasMeaningful && pitchImproved),
                YawOvershot = yawWasMeaningful && beforeYawError * afterYawError < 0.0,
                PitchOvershot = pitchWasMeaningful && beforePitchError * afterPitchError < 0.0,
                BeforeYawAbs = beforeYawAbs,
                AfterYawAbs = afterYawAbs,
                BeforePitchAbs = beforePitchAbs,
                AfterPitchAbs = afterPitchAbs
            };
        }
    }
}
