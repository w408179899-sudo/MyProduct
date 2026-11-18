using System.Text;

internal static class AtomicJsonFile
{
    public static async Task WriteAsync(string path, string content, CancellationToken token = default)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), token);
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
                    else File.Move(temporary, path);
                    return;
                }
                catch (Exception error) when (attempt < 4 && error is IOException or UnauthorizedAccessException)
                {
                    // Windows readers without FileShare.Delete can briefly block replacement.
                    await Task.Delay(50, token);
                }
            }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
