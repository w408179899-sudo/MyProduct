namespace Roadhog.Infrastructure.ToolBridge;

public sealed record ToolCommandOutput(
    ToolApiMode Mode,
    int? ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError)
{
    public bool Success => !TimedOut && ExitCode == 0;
}
