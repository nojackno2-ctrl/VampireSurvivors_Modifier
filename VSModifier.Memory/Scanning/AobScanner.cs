using VSModifier.Memory.ProcessMemory;

namespace VSModifier.Memory.Scanning;

public static class AobScanner
{
    public static IReadOnlyList<nint> Scan(
        IMemoryAccessor memory,
        nint startAddress,
        int size,
        AobPattern pattern,
        int chunkSize = 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (chunkSize < pattern.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize 不可小於 pattern 長度。");
        }

        HashSet<nint> matches = [];
        int overlap = pattern.Length - 1;
        int step = chunkSize - overlap;
        for (int offset = 0; offset < size; offset += step)
        {
            int readLength = Math.Min(chunkSize, size - offset);
            byte[] chunk = memory.ReadBytes(startAddress + offset, readLength);
            foreach (int localOffset in pattern.FindAll(chunk))
            {
                matches.Add(startAddress + offset + localOffset);
            }

            if (readLength < chunkSize)
            {
                break;
            }
        }

        return matches.Order().ToArray();
    }
}
