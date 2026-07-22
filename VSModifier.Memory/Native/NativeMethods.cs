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

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(ProcessAccess desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        byte[] buffer,
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
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushInstructionCache(SafeProcessHandle process, nint baseAddress, nuint size);
}
