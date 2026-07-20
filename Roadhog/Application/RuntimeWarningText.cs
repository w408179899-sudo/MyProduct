namespace Roadhog.Application;

public static class RuntimeWarningText
{
    private const int MaxDetailLength = 80;

    public static string FromPlayerReadFailure(string? error)
    {
        return Format(error, "读取角色失败");
    }

    public static string FromRuntimeError(string? error)
    {
        return Format(error, "运行异常");
    }

    private static string Format(string? error, string fallback)
    {
        var detail = Normalize(error);
        if (detail.Length == 0)
        {
            return fallback;
        }

        if (Contains(detail, "VMM INIT FAILED") ||
            Contains(detail, "vmm.connection.init_failed"))
        {
            return "VMM连接失败";
        }

        if (Contains(detail, "VMM reconnect cooling down"))
        {
            return "VMM重连等待中";
        }

        if (Contains(detail, "Target process not found"))
        {
            return "游戏进程不存在或已退出";
        }

        if (Contains(detail, "Module not found: Game.dll"))
        {
            return "游戏模块未加载，可能正在登录或切图";
        }

        if (Contains(detail, "failed to read local entity id") ||
            (Contains(detail, "local entity id") && Contains(detail, "was not found")))
        {
            return "读取不到角色，疑似掉线或未进游戏";
        }

        if (Contains(detail, "Multiple") && Contains(detail, "process"))
        {
            return "发现多个游戏进程，账号PID未绑定";
        }

        if (Contains(detail, "PID mismatch"))
        {
            return "游戏进程PID不匹配";
        }

        return fallback + "：" + Truncate(detail, MaxDetailLength);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static bool Contains(string value, string pattern)
    {
        return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 1)] + "...";
    }
}
