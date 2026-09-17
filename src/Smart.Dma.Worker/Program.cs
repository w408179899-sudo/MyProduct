namespace Smart.Dma.Worker;

internal static class Program
{
    private static Task<int> Main(string[] args) => WorkerServer.RunAsync(args, NativeConnection.Open);
}
