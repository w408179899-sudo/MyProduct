using Roadhog.Application.SemiAuto;
using Roadhog.Core.Model;

internal static class TestCooldownClock
{
    // Keep simulated game ticks independent of the host's uptime and signed wrap point.
    private static readonly uint Offset = unchecked(1_000_000u - (uint)Environment.TickCount64);

    public static uint CurrentTick => ToGameTick(unchecked((uint)Environment.TickCount64));

    public static void Calibrate(SemiAutoCombatState state)
    {
        var osTick = unchecked((uint)Environment.TickCount64);
        var now = DateTimeOffset.Now;
        var marker = new SkillSnapshot(900001, "clock marker", 1, 1, "clock marker", 1, false, 1000, 0);
        state.MarkSkillPressed(marker, now.AddSeconds(1));
        if (!state.TryUpdateCooldownTickCalibration(
            new[] { marker with { CooldownEndTime = unchecked(ToGameTick(osTick) + marker.CooldownDuration) } },
            osTick, now, out _))
            throw new InvalidOperationException("The fixture must calibrate from an observed cooldown advance.");
    }

    private static uint ToGameTick(uint osTick) => unchecked(osTick + Offset);
}
