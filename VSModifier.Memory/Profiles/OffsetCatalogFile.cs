using System.Security.Cryptography;

namespace VSModifier.Memory.Profiles;

public sealed class OffsetCatalogFile
{
    private readonly string _path;
    private string? _observedSha256;
    private DateTime _observedWriteTimeUtc;
    private long _observedLength = -1;

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
        RecordStamp();
        return OffsetCatalog.Parse(content);
    }

    /// <summary>
    /// 由 UI 的每秒計時器呼叫。先比對寫入時間與長度；只有戳記變動時才重讀內容並以 SHA-256 定案，
    /// 避免每秒都完整讀檔加雜湊。
    /// </summary>
    public bool HasChanged()
    {
        try
        {
            FileInfo info = new(_path);
            if (info.Exists
                && info.LastWriteTimeUtc == _observedWriteTimeUtc
                && info.Length == _observedLength)
            {
                return false;
            }

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

    private void RecordStamp()
    {
        try
        {
            FileInfo info = new(_path);
            _observedWriteTimeUtc = info.LastWriteTimeUtc;
            _observedLength = info.Length;
        }
        catch (IOException)
        {
            _observedLength = -1;
        }
        catch (UnauthorizedAccessException)
        {
            _observedLength = -1;
        }
    }

    private static string CalculateSha256(ReadOnlySpan<byte> content)
    {
        return Convert.ToHexString(SHA256.HashData(content));
    }
}
