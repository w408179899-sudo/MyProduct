namespace Roadhog.Infrastructure.ToolBridge;

public sealed class ToolBridgeOptions
{
    public string? ToolExecutablePath { get; set; }

    public string ProcessName { get; set; } = "Aion.bin";

    public string ModuleName { get; set; } = "Game.dll";

    public string VmmDevice { get; set; } = "fpga";

    public string? MemProcFsHome { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(4);

    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.OrdinalIgnoreCase);
}
