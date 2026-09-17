using Smart.ProbeTarget;

using var shutdown = new CancellationTokenSource();
var cancellationGate = new object(); var listening = true; Task cancellation = Task.CompletedTask;
ConsoleCancelEventHandler cancel = (_, e) =>
{
    e.Cancel = true;
    lock (cancellationGate)
        if (listening && !shutdown.IsCancellationRequested) cancellation = shutdown.CancelAsync();
};
Console.CancelKeyPress += cancel;
try
{
    await ProbeTargetRunner.RunAsync(ProbeTargetOptions.Parse(args), Console.Out, Console.Error, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
catch (Exception error)
{
    Console.Error.WriteLine(error.Message); Environment.ExitCode = 1;
}
finally
{
    Console.CancelKeyPress -= cancel;
    Task pending;
    lock (cancellationGate) { listening = false; pending = cancellation; }
    await pending;
}
