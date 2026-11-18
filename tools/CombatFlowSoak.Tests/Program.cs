using System.Diagnostics;

var directory = Path.Combine(Path.GetTempPath(), "RoadhogSoakIo-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "report.json");
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
    passed++;
}

try
{
    await File.WriteAllTextAsync(path, "old");
    var oldTemporary = Path.Combine(directory, "old-writer.tmp");
    await File.WriteAllTextAsync(oldTemporary, "new");
    using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        Exception? observed = null;
        try { File.Move(oldTemporary, path, overwrite: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { observed = error; }
        Check(observed is not null, "old atomic replacement reproduces Windows reader conflict");
        Console.WriteLine($"Reproduced {observed!.GetType().Name}, HResult=0x{observed.HResult:X8}");
    }
    File.Delete(oldTemporary);

    var publisher = new ReportFilePublisher();
    using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        var elapsed = Stopwatch.StartNew();
        var result = await publisher.TryWriteAsync(path, "blocked");
        Check(!result && publisher.Status.FailureCount == 1 && elapsed.Elapsed < TimeSpan.FromSeconds(5) &&
              await File.ReadAllTextAsync(path) == "old" && Directory.GetFiles(directory, "*.tmp").Length == 0,
            "blocked telemetry yields in bounded time without throwing or corrupting the previous report");
        var strictFailed = false;
        try { await AtomicJsonFile.WriteAsync(path, "stop-request"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { strictFailed = true; }
        Check(strictFailed, "explicit commands do not silently discard a failed write");
    }
    Check(await publisher.TryWriteAsync(path, "recovered") && await File.ReadAllTextAsync(path) == "recovered" &&
          publisher.Status.FailureCount == 1, "publication recovers on the next sample and retains failure evidence");

    using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    {
        Check(await publisher.TryWriteAsync(path, "shared-reader") && await File.ReadAllTextAsync(path) == "shared-reader",
            "delete-sharing observer permits atomic report replacement");
    }
    using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        var write = publisher.TryWriteAsync(path, "retry-recovered");
        await Task.Delay(70);
        reader.Dispose();
        Check(await write && await File.ReadAllTextAsync(path) == "retry-recovered" && publisher.Status.FailureCount == 1,
            "brief reader conflicts recover within the bounded retry");
    }
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        var cancelled = false;
        try { await publisher.TryWriteAsync(path, "cancelled", cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && Directory.GetFiles(directory, "*.tmp").Length == 0,
            "cancellation propagates and cleans temporary report files");
    }
    Console.WriteLine($"All {passed} soak I/O tests passed.");
}
finally
{
    // Delete only files created by this test, then the now-empty unique directory.
    foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
    Directory.Delete(directory);
}
