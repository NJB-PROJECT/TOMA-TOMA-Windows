using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace Toma.Windows;
internal static class GeminiSecret
{
    private static string PathName => Path.Combine(Storage.DirectoryPath, "gemini-key.dpapi");
    public static bool Exists => File.Exists(PathName);
    public static string Read()
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
    public static void Save(string key)
    {
        byte[] plain = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Storage.DirectoryPath);
            File.WriteAllBytes(PathName + ".tmp", encrypted);
            File.Move(PathName + ".tmp", PathName, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static void Remove() { if (File.Exists(PathName)) File.Delete(PathName); }
}
