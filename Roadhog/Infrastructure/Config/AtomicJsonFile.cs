using System.Text.Json;

namespace Roadhog.Infrastructure.Config;

// The manager is the configuration writer. Readers in account processes must see
// either the previous complete document or the next complete document.
internal static class AtomicJsonFile
{
    public static bool Exists(string path)
    {
        if (File.Exists(path)) return true;
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(path)))) return false;
        // A replacement can briefly hide the directory entry. Do not turn that
        // metadata transition into an empty account list or a missing profile.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Thread.Sleep(1 << attempt);
            if (File.Exists(path)) return true;
        }
        return false;
    }

    public static FileStream OpenRead(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (IOException exception) when (attempt < 20 && (exception.HResult & 0xffff) is 2 or 32 or 33)
            {
                // ReplaceFile briefly owns a metadata handle. A reader arriving
                // inside that window retries missing/sharing errors, never a partial document.
                Thread.Sleep(5);
            }
        }
    }

    public static async Task WriteAsync<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 4096, FileOptions.Asynchronous))
            {
                // SerializeAsync retains the existing UTF-8, no-BOM JSON contract.
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Commit(temporaryPath, fullPath);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void Commit(string temporaryPath, string path)
    {
        // ReplaceFile preserves open readers on Windows. MoveFileEx with replace
        // can fail with access denied even when those readers allow deletion.
        if (File.Exists(path)) File.Replace(temporaryPath, path, destinationBackupFileName: null);
        else File.Move(temporaryPath, path);
    }
}
