using System.Runtime.InteropServices;

namespace VSModifier.Memory.ProcessMemory;

public static class MemoryAccessorExtensions
{
    public static T Read<T>(this IMemoryAccessor accessor, nint address) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(accessor);
        Span<byte> bytes = stackalloc byte[Marshal.SizeOf<T>()];
        accessor.ReadBytes(address, bytes);
        return MemoryMarshal.Read<T>(bytes);
    }

    public static void Write<T>(this IMemoryAccessor accessor, nint address, T value) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(accessor);
        Span<byte> bytes = stackalloc byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        accessor.WriteBytes(address, bytes);
    }
}
