namespace Smart.Dma.Worker;

internal static class Program
{
    private static Task<int> Main(string[] args) => args is ["--inventory"]
        ? InventoryCommand.RunAsync() : WorkerServer.RunAsync(args, NativeConnection.Open);
}
