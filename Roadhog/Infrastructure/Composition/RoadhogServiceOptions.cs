using Roadhog.Infrastructure.ToolBridge;
using Roadhog.Infrastructure.Processes;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.Composition;

public sealed class RoadhogServiceOptions
{
    public bool UseToolTestBridge { get; set; }

    public bool UseMockGameApi { get; set; }

    public string AccountConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "accounts.json");

    public string PathLibraryDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "paths");

    public string LogDirectory { get; set; } = Path.Combine(ResolveRoadhogProjectDirectory(), "logs");

    public TimeSpan AccountWorkerTickInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan AccountWorkerStopTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public bool PollPlayerSnapshotInWorker { get; set; }

    public WindowsHardwareDeviceResolverOptions HardwareResolver { get; } = new();

    public AionProcessResolverOptions ProcessResolver { get; } = new();

    public ToolBridgeOptions ToolTestBridge { get; } = new();

    public AionVmmGameApiOptions AionVmm { get; } = new();

    public KmBoxNetKeyboardInputOptions KmBoxNetInput { get; } = new();

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
}
