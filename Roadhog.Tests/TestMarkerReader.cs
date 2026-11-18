internal static class TestMarkerReader
{
    // The worker appends the observation marker while the parent polls it. Keep the
    // original complete-file read and assertions; retry only Windows sharing contention.
    internal static string[] ReadAllLines(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return File.ReadAllLines(path); }
            catch (IOException error) when (attempt < 20 && (error.HResult & 0xffff) is 32 or 33)
            {
                Thread.Sleep(5);
            }
        }
    }
}
