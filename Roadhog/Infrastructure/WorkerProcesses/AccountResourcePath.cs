namespace Roadhog.Infrastructure.WorkerProcesses;

/// <summary>Account overrides are relative to accounts.json, independent of a child process's working directory.</summary>
internal static class AccountResourcePath
{
    public static string Resolve(string? accountPath, string fallbackPath, string accountConfigPath) =>
        string.IsNullOrWhiteSpace(accountPath)
            ? Path.GetFullPath(fallbackPath)
            : Path.GetFullPath(accountPath, Path.GetDirectoryName(Path.GetFullPath(accountConfigPath))!);

    public static void ValidateLaunchOverride(string? accountPath, string effectivePath, string accountConfigPath, string resource)
    {
        if (string.IsNullOrWhiteSpace(accountPath)) return;
        if (string.IsNullOrWhiteSpace(effectivePath) || !string.Equals(Resolve(accountPath, effectivePath, accountConfigPath),
                Path.GetFullPath(effectivePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("账号" + resource + "来源与后台启动配置不一致。");
    }
}
