using System.Security.Cryptography;

namespace VSModifier.Memory.Profiles;

public static class GameAssemblyFingerprint
{
    public static string CalculateSha256(string gameAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameAssemblyPath);
        using FileStream stream = new(gameAssemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
