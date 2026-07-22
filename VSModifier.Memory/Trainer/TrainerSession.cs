using System.Globalization;
using VSModifier.Memory.Locking;
using VSModifier.Memory.Patching;
using VSModifier.Memory.ProcessMemory;
using VSModifier.Memory.Profiles;

namespace VSModifier.Memory.Trainer;

public sealed class TrainerSession : IAsyncDisposable
{
    private const string SafetyGuardKey = "__onlineSessionGuard";
    private readonly ProcessMemorySession _memory;
    private readonly ValueLockService _locks = new(stopAllOnFailure: true);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, byte[]> _originalValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryPatchSet> _patches = new(StringComparer.Ordinal);
    private readonly string? _verificationFeatureKey;
    private Exception? _safetyStopCause;
    private bool _disposed;

    private TrainerSession(
        ProcessMemorySession memory,
        GameVersionProfile profile,
        string? verificationFeatureKey = null)
    {
        _memory = memory;
        Profile = profile;
        _verificationFeatureKey = verificationFeatureKey;
        _locks.LockFailed += Locks_LockFailed;
        _locks.Set(SafetyGuardKey, EnsureOffline);
    }

    public GameVersionProfile Profile { get; }

    public bool IsAttached => !_disposed && !_memory.HasExited;

    public event EventHandler<TrainerSafetyStopEventArgs>? SafetyStopped;

    public static TrainerSession Attach(
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
            ?? throw new InvalidOperationException(match.Error ?? "目前 offsets.json 不支援這個遊戲版本。");
        TrainerProfilePolicy.RequireReleaseReady(profile);

        ProcessMemorySession memory = ProcessMemorySession.Attach();
        try
        {
            return new TrainerSession(memory, profile);
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    internal static TrainerSession AttachForVerification(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath,
        string expectedProfileId,
        string featureKey,
        FeatureKind expectedKind,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        GameVersionFingerprint fingerprint = GameVersionFingerprint.Calculate(
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath);
        ProfileMatchResult match = catalog.Match(fingerprint);
        GameVersionProfile profile = match.Profile
            ?? throw new InvalidOperationException(match.Error ?? "目前 offsets.json 不支援這個遊戲版本。");
        TrainerProfilePolicy.RequireDevelopmentVerification(
            profile,
            expectedProfileId,
            featureKey,
            expectedKind,
            duration);

        ProcessMemorySession memory = ProcessMemorySession.Attach();
        try
        {
            return new TrainerSession(memory, profile, featureKey);
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    public void EnableValueLock(string featureKey, double value)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
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
    }

    public void EnableMultiplierLock(string featureKey, double multiplier)
    {
        ThrowIfUnavailable();
        if (!double.IsFinite(multiplier) || multiplier < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }

        lock (_stateGate)
        {
            ThrowIfUnavailable();
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
    }

    public void EnableAdditiveLock(string featureKey, double amount)
    {
        ThrowIfUnavailable();
        if (!double.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        lock (_stateGate)
        {
            ThrowIfUnavailable();
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
    }

    public void DisableValueLock(string featureKey, bool restoreOriginal = true)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            _locks.Remove(featureKey);
            if (!restoreOriginal || !_originalValues.TryGetValue(featureKey, out byte[]? original))
            {
                return;
            }

            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
            _ = WriteValueAndVerify(feature.Address, original);
            _originalValues.Remove(featureKey);
        }
    }

    public void EnablePatch(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
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
    }

    public void DisablePatch(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            if (_patches.TryGetValue(featureKey, out MemoryPatchSet? patch))
            {
                patch.Dispose();
                _patches.Remove(featureKey);
            }
        }
    }

    internal double ReadValueForVerification(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
            int size = GetValueSize(feature.ValueType);
            return DecodeValue(feature.ValueType, _memory.ReadBytes(Resolve(feature.Address), size));
        }
    }

    internal double RestoreValueForVerification(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            _locks.Remove(featureKey);
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
            if (!_originalValues.TryGetValue(featureKey, out byte[]? original))
            {
                throw new InvalidOperationException($"功能 {featureKey} 沒有可還原的原始值。");
            }

            byte[] restored = WriteValueAndVerify(feature.Address, original);
            _originalValues.Remove(featureKey);
            return DecodeValue(feature.ValueType, restored);
        }
    }

    internal bool PatchMatchesForVerification(string featureKey, bool expectedPatched)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Patch);
            if (!PatchSegmentMatches(feature.Address, expectedPatched ? feature.PatchBytes : feature.ExpectedBytes))
            {
                return false;
            }

            return feature.AdditionalPatches.All(segment => PatchSegmentMatches(
                segment.Address,
                expectedPatched ? segment.PatchBytes : segment.ExpectedBytes));
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _locks.LockFailed -= Locks_LockFailed;
        }

        Exception? restorationError = null;
        try
        {
            await _locks.DisposeAsync();
            lock (_stateGate)
            {
                if (!_memory.HasExited)
                {
                    restorationError = RestoreActiveState();
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

    private void Locks_LockFailed(object? sender, ValueLockFailureEventArgs args)
    {
        Exception? restorationError;
        lock (_stateGate)
        {
            if (_disposed || _safetyStopCause is not null)
            {
                return;
            }

            _safetyStopCause = args.Exception;
            _locks.Clear();
            restorationError = _memory.HasExited ? null : RestoreActiveState();
        }

        try
        {
            SafetyStopped?.Invoke(this, new TrainerSafetyStopEventArgs(args.Key, args.Exception, restorationError));
        }
        catch
        {
            // Safety restoration has already run; UI notification errors must not resume the session.
        }
    }

    private Exception? RestoreActiveState()
    {
        Exception? restorationError = null;
        foreach ((string key, MemoryPatchSet patch) in _patches.ToArray())
        {
            try
            {
                patch.Dispose();
                _patches.Remove(key);
            }
            catch (Exception exception)
            {
                restorationError ??= exception;
            }
        }

        foreach ((string key, byte[] original) in _originalValues.ToArray())
        {
            if (!Profile.Features.TryGetValue(key, out FeatureDefinition? feature))
            {
                continue;
            }

            try
            {
                _ = WriteValueAndVerify(feature.Address, original);
                _originalValues.Remove(key);
            }
            catch (Exception exception)
            {
                restorationError ??= exception;
            }
        }

        return restorationError;
    }

    private nint Resolve(AddressDefinition definition)
    {
        return ProfileAddressResolver.Resolve(_memory, definition);
    }

    private FeatureDefinition GetFeature(string featureKey, FeatureKind kind)
    {
        if (_verificationFeatureKey is not null
            && !string.Equals(_verificationFeatureKey, featureKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"此開發驗證工作階段僅允許功能 {_verificationFeatureKey}，已拒絕 {featureKey}。");
        }

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

    private bool PatchSegmentMatches(AddressDefinition address, string? expectedBytes)
    {
        byte[] expected = ParseBytes(expectedBytes, "驗證位元組");
        return _memory.ReadBytes(Resolve(address), expected.Length).AsSpan().SequenceEqual(expected);
    }

    private byte[] WriteValueAndVerify(AddressDefinition address, byte[] value)
    {
        nint resolved = Resolve(address);
        _memory.WriteBytes(resolved, value);
        byte[] verification = _memory.ReadBytes(resolved, value.Length);
        if (!verification.AsSpan().SequenceEqual(value))
        {
            throw new IOException($"位址 0x{resolved:X} 的原始數值還原後讀回驗證失敗。");
        }

        return verification;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_safetyStopCause is not null)
        {
            throw new InvalidOperationException("Trainer 已因安全防護停止，必須重新附加。", _safetyStopCause);
        }
    }
}

public sealed class TrainerSafetyStopEventArgs(
    string key,
    Exception cause,
    Exception? restorationError) : EventArgs
{
    public string Key { get; } = key;

    public Exception Cause { get; } = cause;

    public Exception? RestorationError { get; } = restorationError;
}
