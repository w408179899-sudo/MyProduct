namespace Roadhog.Core.Api;

public sealed record GameApiReadContext(
    string AccountName,
    int ProcessId,
    string TargetProcessName,
    string VmmDeviceName);
