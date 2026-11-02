using System;
using System.Security.Cryptography;
using System.Text;

namespace DDLDesk;

// DPAPI binds the ciphertext to the Windows user profile. No encryption key
// is shipped with the application or stored beside the model configuration.
public static class UserKeyProtection
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ReadingDesk.DeepSeek.ApiKey.v1");

    public static string? Protect(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("密钥保存需要 Windows 用户数据保护。");
        byte[] plain = Encoding.UTF8.GetBytes(key.Trim());
        try { return Convert.ToBase64String(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static string Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("密钥读取需要 Windows 用户数据保护。");
        if (ciphertext.Length > 16384) throw new CryptographicException("密钥密文无效。");
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(Convert.FromBase64String(ciphertext), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }
}
