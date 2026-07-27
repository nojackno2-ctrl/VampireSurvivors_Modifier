using VSModifier.Memory.ProcessMemory;
using VSModifier.Memory.Profiles;

namespace VSModifier.Memory.Trainer;

public sealed record DiagnosticValue(string Key, bool Success, nint Address, double? Value, string? Error);

public sealed record TrainerDiagnosticResult(
    string ProfileLabel,
    bool ProfileVerified,
    DiagnosticValue OnlineSession,
    IReadOnlyList<DiagnosticValue> Features);

public static class TrainerDiagnostics
{
    public static TrainerDiagnosticResult InspectReadOnly(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        GameVersionFingerprint fingerprint = GameVersionFingerprint.Calculate(
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath);
        ProfileMatchResult match = catalog.Match(fingerprint);
        GameVersionProfile profile = match.Profile
            ?? throw new InvalidOperationException(match.Error ?? "offsets.json 沒有目前遊戲版本的 profile。");
        using ProcessMemorySession memory = ProcessMemorySession.AttachReadOnly();

        DiagnosticValue online = profile.OnlineSession is null
            ? new DiagnosticValue("onlineSession", false, 0, null, "缺少線上 guard 定義。")
            : ReadValue(memory, "onlineSession", profile.OnlineSession, MemoryValueType.Boolean);
        List<DiagnosticValue> features = [];
        foreach ((string key, FeatureDefinition feature) in profile.Features.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (feature.Kind is FeatureKind.Patch or FeatureKind.Hook)
            {
                features.Add(ReadBytes(memory, key, feature));
            }
            else
            {
                features.Add(ReadValue(memory, key, feature.Address, feature.ValueType));
            }
        }

        return new TrainerDiagnosticResult(profile.Label, profile.Verified, online, features);
    }

    private static DiagnosticValue ReadValue(
        ProcessMemorySession memory,
        string key,
        AddressDefinition addressDefinition,
        MemoryValueType type)
    {
        try
        {
            nint address = ProfileAddressResolver.Resolve(memory, addressDefinition);
            byte[] bytes = memory.ReadBytes(address, SizeOf(type));
            double value = type switch
            {
                MemoryValueType.Boolean => BitConverter.ToBoolean(bytes) ? 1 : 0,
                MemoryValueType.Byte => bytes[0],
                MemoryValueType.Int32 => BitConverter.ToInt32(bytes),
                MemoryValueType.Int64 => BitConverter.ToInt64(bytes),
                MemoryValueType.Float => BitConverter.ToSingle(bytes),
                MemoryValueType.Double => BitConverter.ToDouble(bytes),
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            return new DiagnosticValue(key, true, address, value, null);
        }
        catch (Exception exception)
        {
            return new DiagnosticValue(key, false, 0, null, exception.Message);
        }
    }

    private static DiagnosticValue ReadBytes(ProcessMemorySession memory, string key, FeatureDefinition feature)
    {
        try
        {
            nint firstAddress = ReadPatchSegment(memory, feature.Address, feature.ExpectedBytes, feature.PatchBytes);
            foreach (PatchSegmentDefinition segment in feature.AdditionalPatches)
            {
                _ = ReadPatchSegment(memory, segment.Address, segment.ExpectedBytes, segment.PatchBytes);
            }

            return new DiagnosticValue(key, true, firstAddress, null, null);
        }
        catch (Exception exception)
        {
            return new DiagnosticValue(key, false, 0, null, exception.Message);
        }
    }

    private static nint ReadPatchSegment(
        ProcessMemorySession memory,
        AddressDefinition addressDefinition,
        string? expectedBytes,
        string? patchBytes)
    {
        nint address = ProfileAddressResolver.Resolve(memory, addressDefinition);
        byte[] expected = ParseBytes(expectedBytes ?? patchBytes);
        byte[] actual = memory.ReadBytes(address, expected.Length);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException($"位址 0x{address:X} 的原始程式碼位元組與 profile 不符。");
        }

        return address;
    }

    private static byte[] ParseBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Patch 缺少 expectedBytes/patchBytes。");
        }

        string compact = string.Concat(value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return Convert.FromHexString(compact);
    }

    private static int SizeOf(MemoryValueType type) => type switch
    {
        MemoryValueType.Boolean or MemoryValueType.Byte => 1,
        MemoryValueType.Int32 or MemoryValueType.Float => 4,
        MemoryValueType.Int64 or MemoryValueType.Double => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
