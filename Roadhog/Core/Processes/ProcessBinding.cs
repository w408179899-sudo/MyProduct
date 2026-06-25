namespace Roadhog.Core.Processes;

public sealed record ProcessBinding(
    string AccountName,
    int ProcessId,
    string ProcessName,
    DateTimeOffset BoundAt);
