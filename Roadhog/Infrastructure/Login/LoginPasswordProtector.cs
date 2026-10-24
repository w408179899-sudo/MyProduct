using System.Security.Cryptography;
using System.Text;

namespace Roadhog.Infrastructure.Login;

internal static class LoginPasswordProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Roadhog.GameLogin.Password.v1");

    public static string Protect(string password)
    {
        if (password.Length == 0) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(password);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Unprotect(string ciphertext)
    {
        if (ciphertext.Length == 0) return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ciphertext), Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new InvalidOperationException("保存的游戏密码无法在当前 Windows 用户下解密，请重新输入并保存。", exception);
        }
    }
}
