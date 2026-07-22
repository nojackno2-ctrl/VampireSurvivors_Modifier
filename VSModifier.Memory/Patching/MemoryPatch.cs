using VSModifier.Memory.ProcessMemory;

namespace VSModifier.Memory.Patching;

public sealed class MemoryPatch(
    IProtectedMemoryAccessor memory,
    nint address,
    ReadOnlyMemory<byte> patchBytes,
    ReadOnlyMemory<byte>? expectedOriginal = null) : IDisposable
{
    private byte[]? _original;

    public bool IsEnabled { get; private set; }

    public void Enable()
    {
        if (IsEnabled)
        {
            return;
        }

        byte[] current = memory.ReadBytes(address, patchBytes.Length);
        if (expectedOriginal.HasValue && !current.AsSpan().SequenceEqual(expectedOriginal.Value.Span))
        {
            throw new InvalidDataException("程式碼位元組與版本設定不符，已拒絕套用 patch。");
        }

        _original = current;
        memory.WriteCodeBytes(address, patchBytes.Span);
        byte[] verification = memory.ReadBytes(address, patchBytes.Length);
        if (!verification.AsSpan().SequenceEqual(patchBytes.Span))
        {
            memory.WriteCodeBytes(address, current);
            throw new IOException("code patch 寫入驗證失敗，已嘗試還原原始位元組。");
        }

        IsEnabled = true;
    }

    public void Disable()
    {
        if (!IsEnabled || _original is null)
        {
            return;
        }

        memory.WriteCodeBytes(address, _original);
        IsEnabled = false;
    }

    public void Dispose() => Disable();
}
