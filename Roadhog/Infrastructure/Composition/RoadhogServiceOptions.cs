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

    public bool UseToolTestBridge { get; set; }

    public bool UseMockGameApi { get; set; }

    public bool EnableLogging { get; set; } = true;

    public string AccountConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "accounts.json");

    public string PathLibraryDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "paths");

    public string ProfileLibraryDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "profiles");

    public string KmBoxNetConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "kmbox-net.json");

    public string LogDirectory { get; set; } = Path.Combine(ResolveRoadhogProjectDirectory(), "logs");

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
            LogDirectory = Path.Combine(clientRoot, "logs");
        }

        var configRoot = ReadPathFromEnvironment(ConfigRootEnvironmentVariable);
        if (configRoot is not null)
        {
            AccountConfigPath = Path.Combine(configRoot, "accounts.json");
            PathLibraryDirectory = Path.Combine(configRoot, "paths");
            ProfileLibraryDirectory = Path.Combine(configRoot, "profiles");
            KmBoxNetConfigPath = Path.Combine(configRoot, "kmbox-net.json");
        }

        AccountConfigPath = ReadPathFromEnvironment(AccountConfigPathEnvironmentVariable) ?? AccountConfigPath;
        PathLibraryDirectory = ReadPathFromEnvironment(PathLibraryDirectoryEnvironmentVariable) ?? PathLibraryDirectory;
        ProfileLibraryDirectory = ReadPathFromEnvironment(ProfileLibraryDirectoryEnvironmentVariable) ?? ProfileLibraryDirectory;
        KmBoxNetConfigPath = ReadPathFromEnvironment(KmBoxNetConfigPathEnvironmentVariable) ?? KmBoxNetConfigPath;
        LogDirectory = ReadPathFromEnvironment(LogDirectoryEnvironmentVariable) ?? LogDirectory;
        EnableLogging = ReadBoolFromEnvironment(EnableLoggingEnvironmentVariable) ?? EnableLogging;
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
}
