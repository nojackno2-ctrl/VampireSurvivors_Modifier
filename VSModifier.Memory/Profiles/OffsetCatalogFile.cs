using System.Security.Cryptography;

namespace VSModifier.Memory.Profiles;

public sealed class OffsetCatalogFile
{
    private readonly string _path;
    private string? _observedSha256;

    public OffsetCatalogFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public OffsetCatalog Reload()
    {
        byte[] content = File.ReadAllBytes(_path);
        _observedSha256 = CalculateSha256(content);
        return OffsetCatalog.Parse(content);
    }

    public bool HasChanged()
    {
        try
        {
            byte[] content = File.ReadAllBytes(_path);
            return !string.Equals(
                _observedSha256,
                CalculateSha256(content),
                StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string CalculateSha256(ReadOnlySpan<byte> content)
    {
        return Convert.ToHexString(SHA256.HashData(content));
    }
}
