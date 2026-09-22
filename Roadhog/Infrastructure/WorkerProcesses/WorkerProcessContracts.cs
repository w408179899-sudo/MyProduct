using Roadhog.Core.Accounts;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed record WorkerServicePaths
{
    public string ClientRoot { get; init; } = string.Empty;
    public string AccountConfigPath { get; init; } = string.Empty;
    public string PathLibraryDirectory { get; init; } = string.Empty;
    public string ProfileLibraryDirectory { get; init; } = string.Empty;
    public string RadarMapDirectory { get; init; } = string.Empty;
    public string BagCleanupNameListPath { get; init; } = string.Empty;
    public string LogDirectory { get; init; } = string.Empty;
    public string LicenseCredentialPath { get; init; } = string.Empty;
    public string OwnerLicenseGrantPath { get; init; } = string.Empty;
    public string LicenseServerUrl { get; init; } = string.Empty;
    public bool EnableLogging { get; init; } = true;
    public TimeSpan LicenseHeartbeatInterval { get; init; } = TimeSpan.FromMinutes(30);
    public int LicenseHeartbeatRetryCount { get; init; } = 3;
    public TimeSpan LicenseHeartbeatRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan LicenseRequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan AccountWorkerTickInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan AccountWorkerStopTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public bool PollPlayerSnapshotInWorker { get; init; }
}

public sealed record WorkerLaunchSpec
{
    public int ProtocolVersion { get; init; } = 1;
    public AccountConfig Account { get; init; } = new();
    public WorkerServicePaths Paths { get; init; } = new();
    public string PipeName { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string LeasePath { get; init; } = string.Empty;
}

public sealed record WorkerDescriptor
{
    public int ProtocolVersion { get; init; } = 1;
    public string InstanceId { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string PipeName { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public DateTimeOffset ProcessStartedAtUtc { get; init; }
}

public sealed record WorkerStatus
{
    public bool InitializationComplete { get; init; }
    public string InstanceId { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public bool Authorized { get; init; }
    public string? AuthorizationError { get; init; }
    public bool IsRunning { get; init; }
    public AccountRuntimeSnapshot? Snapshot { get; init; }
    public DateTimeOffset ReportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public static class WorkerCommands
{
    public const string Status = "$status";
    public const string Start = "$start";
    public const string Stop = "$stop";
    public const string Cleanup = "$cleanup";
    public const string Shutdown = "$shutdown";
    public const string VerifyHardware = "$verifyHardware";
}

public sealed record HardwareVerification(string CharacterName, string HardwareKey, string VmmDeviceName, bool KmBoxConnected, string SessionId = "");
