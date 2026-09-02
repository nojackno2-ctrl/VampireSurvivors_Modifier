using System.Globalization;
using VSModifier.Memory.Locking;
using VSModifier.Memory.Patching;
using VSModifier.Memory.ProcessMemory;
using VSModifier.Memory.Profiles;

namespace VSModifier.Memory.Trainer;

public sealed class TrainerSession : IAsyncDisposable
{
    private const string SafetyGuardKey = "__onlineSessionGuard";
    private readonly IProtectedMemoryAccessor _memory;
    private readonly IRemoteCodeMemory? _remoteCodeMemory;
    private readonly Func<bool> _hasExited;
    private readonly Action _disposeMemory;
    private readonly Func<AddressDefinition, nint> _addressResolver;
    private readonly ValueLockService _locks;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, OriginalValueSnapshot> _originalValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryPatchSet> _patches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteCodeHook> _hooks = new(StringComparer.Ordinal);
    private readonly string? _verificationFeatureKey;
    private readonly bool _allowInvulnerabilityCompanion;
    private Exception? _safetyStopCause;
    private bool _disposed;

    private TrainerSession(
        ProcessMemorySession memory,
        GameVersionProfile profile,
        string? verificationFeatureKey = null,
        bool allowInvulnerabilityCompanion = false)
        : this(
            memory,
            profile,
            definition => ProfileAddressResolver.Resolve(memory, definition),
            () => memory.HasExited,
            memory.Dispose,
            verificationFeatureKey,
            allowInvulnerabilityCompanion)
    {
    }

    internal TrainerSession(
        IProtectedMemoryAccessor memory,
        GameVersionProfile profile,
        Func<AddressDefinition, nint> addressResolver,
        Func<bool>? hasExited = null,
        Action? disposeMemory = null,
        string? verificationFeatureKey = null,
        bool allowInvulnerabilityCompanion = false)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(addressResolver);
        _memory = memory;
        _remoteCodeMemory = memory as IRemoteCodeMemory;
        _addressResolver = addressResolver;
        _hasExited = hasExited ?? (() => false);
        _disposeMemory = disposeMemory ?? (() => { });
        Profile = profile;
        _verificationFeatureKey = verificationFeatureKey;
        _allowInvulnerabilityCompanion = allowInvulnerabilityCompanion;

        // 附加當下就必須確認離線；失敗時尚未啟動任何背景工作，不留下需要清理的鎖服務。
        EnsureOffline();

        // 線上防護是所有鎖共用的前置條件，交給鎖服務每個週期只跑一次；
        // 若放進每個鎖的委派，成本會隨啟用功能數量線性增加。
        _locks = new ValueLockService(
            stopAllOnFailure: true,
            guard: EnsureOffline,
            guardKey: SafetyGuardKey);
        _locks.LockFailed += Locks_LockFailed;
    }

    public GameVersionProfile Profile { get; }

    public bool IsAttached => !_disposed && !_hasExited();

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
        if (expectedKind == FeatureKind.Hook)
        {
            TrainerProfilePolicy.RequireDevelopmentHookVerification(
                profile,
                expectedProfileId,
                featureKey,
                duration);
        }
        else
        {
            TrainerProfilePolicy.RequireDevelopmentVerification(
                profile,
                expectedProfileId,
                featureKey,
                expectedKind,
                duration);
        }

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

    internal static TrainerSession AttachForForcedItemBehaviorVerification(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath,
        string expectedProfileId,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        GameVersionFingerprint fingerprint = GameVersionFingerprint.Calculate(
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath);
        GameVersionProfile profile = catalog.Match(fingerprint).Profile
            ?? throw new InvalidOperationException("目前 offsets.json 不支援這個遊戲版本。");
        TrainerProfilePolicy.RequireDevelopmentHookVerification(
            profile,
            expectedProfileId,
            "forceLevelUpItem",
            duration);
        TrainerProfilePolicy.RequireDevelopmentVerification(
            profile,
            expectedProfileId,
            "invulnerability",
            FeatureKind.Value,
            TrainerProfilePolicy.MaximumVerificationDuration);

        ProcessMemorySession memory = ProcessMemorySession.Attach();
        try
        {
            return new TrainerSession(
                memory,
                profile,
                "forceLevelUpItem",
                allowInvulnerabilityCompanion: true);
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    internal static TrainerSession AttachForTreasureBehaviorVerification(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath,
        string expectedProfileId,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        GameVersionFingerprint fingerprint = GameVersionFingerprint.Calculate(
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath);
        GameVersionProfile profile = catalog.Match(fingerprint).Profile
            ?? throw new InvalidOperationException("目前 offsets.json 不支援這個遊戲版本。");
        TrainerProfilePolicy.RequireTreasureBehaviorVerification(
            profile,
            expectedProfileId,
            duration);

        ProcessMemorySession memory = ProcessMemorySession.Attach();
        try
        {
            return new TrainerSession(memory, profile, "maxTreasure");
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    public void EnableValueLock(string featureKey, double value)
    {
        EnableComputedLock(
            featureKey,
            feature => ValidateValueRange(featureKey, feature, value),
            (_, _) => value,
            honorPreserveZero: true);
    }

    public void EnableMultiplierLock(string featureKey, double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }

        EnableComputedLock(
            featureKey,
            validate: null,
            (_, original) => original * multiplier,
            honorPreserveZero: true);
    }

    public void EnableAdditiveLock(string featureKey, double amount)
    {
        if (!double.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        EnableComputedLock(
            featureKey,
            validate: null,
            (_, original) => original + amount,
            honorPreserveZero: false);
    }

    /// <summary>
    /// 三種鎖只差在目標值怎麼算與是否尊重 <see cref="FeatureDefinition.PreserveZero"/>；
    /// 位址解析、原始值快照與寫入委派完全相同。
    /// </summary>
    private void EnableComputedLock(
        string featureKey,
        Action<FeatureDefinition>? validate,
        Func<FeatureDefinition, double, double> computeTarget,
        bool honorPreserveZero)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
            validate?.Invoke(feature);

            nint address = Resolve(feature.Address);
            int size = GetValueSize(feature.ValueType);
            OriginalValueSnapshot snapshot = CaptureOriginalValue(featureKey, address, size);
            MemoryValueType valueType = feature.ValueType;
            byte[] desired = EncodeValue(
                valueType,
                computeTarget(feature, DecodeValue(valueType, snapshot.Bytes)));
            bool preserveZero = honorPreserveZero && feature.PreserveZero;
            _locks.Set(featureKey, () =>
            {
                if (preserveZero)
                {
                    Span<byte> current = stackalloc byte[size];
                    _memory.ReadBytes(address, current);
                    if (DecodeValue(valueType, current) == 0)
                    {
                        return;
                    }
                }

                _memory.WriteBytes(address, desired);
            });
        }
    }

    private OriginalValueSnapshot CaptureOriginalValue(string featureKey, nint address, int size)
    {
        if (!_originalValues.TryGetValue(featureKey, out OriginalValueSnapshot? snapshot))
        {
            snapshot = new OriginalValueSnapshot(address, _memory.ReadBytes(address, size));
            _originalValues.Add(featureKey, snapshot);
        }

        return snapshot;
    }

    public void DisableValueLock(string featureKey, bool restoreOriginal = true)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            _locks.Remove(featureKey);
            if (!restoreOriginal
                || !_originalValues.TryGetValue(featureKey, out OriginalValueSnapshot? snapshot))
            {
                return;
            }

            _ = GetFeature(featureKey, FeatureKind.Value);
            _ = WriteValueAtAndVerify(snapshot.Address, snapshot.Bytes);
            _originalValues.Remove(featureKey);
        }
    }

    public double ReadValue(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
            Span<byte> bytes = stackalloc byte[GetValueSize(feature.ValueType)];
            _memory.ReadBytes(Resolve(feature.Address), bytes);
            return DecodeValue(feature.ValueType, bytes);
        }
    }

    public double WriteValue(string featureKey, double value)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
            ValidateValueRange(featureKey, feature, value);
            byte[] desired = EncodeValue(feature.ValueType, value);

            EnsureOffline();
            nint address = Resolve(feature.Address);
            byte[] original = _memory.ReadBytes(address, desired.Length);
            try
            {
                byte[] verification = WriteValueAtAndVerify(address, desired);
                EnsureOffline();
                return DecodeValue(feature.ValueType, verification);
            }
            catch (Exception writeError)
            {
                try
                {
                    _ = WriteValueAtAndVerify(address, original);
                }
                catch (Exception restoreError)
                {
                    throw new AggregateException(
                        $"寫入 {featureKey} 失敗，且原始值還原失敗。",
                        writeError,
                        restoreError);
                }

                throw new InvalidOperationException(
                    $"寫入 {featureKey} 失敗，原始值已還原。",
                    writeError);
            }
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

    public void EnableForcedLevelUpItem(int itemId)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }

        ArmHook(
            "forceLevelUpItem",
            TrainerHookPayloadFactory.CreateForcedLevelUpItem,
            hook => hook.WriteData(TrainerHookPayloadFactory.ItemId, BitConverter.GetBytes(itemId)));
    }

    public void DisableForcedLevelUpItem()
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            RemoveHook("forceLevelUpItem");
        }
    }

    public void TriggerDuplicateNextWeapon() =>
        ArmHook("duplicateNextWeapon", TrainerHookPayloadFactory.CreateDuplicateNext);

    public void TriggerDuplicateNextAccessory() =>
        ArmHook("duplicateNextAccessory", TrainerHookPayloadFactory.CreateDuplicateNext);

    internal double ReadValueForVerification(string featureKey) => ReadValue(featureKey);

    internal double RestoreValueForVerification(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            _locks.Remove(featureKey);
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Value);
            if (!_originalValues.TryGetValue(featureKey, out OriginalValueSnapshot? snapshot))
            {
                throw new InvalidOperationException($"功能 {featureKey} 沒有可還原的原始值。");
            }

            byte[] restored = WriteValueAtAndVerify(snapshot.Address, snapshot.Bytes);
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

    internal bool HookMatchesOriginalForVerification(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Hook);
            byte[] expected = ParseBytes(feature.ExpectedBytes, "expectedBytes");
            return _memory.ReadBytes(Resolve(feature.Address), expected.Length)
                .AsSpan()
                .SequenceEqual(expected);
        }
    }

    internal bool HookJumpInstalledForVerification(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Hook);
            return _hooks.ContainsKey(featureKey)
                && _memory.ReadBytes(Resolve(feature.Address), 1)[0] == 0xE9;
        }
    }

    internal void DisableHookForVerification(string featureKey)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            _ = GetFeature(featureKey, FeatureKind.Hook);
            RemoveHook(featureKey);
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
                if (!_hasExited())
                {
                    restorationError = RestoreActiveState();
                }
            }
        }
        finally
        {
            _disposeMemory();
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
            restorationError = _hasExited() ? null : RestoreActiveState();
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
        foreach ((string key, RemoteCodeHook hook) in _hooks.ToArray())
        {
            try
            {
                hook.Dispose();
                _hooks.Remove(key);
            }
            catch (Exception exception)
            {
                restorationError ??= exception;
            }
        }

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

        foreach ((string key, OriginalValueSnapshot snapshot) in _originalValues.ToArray())
        {
            if (!Profile.Features.TryGetValue(key, out FeatureDefinition? feature))
            {
                continue;
            }

            try
            {
                _ = WriteValueAtAndVerify(snapshot.Address, snapshot.Bytes);
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
        return _addressResolver(definition);
    }

    /// <summary>
    /// 安裝（或沿用）hook、寫入選用的參數後再拉起啟用旗標；
    /// 任何一步失敗都會移除 hook，還原不完整時以 <see cref="AggregateException"/> 回報。
    /// </summary>
    private void ArmHook(
        string featureKey,
        RemoteHookPayloadBuilder payloadBuilder,
        Action<RemoteCodeHook>? configure = null)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailable();
            EnsureOffline();
            RemoteCodeHook hook = GetOrInstallHook(featureKey, payloadBuilder);
            try
            {
                configure?.Invoke(hook);
                hook.WriteData(TrainerHookPayloadFactory.EnabledFlag, [1]);
                EnsureOffline();
            }
            catch (Exception mutationError)
            {
                try
                {
                    RemoveHook(featureKey);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(
                        $"{featureKey} hook 啟動失敗，且清理未完整完成。",
                        mutationError,
                        cleanupError);
                }

                throw;
            }
        }
    }

    private RemoteCodeHook GetOrInstallHook(
        string featureKey,
        RemoteHookPayloadBuilder payloadBuilder)
    {
        if (_hooks.TryGetValue(featureKey, out RemoteCodeHook? existing))
        {
            return existing;
        }

        IRemoteCodeMemory memory = _remoteCodeMemory
            ?? throw new InvalidOperationException("目前記憶體工作階段不支援遠端程式碼 hook。");
        FeatureDefinition feature = GetFeature(featureKey, FeatureKind.Hook);
        byte[] expected = ParseBytes(feature.ExpectedBytes, "expectedBytes");
        RemoteCodeHook hook = RemoteCodeHook.Install(
            memory,
            Resolve(feature.Address),
            expected,
            payloadBuilder);
        _hooks.Add(featureKey, hook);
        return hook;
    }

    private void RemoveHook(string featureKey)
    {
        if (!_hooks.TryGetValue(featureKey, out RemoteCodeHook? hook))
        {
            return;
        }

        hook.Dispose();
        _hooks.Remove(featureKey);
    }

    private FeatureDefinition GetFeature(string featureKey, FeatureKind kind)
    {
        if (_verificationFeatureKey is not null
            && !string.Equals(_verificationFeatureKey, featureKey, StringComparison.Ordinal)
            && !(_allowInvulnerabilityCompanion
                && string.Equals(featureKey, "invulnerability", StringComparison.Ordinal)))
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

    private static void ValidateValueRange(
        string featureKey,
        FeatureDefinition feature,
        double value)
    {
        if (!double.IsFinite(value)
            || feature.MinValue is double minimum && value < minimum
            || feature.MaxValue is double maximum && value > maximum)
        {
            string allowed = feature.MinValue is null && feature.MaxValue is null
                ? "有限數值"
                : $"{feature.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} 至 "
                    + $"{feature.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "+∞"}";
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"功能 {featureKey} 的值必須介於 {allowed}。");
        }
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

    private byte[] WriteValueAtAndVerify(nint resolved, ReadOnlySpan<byte> value)
    {
        _memory.WriteBytes(resolved, value);
        byte[] verification = _memory.ReadBytes(resolved, value.Length);
        if (!verification.AsSpan().SequenceEqual(value))
        {
            throw new IOException($"位址 0x{resolved:X} 的原始數值還原後讀回驗證失敗。");
        }

        return verification;
    }

    private sealed record OriginalValueSnapshot(nint Address, byte[] Bytes);

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
