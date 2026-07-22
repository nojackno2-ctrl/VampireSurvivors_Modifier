namespace VSModifier.Memory.ProcessMemory;

public static class PointerChain
{
    public static nint Resolve(IMemoryAccessor memory, nint baseAddress, IReadOnlyList<long> offsets)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(offsets);
        if (baseAddress == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseAddress));
        }

        if (offsets.Count == 0)
        {
            return baseAddress;
        }

        nint current = baseAddress;
        for (int index = 0; index < offsets.Count - 1; index++)
        {
            current = memory.Read<nint>(current + checked((nint)offsets[index]));
            if (current == 0)
            {
                throw new InvalidDataException($"指標鏈在第 {index + 1} 層得到 null。");
            }
        }

        return current + checked((nint)offsets[^1]);
    }
}
