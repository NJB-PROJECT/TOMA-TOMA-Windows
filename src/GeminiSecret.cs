using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
namespace Toma.Windows;
internal static class ProviderSecrets
{
    private static string PathName(string provider) { if (!AiProviders.Ids.Contains(provider)) throw new ArgumentException("Unknown provider"); return Path.Combine(Storage.DirectoryPath, provider + "-key.dpapi"); }
    public static bool Exists(string provider) => File.Exists(PathName(provider));
    public static string Read(string provider)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName(provider)), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
    public static void Save(string provider, string key)
    {
        byte[] plain = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Storage.DirectoryPath);
            File.WriteAllBytes(PathName(provider) + ".tmp", encrypted);
            File.Move(PathName(provider) + ".tmp", PathName(provider), true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static void Remove(string provider) { if (File.Exists(PathName(provider))) File.Delete(PathName(provider)); }
}

internal static class GeminiSecret { public static bool Exists => ProviderSecrets.Exists("gemini"); public static string Read() => ProviderSecrets.Read("gemini"); public static void Save(string key) => ProviderSecrets.Save("gemini", key); public static void Remove() => ProviderSecrets.Remove("gemini"); }
