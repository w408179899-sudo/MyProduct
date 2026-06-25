namespace Roadhog.Core.Processes;

public sealed record TargetProcessInfo(
    int ProcessId,
    string ProcessName,
    string? MainWindowTitle,
    string? FileName,
    DateTimeOffset? StartedAt);
