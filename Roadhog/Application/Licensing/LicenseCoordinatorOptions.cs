namespace Roadhog.Application.Licensing;

public sealed class LicenseCoordinatorOptions
{
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(30);

    public int HeartbeatRetryCount { get; set; } = 3;

    public TimeSpan HeartbeatRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public string ClientVersion { get; set; } = "unknown";
}
