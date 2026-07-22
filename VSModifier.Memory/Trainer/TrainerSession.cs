using System.Globalization;
using VSModifier.Memory.Locking;
using VSModifier.Memory.Patching;
using VSModifier.Memory.ProcessMemory;
using VSModifier.Memory.Profiles;

namespace VSModifier.Memory.Trainer;

public sealed class TrainerSession : IAsyncDisposable
{
    private readonly ProcessMemorySession _memory;
    private readonly ValueLockService _locks = new();
    private readonly Dictionary<string, byte[]> _originalValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryPatchSet> _patches = new(StringComparer.Ordinal);
    private bool _disposed;

    private TrainerSession(ProcessMemorySession memory, GameVersionProfile profile)
    {
        _memory = memory;
        Profile = profile;
    }

    public GameVersionProfile Profile { get; }

    public bool IsAttached => !_disposed && !_memory.HasExited;

    public static TrainerSession Attach(OffsetCatalog catalog, string gameAssemblyPath)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string fingerprint = GameAssemblyFingerprint.CalculateSha256(gameAssemblyPath);
        GameVersionProfile profile = catalog.FindByHash(fingerprint)
            ?? throw new InvalidOperationException("目前 offsets.json 不支援這個 GameAssembly.dll 版本。");
        if (!profile.Verified)
        {
            throw new InvalidOperationException("此版本偏移尚未完成實機驗證，已拒絕附加。");
        }

        if (profile.OnlineSession is null)
        {
            throw new InvalidOperationException("此版本缺少線上會話防護偏移，已拒絕附加。");
        }

        return new TrainerSession(ProcessMemorySession.Attach(), profile);
    }

    public void EnableValueLock(string featureKey, double value)
    {
        ThrowIfDisposed();
        FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
        nint address = Resolve(feature.Address);
        byte[] desired = EncodeValue(feature.ValueType, value);
        _originalValues.TryAdd(featureKey, _memory.ReadBytes(address, desired.Length));
        _locks.Set(featureKey, () =>
        {
            EnsureOffline();
            if (feature.PreserveZero && DecodeValue(feature.ValueType, _memory.ReadBytes(address, desired.Length)) == 0)
            {
                return;
            }

            _memory.WriteBytes(address, desired);
        });
    }

    public void EnableMultiplierLock(string featureKey, double multiplier)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(multiplier) || multiplier < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }

        FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
        nint address = Resolve(feature.Address);
        int size = GetValueSize(feature.ValueType);
        if (!_originalValues.TryGetValue(featureKey, out byte[]? original))
        {
            original = _memory.ReadBytes(address, size);
            _originalValues.Add(featureKey, original);
        }

        double originalValue = DecodeValue(feature.ValueType, original);
        byte[] desired = EncodeValue(feature.ValueType, originalValue * multiplier);
        _locks.Set(featureKey, () =>
        {
            EnsureOffline();
            if (feature.PreserveZero && DecodeValue(feature.ValueType, _memory.ReadBytes(address, size)) == 0)
            {
                return;
            }

            _memory.WriteBytes(address, desired);
        });
    }

    public void EnableAdditiveLock(string featureKey, double amount)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
        nint address = Resolve(feature.Address);
        int size = GetValueSize(feature.ValueType);
        if (!_originalValues.TryGetValue(featureKey, out byte[]? original))
        {
            original = _memory.ReadBytes(address, size);
            _originalValues.Add(featureKey, original);
        }

        byte[] desired = EncodeValue(feature.ValueType, DecodeValue(feature.ValueType, original) + amount);
        _locks.Set(featureKey, () =>
        {
            EnsureOffline();
            _memory.WriteBytes(address, desired);
        });
    }

    public void DisableValueLock(string featureKey, bool restoreOriginal = true)
    {
        ThrowIfDisposed();
        _locks.Remove(featureKey);
        if (!restoreOriginal || !_originalValues.Remove(featureKey, out byte[]? original))
        {
            return;
        }

        FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
        _memory.WriteBytes(Resolve(feature.Address), original);
    }

    public void EnablePatch(string featureKey)
    {
        ThrowIfDisposed();
        if (_patches.ContainsKey(featureKey))
        {
            return;
        }

        EnsureOffline();
        FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Patch);
        List<MemoryPatch> segments =
        [
            CreatePatch(feature.Address, feature.ExpectedBytes, feature.PatchBytes)
        ];
        segments.AddRange(feature.AdditionalPatches.Select(segment =>
            CreatePatch(segment.Address, segment.ExpectedBytes, segment.PatchBytes)));
        MemoryPatchSet patchSet = new(segments);
        patchSet.Enable();
        _patches.Add(featureKey, patchSet);
    }

    public void DisablePatch(string featureKey)
    {
        ThrowIfDisposed();
        if (_patches.Remove(featureKey, out MemoryPatchSet? patch))
        {
            patch.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? restorationError = null;
        try
        {
            await _locks.DisposeAsync();
            if (!_memory.HasExited)
            {
                foreach (MemoryPatchSet patch in _patches.Values)
                {
                    try
                    {
                        patch.Dispose();
                    }
                    catch (Exception exception)
                    {
                        restorationError ??= exception;
                    }
                }

                foreach ((string key, byte[] original) in _originalValues)
                {
                    if (Profile.Features.TryGetValue(key, out FeatureDefinition? feature))
                    {
                        try
                        {
                            _memory.WriteBytes(Resolve(feature.Address), original);
                        }
                        catch (Exception exception)
                        {
                            restorationError ??= exception;
                        }
                    }
                }
            }
        }
        finally
        {
            _memory.Dispose();
        }

        if (restorationError is not null)
        {
            throw new InvalidOperationException("Trainer 關閉時至少一項原始值還原失敗。", restorationError);
        }
    }

    private void EnsureOffline()
    {
        AddressDefinition online = Profile.OnlineSession
            ?? throw new OnlineSessionException("缺少線上會話防護，已停止寫入。");
        byte onlineValue = _memory.Read<byte>(Resolve(online));
        if (onlineValue != 0)
        {
            throw new OnlineSessionException("偵測到線上會話，所有 Trainer 寫入已停用。");
        }
    }

    private nint Resolve(AddressDefinition definition)
    {
        return ProfileAddressResolver.Resolve(_memory, definition);
    }

    private FeatureDefinition GetFeature(string featureKey, FeatureKind kind)
    {
        if (!Profile.Features.TryGetValue(featureKey, out FeatureDefinition? feature))
        {
            throw new KeyNotFoundException($"此版本沒有功能 {featureKey} 的偏移。");
        }

        if (feature.Kind != kind)
        {
            throw new InvalidOperationException($"功能 {featureKey} 的種類不是 {kind}。");
        }

        return feature;
    }

    private static byte[] EncodeValue(MemoryValueType type, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return type switch
        {
            MemoryValueType.Boolean => BitConverter.GetBytes(value != 0),
            MemoryValueType.Byte => [(byte)checked((int)value)],
            MemoryValueType.Int32 => BitConverter.GetBytes(checked((int)value)),
            MemoryValueType.Int64 => BitConverter.GetBytes(checked((long)value)),
            MemoryValueType.Float => BitConverter.GetBytes((float)value),
            MemoryValueType.Double => BitConverter.GetBytes(value),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static double DecodeValue(MemoryValueType type, ReadOnlySpan<byte> bytes)
    {
        return type switch
        {
            MemoryValueType.Boolean => BitConverter.ToBoolean(bytes) ? 1d : 0d,
            MemoryValueType.Byte => bytes[0],
            MemoryValueType.Int32 => BitConverter.ToInt32(bytes),
            MemoryValueType.Int64 => BitConverter.ToInt64(bytes),
            MemoryValueType.Float => BitConverter.ToSingle(bytes),
            MemoryValueType.Double => BitConverter.ToDouble(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static int GetValueSize(MemoryValueType type)
    {
        return type switch
        {
            MemoryValueType.Boolean or MemoryValueType.Byte => 1,
            MemoryValueType.Int32 or MemoryValueType.Float => 4,
            MemoryValueType.Int64 or MemoryValueType.Double => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static byte[] ParseBytes(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"缺少 {field}。");
        }

        string[] tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Select(token => byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)).ToArray();
    }

    private MemoryPatch CreatePatch(AddressDefinition address, string? expectedBytes, string? patchBytes)
    {
        byte[] patch = ParseBytes(patchBytes, "patchBytes");
        byte[]? expected = string.IsNullOrWhiteSpace(expectedBytes)
            ? null
            : ParseBytes(expectedBytes, "expectedBytes");
        return new MemoryPatch(_memory, Resolve(address), patch, expected);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
