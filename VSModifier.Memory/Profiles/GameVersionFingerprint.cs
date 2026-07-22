using System.Security.Cryptography;

namespace VSModifier.Memory.Profiles;

public sealed record GameVersionFingerprint(
    string GameAssemblySha256,
    string UnityPlayerSha256,
    string Il2CppMetadataSha256)
{
    public static GameVersionFingerprint Calculate(
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath)
    {
        return new GameVersionFingerprint(
            CalculateSha256(gameAssemblyPath),
            CalculateSha256(unityPlayerPath),
            CalculateSha256(metadataPath));
    }

    private static string CalculateSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
