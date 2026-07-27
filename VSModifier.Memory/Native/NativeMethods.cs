using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace VSModifier.Memory.Native;

[Flags]
internal enum ProcessAccess : uint
{
    VirtualMemoryOperation = 0x0008,
    VirtualMemoryRead = 0x0010,
    VirtualMemoryWrite = 0x0020,
    QueryInformation = 0x0400
}

internal enum MemoryProtection : uint
{
    ExecuteReadWrite = 0x40
}

[Flags]
internal enum MemoryAllocationType : uint
{
    Commit = 0x1000,
    Reserve = 0x2000
}

[Flags]
internal enum MemoryFreeType : uint
{
    Release = 0x8000
}

internal enum MemoryState : uint
{
    Free = 0x10000
}

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryBasicInformation
{
    internal nint BaseAddress;
    internal nint AllocationBase;
    internal uint AllocationProtect;
    private readonly uint Alignment1;
    internal nuint RegionSize;
    internal uint State;
    internal uint Protect;
    internal uint Type;
    private readonly uint Alignment2;
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(ProcessAccess desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        ref byte buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        in byte buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualProtectEx(
        SafeProcessHandle process,
        nint address,
        nuint size,
        MemoryProtection newProtection,
        out uint oldProtection);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint VirtualAllocEx(
        SafeProcessHandle process,
        nint address,
        nuint size,
        MemoryAllocationType allocationType,
        MemoryProtection protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFreeEx(
        SafeProcessHandle process,
        nint address,
        nuint size,
        MemoryFreeType freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(
        SafeProcessHandle process,
        nint address,
        out MemoryBasicInformation buffer,
        nuint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushInstructionCache(SafeProcessHandle process, nint baseAddress, nuint size);
}
