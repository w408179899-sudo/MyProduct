namespace Roadhog.Application.Workers;

public interface IAccountWorkerLoop
{
    Task RunAsync(AccountWorkerContext context);
}
