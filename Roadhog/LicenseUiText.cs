using Roadhog.Application.Licensing;

namespace Roadhog;

internal static class LicenseUiText
{
    public static string Describe(LicenseRuntimeState state)
    {
        return DescribeError(state.ErrorCode);
    }

    public static string DescribeError(string? errorCode)
    {
        return errorCode switch
        {
            null or "" => "需要激活。",
            "INVALID_CDKEY_FORMAT" => "卡密格式不正确。",
            "LICENSE_NOT_FOUND" => "卡密不存在。",
            "LICENSE_NOT_ACTIVATED" => "卡密尚未激活。",
            "LICENSE_NOT_ENABLED" => "卡密尚未启用。",
            "LICENSE_DISABLED" => "卡密已被禁用。",
            "LICENSE_REVOKED" => "卡密已永久废弃。",
            "LICENSE_LOCKED" => "卡密已临时锁定。",
            "LICENSE_EXPIRED" => "卡密已过期。",
            "INSTANCE_MISMATCH" => "卡密已绑定其他客户端。",
            "SESSION_NOT_FOUND" => "授权会话不存在。",
            "SESSION_REVOKED" => "授权会话已被替换或撤销。",
            "SESSION_EXPIRED" => "授权会话已过期。",
            "ACTIVATION_GENERATION_MISMATCH" => "授权已被管理员重置。",
            "LOCAL_LICENSE_ALREADY_CONFIGURED" => "当前客户端已经保存了另一张卡密。",
            "LOCAL_CREDENTIAL_READ_FAILED" => "无法读取本地授权凭证。",
            "LOCAL_CREDENTIAL_WRITE_FAILED" => "无法保存本地授权凭证。",
            "LOCAL_CREDENTIAL_INVALID" => "本地授权凭证已损坏。",
            "DEVICE_IDENTITY_UNAVAILABLE" => "无法读取当前电脑标识。",
            "NETWORK_UNAVAILABLE" => "无法连接授权服务器。",
            "REQUEST_TIMEOUT" => "连接授权服务器超时。",
            "LICENSE_SERVER_UNAVAILABLE" => "授权服务器暂时不可用。",
            "OFFLINE_GRACE_EXPIRED" => "联网验证超时，授权已暂停。",
            "INVALID_SERVER_RESPONSE" => "授权服务器返回了无效响应。",
            "HEARTBEAT_INTERNAL_ERROR" => "授权心跳发生内部错误。",
            _ => "授权失败：" + errorCode
        };
    }

    public static string FormatStatus(LicenseRuntimeState state)
    {
        return state.Kind switch
        {
            LicenseRuntimeStateKind.Checking => "授权检查中",
            LicenseRuntimeStateKind.Authorized => state.LicenseExpiresAt is null
                ? "授权：永久"
                : "授权至 " + state.LicenseExpiresAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
            LicenseRuntimeStateKind.OfflineGrace => "授权网络异常",
            LicenseRuntimeStateKind.ActivationRequired => "授权：未激活",
            LicenseRuntimeStateKind.Denied => "授权：已停止",
            LicenseRuntimeStateKind.Unavailable => "授权：离线",
            _ => "授权：未检查"
        };
    }
}
