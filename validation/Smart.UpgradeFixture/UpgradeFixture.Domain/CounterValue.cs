namespace UpgradeFixture.Domain;

// A deliberately fixed test value, unrelated to any game or device layout.
public sealed record CounterValue(long Value);
public sealed record FixtureSettings(bool Enabled, int IntervalMilliseconds, int Threshold);
