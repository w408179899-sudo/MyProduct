using Microsoft.Win32;
using Roadhog.Core.Accounts;

namespace Roadhog.Infrastructure.Hardware;

internal static class HardwareVerificationSession
{
    public const string RequiredMessage = "本次开机尚未确认硬件连接。请打开“设备/角色”，重新读取角色、勾选确认并保存硬件配置后再启动。";
    private static readonly Lazy<string> Session = new(() => ReadOrCreate(@"Software\Roadhog\HardwareVerificationBootV1"));
    public static string CurrentId => Session.Value;
    public static bool IsCurrent(AccountConfig account) => IsCurrent(account, CurrentId);
    internal static bool IsCurrent(AccountConfig account, string currentId) => !string.IsNullOrWhiteSpace(currentId)
        && !string.IsNullOrWhiteSpace(account.CharacterName)
        && string.Equals(account.HardwareVerificationSessionId, currentId, StringComparison.Ordinal);

    // Volatile registry data survives client restarts, but is discarded on Windows restart or user-hive unload.
    // https://learn.microsoft.com/dotnet/api/microsoft.win32.registryoptions
    internal static string ReadOrCreate(string path)
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            using var mutex = new Mutex(false, @"Global\Roadhog.HardwareVerificationBootV1." + identity);
            var held = false;
            try
            {
                try { held = mutex.WaitOne(TimeSpan.FromSeconds(3)); }
                catch (AbandonedMutexException) { held = true; }
                if (!held) return string.Empty;
                using var key = Registry.CurrentUser.CreateSubKey(path, writable: true, RegistryOptions.Volatile);
                if (key is null) return string.Empty;
                if (key.GetValue("SessionId") is string existing && Guid.TryParseExact(existing, "N", out _)) return existing;
                var created = Guid.NewGuid().ToString("N");
                key.SetValue("SessionId", created, RegistryValueKind.String);
                return created;
            }
            finally { if (held) mutex.ReleaseMutex(); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return string.Empty; } // An unavailable session must never authorize a start.
    }
}
