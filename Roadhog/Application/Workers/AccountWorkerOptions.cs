namespace Roadhog.Application.Workers;

public sealed class AccountWorkerOptions
{
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public bool PollPlayerSnapshot { get; set; }
}
