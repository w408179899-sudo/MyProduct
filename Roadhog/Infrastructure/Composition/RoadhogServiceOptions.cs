using Roadhog.Infrastructure.ToolBridge;
using Roadhog.Infrastructure.Processes;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.Composition;

public sealed class RoadhogServiceOptions
{
    public const string ClientRootEnvironmentVariable = "ROADHOG_CLIENT_ROOT";
    public const string ConfigRootEnvironmentVariable = "ROADHOG_CONFIG_ROOT";
    public const string AccountConfigPathEnvironmentVariable = "ROADHOG_ACCOUNT_CONFIG_PATH";
    public const string PathLibraryDirectoryEnvironmentVariable = "ROADHOG_PATH_LIBRARY_DIRECTORY";
    public const string ProfileLibraryDirectoryEnvironmentVariable = "ROADHOG_PROFILE_LIBRARY_DIRECTORY";
    public const string KmBoxNetConfigPathEnvironmentVariable = "ROADHOG_KMBOX_NET_CONFIG_PATH";
    public const string LogDirectoryEnvironmentVariable = "ROADHOG_LOG_DIRECTORY";
    public const string EnableLoggingEnvironmentVariable = "ROADHOG_ENABLE_LOGGING";
    public const string LicenseServerUrlEnvironmentVariable = "ROADHOG_LICENSE_SERVER_URL";
    public const string LicenseCredentialPathEnvironmentVariable = "ROADHOG_LICENSE_CREDENTIAL_PATH";
    public const string LicenseHeartbeatSecondsEnvironmentVariable = "ROADHOG_LICENSE_HEARTBEAT_SECONDS";
    public const string LicenseHeartbeatRetryCountEnvironmentVariable = "ROADHOG_LICENSE_HEARTBEAT_RETRY_COUNT";
    public const string LicenseHeartbeatRetryDelaySecondsEnvironmentVariable = "ROADHOG_LICENSE_HEARTBEAT_RETRY_DELAY_SECONDS";
    public const string LicenseRequestTimeoutSecondsEnvironmentVariable = "ROADHOG_LICENSE_REQUEST_TIMEOUT_SECONDS";

    public bool UseToolTestBridge { get; set; }

    public bool UseMockGameApi { get; set; }

    public bool EnableLogging { get; set; } = true;

    public string AccountConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "accounts.json");

    public string PathLibraryDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "paths");

    public string ProfileLibraryDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "profiles");

    public string KmBoxNetConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "kmbox-net.json");

    public string LogDirectory { get; set; } = Path.Combine(ResolveRoadhogProjectDirectory(), "logs");

    public string LicenseServerUrl { get; set; } = "https://account-auth-server.w408179899.workers.dev/";

    public string LicenseCredentialPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "license.dat");

    public TimeSpan LicenseHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(30);

    public int LicenseHeartbeatRetryCount { get; set; } = 3;

    public TimeSpan LicenseHeartbeatRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan LicenseRequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan AccountWorkerTickInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan AccountWorkerStopTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public bool PollPlayerSnapshotInWorker { get; set; }

    public WindowsHardwareDeviceResolverOptions HardwareResolver { get; } = new();

    public AionProcessResolverOptions ProcessResolver { get; } = new();

    public ToolBridgeOptions ToolTestBridge { get; } = new();

    public AionVmmGameApiOptions AionVmm { get; } = new();

    public KmBoxNetKeyboardInputOptions KmBoxNetInput { get; } = new();

    public static RoadhogServiceOptions FromEnvironment()
    {
        var options = new RoadhogServiceOptions();
        options.ApplyEnvironmentOverrides();
        return options;
    }

    public void ApplyEnvironmentOverrides()
    {
        var clientRoot = ReadPathFromEnvironment(ClientRootEnvironmentVariable);
        if (clientRoot is not null)
        {
            AccountConfigPath = Path.Combine(clientRoot, "config", "accounts.json");
            PathLibraryDirectory = Path.Combine(clientRoot, "config", "paths");
            ProfileLibraryDirectory = Path.Combine(clientRoot, "config", "profiles");
            KmBoxNetConfigPath = Path.Combine(clientRoot, "config", "kmbox-net.json");
            LicenseCredentialPath = Path.Combine(clientRoot, "config", "license.dat");
            LogDirectory = Path.Combine(clientRoot, "logs");
        }

        var configRoot = ReadPathFromEnvironment(ConfigRootEnvironmentVariable);
        if (configRoot is not null)
        {
            AccountConfigPath = Path.Combine(configRoot, "accounts.json");
            PathLibraryDirectory = Path.Combine(configRoot, "paths");
            ProfileLibraryDirectory = Path.Combine(configRoot, "profiles");
            KmBoxNetConfigPath = Path.Combine(configRoot, "kmbox-net.json");
            LicenseCredentialPath = Path.Combine(configRoot, "license.dat");
        }

        AccountConfigPath = ReadPathFromEnvironment(AccountConfigPathEnvironmentVariable) ?? AccountConfigPath;
        PathLibraryDirectory = ReadPathFromEnvironment(PathLibraryDirectoryEnvironmentVariable) ?? PathLibraryDirectory;
        ProfileLibraryDirectory = ReadPathFromEnvironment(ProfileLibraryDirectoryEnvironmentVariable) ?? ProfileLibraryDirectory;
        KmBoxNetConfigPath = ReadPathFromEnvironment(KmBoxNetConfigPathEnvironmentVariable) ?? KmBoxNetConfigPath;
        LicenseCredentialPath = ReadPathFromEnvironment(LicenseCredentialPathEnvironmentVariable) ?? LicenseCredentialPath;
        LogDirectory = ReadPathFromEnvironment(LogDirectoryEnvironmentVariable) ?? LogDirectory;
        EnableLogging = ReadBoolFromEnvironment(EnableLoggingEnvironmentVariable) ?? EnableLogging;
        LicenseServerUrl = ReadTextFromEnvironment(LicenseServerUrlEnvironmentVariable) ?? LicenseServerUrl;
        LicenseHeartbeatInterval = ReadPositiveSecondsFromEnvironment(LicenseHeartbeatSecondsEnvironmentVariable)
            ?? LicenseHeartbeatInterval;
        LicenseHeartbeatRetryCount = ReadNonNegativeIntFromEnvironment(
                LicenseHeartbeatRetryCountEnvironmentVariable,
                maximum: 10)
            ?? LicenseHeartbeatRetryCount;
        LicenseHeartbeatRetryDelay = ReadPositiveSecondsFromEnvironment(
                LicenseHeartbeatRetryDelaySecondsEnvironmentVariable)
            ?? LicenseHeartbeatRetryDelay;
        LicenseRequestTimeout = ReadPositiveSecondsFromEnvironment(LicenseRequestTimeoutSecondsEnvironmentVariable)
            ?? LicenseRequestTimeout;
    }

    private static string ResolveRoadhogProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Roadhog.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        var cwd = new DirectoryInfo(Environment.CurrentDirectory);
        while (cwd is not null)
        {
            var candidate = Path.Combine(cwd.FullName, "Roadhog");
            if (File.Exists(Path.Combine(candidate, "Roadhog.csproj")))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(cwd.FullName, "Roadhog.csproj")))
            {
                return cwd.FullName;
            }

            cwd = cwd.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string? ReadPathFromEnvironment(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()));
    }

    private static bool? ReadBoolFromEnvironment(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (bool.TryParse(trimmed, out var parsed))
        {
            return parsed;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "1" or "yes" or "y" or "on" or "enabled" => true,
            "0" or "no" or "n" or "off" or "disabled" => false,
            _ => null
        };
    }

    private static string? ReadTextFromEnvironment(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TimeSpan? ReadPositiveSecondsFromEnvironment(string variableName)
    {
        var value = ReadTextFromEnvironment(variableName);
        if (!double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds)
            || seconds <= 0
            || seconds > 86_400)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static int? ReadNonNegativeIntFromEnvironment(string variableName, int maximum)
    {
        var value = ReadTextFromEnvironment(variableName);
        if (!int.TryParse(value, out var parsed) || parsed < 0 || parsed > maximum)
        {
            return null;
        }

        return parsed;
    }
}
