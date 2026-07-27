using VSModifier.Memory.ProcessMemory;

namespace VSModifier.Memory.Patching;

internal delegate RemoteHookPayload RemoteHookPayloadBuilder(
    nint caveAddress,
    nint targetAddress,
    ReadOnlySpan<byte> originalBytes);

internal sealed class RemoteCodeHook : IDisposable
{
    private const int AllocationSize = 512;
    private readonly IRemoteCodeMemory _memory;
    private readonly nint _targetAddress;
    private readonly byte[] _originalBytes;
    private readonly IReadOnlyDictionary<string, int> _dataOffsets;
    private nint _caveAddress;
    private bool _installed;

    private RemoteCodeHook(
        IRemoteCodeMemory memory,
        nint targetAddress,
        byte[] originalBytes,
        nint caveAddress,
        IReadOnlyDictionary<string, int> dataOffsets)
    {
        _memory = memory;
        _targetAddress = targetAddress;
        _originalBytes = originalBytes;
        _caveAddress = caveAddress;
        _dataOffsets = dataOffsets;
        _installed = true;
    }

    internal static RemoteCodeHook Install(
        IRemoteCodeMemory memory,
        nint targetAddress,
        ReadOnlySpan<byte> expectedOriginal,
        RemoteHookPayloadBuilder payloadFactory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(payloadFactory);
        if (expectedOriginal.Length < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedOriginal), "Hook 至少需要覆寫五個完整位元組。");
        }

        byte[] original = expectedOriginal.ToArray();
        byte[] current = memory.ReadBytes(targetAddress, original.Length);
        if (!current.AsSpan().SequenceEqual(original))
        {
            throw new InvalidDataException("Hook 目標位元組與版本設定不符，已拒絕套用。");
        }

        nint cave = memory.AllocateExecutableNear(targetAddress, AllocationSize);
        bool targetRestored = true;
        try
        {
            RemoteHookPayload payload = payloadFactory(cave, targetAddress, original);
            if (payload.Code.Length > AllocationSize)
            {
                throw new InvalidDataException("Hook payload 超過預留的遠端記憶體大小。");
            }

            memory.WriteCodeBytes(cave, payload.Code);
            Verify(memory, cave, payload.Code, "Hook payload");

            byte[] jump = CreateJump(targetAddress, cave, original.Length);
            targetRestored = false;
            memory.WriteCodeBytes(targetAddress, jump);
            Verify(memory, targetAddress, jump, "Hook 跳躍");

            return new RemoteCodeHook(memory, targetAddress, original, cave, payload.DataOffsets);
        }
        catch (Exception installError)
        {
            Exception? restoreError = null;
            try
            {
                byte[] targetBytes = memory.ReadBytes(targetAddress, original.Length);
                if (!targetBytes.AsSpan().SequenceEqual(original))
                {
                    memory.WriteCodeBytes(targetAddress, original);
                    Verify(memory, targetAddress, original, "Hook 失敗還原");
                }

                targetRestored = true;
            }
            catch (Exception exception)
            {
                restoreError = exception;
            }

            if (targetRestored)
            {
                try
                {
                    memory.FreeExecutable(cave);
                }
                catch (Exception freeError)
                {
                    restoreError ??= freeError;
                }
            }

            throw restoreError is null
                ? new InvalidOperationException("安裝遠端 hook 失敗，目標位元組已還原。", installError)
                : new AggregateException("安裝遠端 hook 失敗，且清理未完整完成。", installError, restoreError);
        }
    }

    internal void WriteData(string key, ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(!_installed, this);
        if (!_dataOffsets.TryGetValue(key, out int offset))
        {
            throw new KeyNotFoundException($"Hook payload 沒有資料欄位 {key}。");
        }

        nint address = _caveAddress + offset;
        _memory.WriteBytes(address, bytes);
        Verify(_memory, address, bytes, $"Hook 資料 {key}");
    }

    internal byte[] ReadData(string key, int length)
    {
        ObjectDisposedException.ThrowIf(!_installed, this);
        if (!_dataOffsets.TryGetValue(key, out int offset))
        {
            throw new KeyNotFoundException($"Hook payload 沒有資料欄位 {key}。");
        }

        return _memory.ReadBytes(_caveAddress + offset, length);
    }

    internal void Disable()
    {
        if (!_installed)
        {
            return;
        }

        _memory.WriteCodeBytes(_targetAddress, _originalBytes);
        Verify(_memory, _targetAddress, _originalBytes, "Hook 原始位元組");
        _installed = false;

        nint cave = _caveAddress;
        _caveAddress = 0;
        _memory.FreeExecutable(cave);
    }

    public void Dispose() => Disable();

    private static byte[] CreateJump(nint source, nint destination, int overwriteLength)
    {
        byte[] bytes = Enumerable.Repeat((byte)0x90, overwriteLength).ToArray();
        bytes[0] = 0xE9;
        long displacement = (long)destination - checked((long)source + 5);
        BitConverter.GetBytes(checked((int)displacement)).CopyTo(bytes, 1);
        return bytes;
    }

    private static void Verify(
        IMemoryAccessor memory,
        nint address,
        ReadOnlySpan<byte> expected,
        string description)
    {
        byte[] actual = memory.ReadBytes(address, expected.Length);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new IOException($"{description} 寫入後讀回驗證失敗。");
        }
    }
}
