namespace Smart.Hosting;

public sealed record DmaWorkerSettings
{
    public string? WorkerPath { get; init; }
    public int StartupTimeoutMs { get; init; } = 30000;
    public int OperationTimeoutMs { get; init; } = 5000;
    public int ShutdownTimeoutMs { get; init; } = 3000;
    public void Validate()
    {
        if (StartupTimeoutMs is < 100 or > 120000 || OperationTimeoutMs is < 100 or > 120000 || ShutdownTimeoutMs is < 100 or > 120000)
            throw new ArgumentException("DMA worker deadlines must be between 100 and 120000 milliseconds.");
        if (WorkerPath is not null && (string.IsNullOrWhiteSpace(WorkerPath) || !Path.IsPathFullyQualified(WorkerPath)))
            throw new ArgumentException("DMA worker path must be an absolute executable path.");
    }
}
