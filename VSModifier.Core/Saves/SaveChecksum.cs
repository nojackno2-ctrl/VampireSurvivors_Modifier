using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VSModifier.Core.Saves;

public static partial class SaveChecksum
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    [GeneratedRegex("(\\\"checksum\\\"\\s*:\\s*\\\")[a-fA-F0-9]{64}(\\\")", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumRegex();

    public static bool TryRead(string json, out string checksum)
    {
        ArgumentNullException.ThrowIfNull(json);
        Match match = ChecksumRegex().Match(json);
        if (!match.Success)
        {
            checksum = string.Empty;
            return false;
        }

        int valueStart = match.Groups[1].Index + match.Groups[1].Length;
        checksum = json.Substring(valueStart, 64).ToLowerInvariant();
        return true;
    }

    public static string Calculate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Match match = ChecksumRegex().Match(json);
        if (!match.Success)
        {
            throw new InvalidDataException("找不到 64 字元的 checksum 欄位。");
        }

        string checksumInput = ChecksumRegex().Replace(json, "$1$2", 1);
        byte[] hash = SHA256.HashData(Utf8WithoutBom.GetBytes(checksumInput));
        return Convert.ToHexStringLower(hash);
    }

    public static bool IsValid(string json)
    {
        return TryRead(json, out string stored)
            && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(stored),
                Convert.FromHexString(Calculate(json)));
    }

    public static string Apply(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Match match = ChecksumRegex().Match(json);
        if (!match.Success)
        {
            throw new InvalidDataException("找不到 64 字元的 checksum 欄位。");
        }

        string calculated = Calculate(json);
        return ChecksumRegex().Replace(
            json,
            match => $"{match.Groups[1].Value}{calculated}{match.Groups[2].Value}",
            1);
    }
}
