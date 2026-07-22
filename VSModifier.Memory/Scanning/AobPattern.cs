using System.Globalization;

namespace VSModifier.Memory.Scanning;

public sealed class AobPattern
{
    private AobPattern(byte?[] values)
    {
        Values = values;
    }

    public IReadOnlyList<byte?> Values { get; }

    public int Length => Values.Count;

    public static AobPattern Parse(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        string[] tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new FormatException("AOB pattern 不可為空。");
        }

        byte?[] values = new byte?[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];
            values[index] = token is "?" or "??"
                ? null
                : byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return new AobPattern(values);
    }

    public IReadOnlyList<int> FindAll(ReadOnlySpan<byte> data)
    {
        List<int> matches = [];
        if (data.Length < Values.Count)
        {
            return matches;
        }

        for (int offset = 0; offset <= data.Length - Values.Count; offset++)
        {
            bool matchesAtOffset = true;
            for (int patternIndex = 0; patternIndex < Values.Count; patternIndex++)
            {
                byte? expected = Values[patternIndex];
                if (expected.HasValue && data[offset + patternIndex] != expected.Value)
                {
                    matchesAtOffset = false;
                    break;
                }
            }

            if (matchesAtOffset)
            {
                matches.Add(offset);
            }
        }

        return matches;
    }
}
