namespace Roadhog.Core.Accounts;

public sealed record RoadhogGlobalSettings
{
    public const int DmaTickPeriodMs = 50;
    public const int DefaultReadCacheMs = 150;
    public const int DefaultNoKillReturnMinutes = 20;
    public const int DefaultReviveDelaySeconds = 10;
    public const int DefaultLogRetentionHours = 24;
    public const int DefaultLogDirectoryMiB = 64;
    public const int MaxNoKillReturnMinutes = 24 * 60;
    public const int MaxReviveDelaySeconds = 60;
    public static readonly int MaxLogRetentionHours = (int)TimeSpan.MaxValue.TotalHours;

    public int ReadCacheMs { get; init; } = DefaultReadCacheMs;
    public int? NoKillReturnMinutes { get; init; }
    public int? ReviveDelaySeconds { get; init; }
    public int LogRetentionHours { get; init; } = DefaultLogRetentionHours;
    public int LogDirectoryMiB { get; init; } = DefaultLogDirectoryMiB;

    public string? Validate()
    {
        if (ReadCacheMs <= 0 || ReadCacheMs % DmaTickPeriodMs != 0)
            return $"缓存须为 {DmaTickPeriodMs} ms 的正整数倍。";
        if (NoKillReturnMinutes is < 0 or > MaxNoKillReturnMinutes)
            return $"无击杀回城时间必须在 0 至 {MaxNoKillReturnMinutes} 分钟之间。";
        if (ReviveDelaySeconds is < 0 or > MaxReviveDelaySeconds)
            return $"复活等待时间必须在 0 至 {MaxReviveDelaySeconds} 秒之间。";
        if (LogRetentionHours <= 0 || LogRetentionHours > MaxLogRetentionHours)
            return "日志保留时长必须是有效的正整数小时。";
        return LogDirectoryMiB <= 0 ? "日志目录上限必须是正整数 MiB。" : null;
    }
}
