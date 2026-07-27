namespace VSModifier.Memory.Patching;

internal sealed class X64CodeBuilder
{
    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<RelativeFixup> _fixups = [];

    internal int Position => _bytes.Count;

    internal void MarkLabel(string name)
    {
        if (!_labels.TryAdd(name, Position))
        {
            throw new InvalidOperationException($"重複的 x64 標籤：{name}。");
        }
    }

    internal void Emit(params byte[] bytes) => _bytes.AddRange(bytes);

    internal void EmitInt32(int value) => _bytes.AddRange(BitConverter.GetBytes(value));

    internal void EmitRelative32(string label)
    {
        _fixups.Add(new RelativeFixup(Position, label, null));
        Emit(0, 0, 0, 0);
    }

    internal void EmitRelative32(nint absoluteTarget)
    {
        _fixups.Add(new RelativeFixup(Position, null, absoluteTarget));
        Emit(0, 0, 0, 0);
    }

    internal byte[] Build(nint baseAddress)
    {
        byte[] result = [.. _bytes];
        foreach (RelativeFixup fixup in _fixups)
        {
            long destination = fixup.Label is not null
                ? checked((long)baseAddress + GetLabelOffset(fixup.Label))
                : (long)fixup.AbsoluteTarget!.Value;
            long sourceAfterDisplacement = checked((long)baseAddress + fixup.Offset + sizeof(int));
            int displacement = checked((int)(destination - sourceAfterDisplacement));
            BitConverter.GetBytes(displacement).CopyTo(result, fixup.Offset);
        }

        return result;
    }

    internal int GetLabelOffset(string name) =>
        _labels.TryGetValue(name, out int offset)
            ? offset
            : throw new InvalidOperationException($"找不到 x64 標籤：{name}。");

    private sealed record RelativeFixup(int Offset, string? Label, nint? AbsoluteTarget);
}
