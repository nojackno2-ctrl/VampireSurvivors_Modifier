using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VSModifier.Memory.Native;

namespace VSModifier.Memory.ProcessMemory;

public sealed class ProcessMemorySession : IProtectedMemoryAccessor, IRemoteCodeMemory, IDisposable
{
    private readonly Process _process;
    private readonly SafeProcessHandle _handle;
    private readonly Dictionary<string, ProcessModuleInfo> _moduleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _moduleGate = new();
    private bool _disposed;

    private ProcessMemorySession(Process process, SafeProcessHandle handle, bool canWrite)
    {
        _process = process;
        _handle = handle;
        CanWrite = canWrite;
    }

    public int ProcessId => _process.Id;

    public bool CanWrite { get; }

    public bool HasExited
    {
        get
        {
            _process.Refresh();
            return _process.HasExited;
        }
    }

    public static ProcessMemorySession Attach(string processName = "VampireSurvivors")
    {
        return AttachCore(processName, canWrite: true);
    }

    public static ProcessMemorySession AttachReadOnly(string processName = "VampireSurvivors")
    {
        return AttachCore(processName, canWrite: false);
    }

    private static ProcessMemorySession AttachCore(string processName, bool canWrite)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Trainer 僅支援 Windows。");
        }

        Process[] processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            throw new InvalidOperationException($"找不到 {processName} 行程。");
        }

        Process selected = processes.OrderByDescending(process => process.StartTime).First();
        foreach (Process process in processes.Where(process => !ReferenceEquals(process, selected)))
        {
            process.Dispose();
        }

        try
        {
            SafeProcessHandle handle = NativeMethods.OpenProcess(
                ProcessAccess.QueryInformation
                    | ProcessAccess.VirtualMemoryRead
                    | (canWrite ? ProcessAccess.VirtualMemoryOperation | ProcessAccess.VirtualMemoryWrite : 0),
                inheritHandle: false,
                selected.Id);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "無法開啟遊戲行程；請確認權限與行程狀態。");
            }

            return new ProcessMemorySession(selected, handle, canWrite);
        }
        catch
        {
            selected.Dispose();
            throw;
        }
    }

    public nint GetModuleBaseAddress(string moduleName)
    {
        return GetModuleInfo(moduleName).BaseAddress;
    }

    /// <summary>
    /// 模組基底位址在行程存活期間不會變動，因此每個模組只列舉一次；
    /// 鎖值迴圈每 100ms 解析一次位址，重複列舉整份模組表是主要的固定成本。
    /// </summary>
    public ProcessModuleInfo GetModuleInfo(string moduleName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_moduleGate)
        {
            if (_moduleCache.TryGetValue(moduleName, out ProcessModuleInfo? cached))
            {
                return cached;
            }
        }

        foreach (ProcessModule module in _process.Modules)
        {
            using (module)
            {
                if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    ProcessModuleInfo info = new(module.BaseAddress, module.ModuleMemorySize);
                    lock (_moduleGate)
                    {
                        _moduleCache[moduleName] = info;
                    }

                    return info;
                }
            }
        }

        throw new InvalidOperationException($"行程中找不到模組 {moduleName}。");
    }

    public byte[] ReadBytes(nint address, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        byte[] buffer = new byte[length];
        ReadBytes(address, buffer.AsSpan());
        return buffer;
    }

    public void ReadBytes(nint address, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (address == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "記憶體位址不可為 0。");
        }

        if (destination.IsEmpty)
        {
            throw new ArgumentException("讀取長度必須大於 0。", nameof(destination));
        }

        if (!NativeMethods.ReadProcessMemory(
                _handle,
                address,
                ref MemoryMarshal.GetReference(destination),
                (nuint)destination.Length,
                out nuint bytesRead)
            || bytesRead != (nuint)destination.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"讀取位址 0x{address:X} 失敗。");
        }
    }

    public void WriteBytes(nint address, ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWritable();
        WriteCore(address, bytes);
    }

    public void WriteCodeBytes(nint address, ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWritable();
        if (!NativeMethods.VirtualProtectEx(_handle, address, (nuint)bytes.Length, MemoryProtection.ExecuteReadWrite, out uint oldProtection))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法變更程式碼頁保護屬性。");
        }

        try
        {
            WriteCore(address, bytes);
            if (!NativeMethods.FlushInstructionCache(_handle, address, (nuint)bytes.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "無法刷新遊戲指令快取。");
            }
        }
        finally
        {
            _ = NativeMethods.VirtualProtectEx(_handle, address, (nuint)bytes.Length, (MemoryProtection)oldProtection, out _);
        }
    }

    internal nint AllocateExecutableNear(nint targetAddress, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWritable();
        if (targetAddress == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetAddress));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        const long minimumUserAddress = 0x10000;
        const long allocationGranularity = 0x10000;
        long target = targetAddress;
        long lowerBound = Math.Max(minimumUserAddress, target - int.MaxValue + allocationGranularity);
        long upperBound = Math.Min(long.MaxValue - length, target + int.MaxValue - allocationGranularity);
        int querySize = Marshal.SizeOf<MemoryBasicInformation>();
        List<long> candidates = [];
        long cursor = lowerBound;
        while (cursor < upperBound)
        {
            nuint queried = NativeMethods.VirtualQueryEx(
                _handle,
                (nint)cursor,
                out MemoryBasicInformation information,
                (nuint)querySize);
            if (queried == 0)
            {
                break;
            }

            long regionBase = information.BaseAddress;
            long regionSize = checked((long)information.RegionSize);
            long regionEnd = checked(regionBase + regionSize);
            if (information.State == (uint)MemoryState.Free)
            {
                long candidate = AlignUp(Math.Max(regionBase, lowerBound), allocationGranularity);
                long usableEnd = Math.Min(regionEnd, upperBound);
                if (candidate <= usableEnd - length
                    && CanEncodeRelativeJump(targetAddress, (nint)candidate)
                    && CanEncodeRelativeJump((nint)candidate, targetAddress))
                {
                    candidates.Add(candidate);
                }
            }

            long next = Math.Max(regionEnd, cursor + allocationGranularity);
            cursor = next > cursor ? next : checked(cursor + allocationGranularity);
        }

        foreach (long candidate in candidates.OrderBy(address => Math.Abs(address - target)))
        {
            nint allocated = NativeMethods.VirtualAllocEx(
                _handle,
                (nint)candidate,
                (nuint)length,
                MemoryAllocationType.Reserve | MemoryAllocationType.Commit,
                MemoryProtection.ExecuteReadWrite);
            if (allocated != 0
                && CanEncodeRelativeJump(targetAddress, allocated)
                && CanEncodeRelativeJump(allocated, targetAddress))
            {
                return allocated;
            }

            if (allocated != 0)
            {
                _ = NativeMethods.VirtualFreeEx(_handle, allocated, 0, MemoryFreeType.Release);
            }
        }

        throw new InvalidOperationException(
            $"無法在目標位址 0x{targetAddress:X} 的 rel32 範圍內配置可執行記憶體。");
    }

    internal void FreeExecutable(nint address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWritable();
        if (address == 0)
        {
            return;
        }

        if (!NativeMethods.VirtualFreeEx(_handle, address, 0, MemoryFreeType.Release))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"釋放遠端記憶體 0x{address:X} 失敗。");
        }
    }

    nint IRemoteCodeMemory.AllocateExecutableNear(nint targetAddress, int length) =>
        AllocateExecutableNear(targetAddress, length);

    void IRemoteCodeMemory.FreeExecutable(nint address) =>
        FreeExecutable(address);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
        _process.Dispose();
    }

    private void WriteCore(nint address, ReadOnlySpan<byte> bytes)
    {
        if (address == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "記憶體位址不可為 0。");
        }

        if (bytes.IsEmpty)
        {
            throw new ArgumentException("寫入內容不可為空。", nameof(bytes));
        }

        if (!NativeMethods.WriteProcessMemory(
                _handle,
                address,
                in MemoryMarshal.GetReference(bytes),
                (nuint)bytes.Length,
                out nuint bytesWritten)
            || bytesWritten != (nuint)bytes.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"寫入位址 0x{address:X} 失敗。");
        }
    }

    private void EnsureWritable()
    {
        if (!CanWrite)
        {
            throw new InvalidOperationException("此行程工作階段為唯讀，禁止寫入。");
        }
    }

    private static long AlignUp(long value, long alignment)
    {
        long remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static bool CanEncodeRelativeJump(nint source, nint destination)
    {
        long displacement = (long)destination - checked((long)source + 5);
        return displacement is >= int.MinValue and <= int.MaxValue;
    }
}

public sealed record ProcessModuleInfo(nint BaseAddress, int Size);
