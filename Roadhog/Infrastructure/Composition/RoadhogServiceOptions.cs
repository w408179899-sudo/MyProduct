using Roadhog.Infrastructure.ToolBridge;
using Roadhog.Infrastructure.Processes;
using Roadhog.Infrastructure.Hardware;

namespace Roadhog.Infrastructure.Composition;

public sealed class RoadhogServiceOptions
{
    public bool UseToolTestBridge { get; set; }

    public string AccountConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "accounts.json");

    public TimeSpan AccountWorkerTickInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan AccountWorkerStopTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public bool PollPlayerSnapshotInWorker { get; set; }

    public WindowsHardwareDeviceResolverOptions HardwareResolver { get; } = new();

    public AionProcessResolverOptions ProcessResolver { get; } = new();

    public ToolBridgeOptions ToolTestBridge { get; } = new();
}
