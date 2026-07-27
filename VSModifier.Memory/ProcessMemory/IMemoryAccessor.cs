namespace VSModifier.Memory.ProcessMemory;

public interface IMemoryAccessor
{
    byte[] ReadBytes(nint address, int length);

    void WriteBytes(nint address, ReadOnlySpan<byte> bytes);

    /// <summary>
    /// 讀入呼叫端提供的緩衝區，讓 100ms 鎖值迴圈這類熱路徑不必每次配置新陣列。
    /// </summary>
    void ReadBytes(nint address, Span<byte> destination) =>
        ReadBytes(address, destination.Length).CopyTo(destination);
}

public interface IProtectedMemoryAccessor : IMemoryAccessor
{
    void WriteCodeBytes(nint address, ReadOnlySpan<byte> bytes);
}

internal interface IRemoteCodeMemory : IProtectedMemoryAccessor
{
    nint AllocateExecutableNear(nint targetAddress, int length);

    void FreeExecutable(nint address);
}
