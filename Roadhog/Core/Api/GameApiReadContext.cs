namespace Roadhog.Core.Api;

internal sealed record GameApiReadContext(
    string AccountName,
    int ProcessId,
    string TargetProcessName,
    string VmmDeviceName,
    bool BypassMemoryCache = false);
