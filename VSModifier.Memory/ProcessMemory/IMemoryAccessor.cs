namespace VSModifier.Memory.ProcessMemory;

public interface IMemoryAccessor
{
    byte[] ReadBytes(nint address, int length);

    void WriteBytes(nint address, ReadOnlySpan<byte> bytes);
}

public interface IProtectedMemoryAccessor : IMemoryAccessor
{
    void WriteCodeBytes(nint address, ReadOnlySpan<byte> bytes);
}
