namespace Roadhog.Infrastructure.Processes;

public sealed class AionProcessResolverOptions
{
    public string ProcessNameEnvironmentVariable { get; set; } = "VMM_PROCESS";

    public string DefaultProcessName { get; set; } = "Aion.bin";
}
