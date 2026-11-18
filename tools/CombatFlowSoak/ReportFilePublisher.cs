internal sealed class ReportPublicationStatus
{
    public int FailureCount { get; set; }
    public string? LastFailureType { get; set; }
    public int? LastFailureHResult { get; set; }
    public DateTimeOffset? LastFailureAtUtc { get; set; }
}

internal sealed class ReportFilePublisher
{
    public ReportPublicationStatus Status { get; } = new();

    // Telemetry publication must not control the account's lifecycle. Startup and
    // explicit stop commands use the strict writer instead of this best-effort path.
    public async Task<bool> TryWriteAsync(string path, string content, CancellationToken token = default)
    {
        try
        {
            await AtomicJsonFile.WriteAsync(path, content, token);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status.FailureCount++;
            Status.LastFailureType = error.GetType().Name;
            Status.LastFailureHResult = error.HResult;
            Status.LastFailureAtUtc = DateTimeOffset.UtcNow;
            return false;
        }
    }
}
