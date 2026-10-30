using System.Security.Cryptography;
using System.Text;

namespace AntCloud.Core.Auth;

/// Keeps the refresh token. On Windows it's encrypted with DPAPI for the current user, so another account
/// (or a copied file) can't read it; AntCloud's own client keeps its AES key in plain text beside the token.
/// Elsewhere (macOS, for development) it's a file only the user can read.
static class SecretStore
{
    private static string PathFor(string name) => Path.Combine(Config.DataDir, $"{name}.secret");
    private static readonly byte[] Entropy = "co.antcloud.windows"u8.ToArray();

    public static void Set(string name, string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        if (OperatingSystem.IsWindows()) data = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        var path = PathFor(name);
        File.WriteAllBytes(path, data);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static string? Get(string name)
    {
        try
        {
            var data = File.ReadAllBytes(PathFor(name));
            if (OperatingSystem.IsWindows()) data = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Delete(string name)
    {
        try { File.Delete(PathFor(name)); } catch (IOException) { }
    }
}
