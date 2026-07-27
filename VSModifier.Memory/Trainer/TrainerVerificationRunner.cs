using VSModifier.Memory.Profiles;

namespace VSModifier.Memory.Trainer;

public enum VerificationValueMode
{
    Set,
    Multiply,
    Add
}

public sealed record ValueVerificationResult(
    string ProfileId,
    string FeatureKey,
    double OriginalValue,
    double AppliedValue,
    double RestoredValue,
    TimeSpan Duration);

public sealed record PatchVerificationResult(
    string ProfileId,
    string FeatureKey,
    bool AppliedBytesMatched,
    bool RestoredBytesMatched,
    TimeSpan Duration);

public sealed record HookVerificationResult(
    string ProfileId,
    string FeatureKey,
    bool InstalledJumpMatched,
    bool RestoredBytesMatched,
    TimeSpan Duration);

public sealed record ForcedItemBehaviorVerificationResult(
    string ProfileId,
    int ItemId,
    bool InstalledJumpMatched,
    bool RestoredBytesMatched,
    TimeSpan Duration);

public static class TrainerVerificationRunner
{
    public static async Task<ValueVerificationResult> VerifyValueAsync(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath,
        string expectedProfileId,
        string featureKey,
        VerificationValueMode mode,
        double value,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        await using TrainerSession session = TrainerSession.AttachForVerification(
            catalog,
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath,
            expectedProfileId,
            featureKey,
            FeatureKind.Value,
            duration);
        TaskCompletionSource<TrainerSafetyStopEventArgs> safetyStop = CreateSafetyStopSignal(session);
        double original = session.ReadValueForVerification(featureKey);
        try
        {
            switch (mode)
            {
                case VerificationValueMode.Set:
                    session.EnableValueLock(featureKey, value);
                    break;
                case VerificationValueMode.Multiply:
                    session.EnableMultiplierLock(featureKey, value);
                    break;
                case VerificationValueMode.Add:
                    session.EnableAdditiveLock(featureKey, value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            double applied = session.ReadValueForVerification(featureKey);
            await WaitForDurationOrSafetyStop(duration, safetyStop.Task, cancellationToken).ConfigureAwait(false);
            double restored = session.RestoreValueForVerification(featureKey);
            return new ValueVerificationResult(
                session.Profile.ProfileId,
                featureKey,
                original,
                applied,
                restored,
                duration);
        }
        finally
        {
            TryRestoreValue(session, featureKey);
        }
    }

    public static async Task<PatchVerificationResult> VerifyPatchAsync(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath,
        string expectedProfileId,
        string featureKey,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        await using TrainerSession session = TrainerSession.AttachForVerification(
            catalog,
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath,
            expectedProfileId,
            featureKey,
            FeatureKind.Patch,
            duration);
        TaskCompletionSource<TrainerSafetyStopEventArgs> safetyStop = CreateSafetyStopSignal(session);
        try
        {
            if (!session.PatchMatchesForVerification(featureKey, expectedPatched: false))
            {
                throw new InvalidDataException("套用前的程式碼位元組與 Profile 不符，已拒絕驗證。");
            }

            session.EnablePatch(featureKey);
            bool applied = session.PatchMatchesForVerification(featureKey, expectedPatched: true);
            if (!applied)
            {
                throw new IOException("Patch 寫入後讀回驗證失敗。");
            }

            await WaitForDurationOrSafetyStop(duration, safetyStop.Task, cancellationToken).ConfigureAwait(false);
            session.DisablePatch(featureKey);
            bool restored = session.PatchMatchesForVerification(featureKey, expectedPatched: false);
            if (!restored)
            {
                throw new IOException("Patch 原始位元組還原後讀回驗證失敗。");
            }

            return new PatchVerificationResult(
                session.Profile.ProfileId,
                featureKey,
                applied,
                restored,
                duration);
        }
        finally
        {
            TryRestorePatch(session, featureKey);
        }
    }

    public static async Task<HookVerificationResult> VerifyHookAsync(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath,
        string expectedProfileId,
        string featureKey,
        TimeSpan duration,
        int forcedItemId = 1,
        CancellationToken cancellationToken = default)
    {
        await using TrainerSession session = TrainerSession.AttachForVerification(
            catalog,
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath,
            expectedProfileId,
            featureKey,
            FeatureKind.Hook,
            duration);
        TaskCompletionSource<TrainerSafetyStopEventArgs> safetyStop = CreateSafetyStopSignal(session);
        try
        {
            if (!session.HookMatchesOriginalForVerification(featureKey))
            {
                throw new InvalidDataException("套用前的 hook 目標位元組與 Profile 不符，已拒絕驗證。");
            }

            switch (featureKey)
            {
                case "forceLevelUpItem":
                    session.EnableForcedLevelUpItem(forcedItemId);
                    break;
                case "duplicateNextWeapon":
                    session.TriggerDuplicateNextWeapon();
                    break;
                case "duplicateNextAccessory":
                    session.TriggerDuplicateNextAccessory();
                    break;
                default:
                    throw new InvalidOperationException($"沒有 {featureKey} 的開發 hook 驗證動作。");
            }

            bool installed = session.HookJumpInstalledForVerification(featureKey);
            if (!installed)
            {
                throw new IOException("Hook 跳躍安裝後讀回驗證失敗。");
            }

            await WaitForDurationOrSafetyStop(duration, safetyStop.Task, cancellationToken).ConfigureAwait(false);
            session.DisableHookForVerification(featureKey);
            bool restored = session.HookMatchesOriginalForVerification(featureKey);
            if (!restored)
            {
                throw new IOException("Hook 原始指令還原後讀回驗證失敗。");
            }

            return new HookVerificationResult(
                session.Profile.ProfileId,
                featureKey,
                installed,
                restored,
                duration);
        }
        finally
        {
            TryRestoreHook(session, featureKey);
        }
    }

    public static async Task<ForcedItemBehaviorVerificationResult> VerifyForcedItemBehaviorAsync(
        OffsetCatalog catalog,
        string gameAssemblyPath,
        string unityPlayerPath,
        string metadataPath,
        string expectedProfileId,
        int forcedItemId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        await using TrainerSession session = TrainerSession.AttachForForcedItemBehaviorVerification(
            catalog,
            gameAssemblyPath,
            unityPlayerPath,
            metadataPath,
            expectedProfileId,
            duration);
        TaskCompletionSource<TrainerSafetyStopEventArgs> safetyStop = CreateSafetyStopSignal(session);
        try
        {
            if (!session.HookMatchesOriginalForVerification("forceLevelUpItem"))
            {
                throw new InvalidDataException("套用前的強制升級 hook 位元組與 Profile 不符。");
            }

            session.EnableValueLock("invulnerability", 1);
            session.EnableForcedLevelUpItem(forcedItemId);
            bool installed = session.HookJumpInstalledForVerification("forceLevelUpItem");
            if (!installed)
            {
                throw new IOException("強制升級 hook 安裝後讀回驗證失敗。");
            }

            await WaitForDurationOrSafetyStop(duration, safetyStop.Task, cancellationToken).ConfigureAwait(false);
            session.DisableForcedLevelUpItem();
            bool restored = session.HookMatchesOriginalForVerification("forceLevelUpItem");
            session.DisableValueLock("invulnerability");
            return new ForcedItemBehaviorVerificationResult(
                session.Profile.ProfileId,
                forcedItemId,
                installed,
                restored,
                duration);
        }
        finally
        {
            TryRestoreHook(session, "forceLevelUpItem");
            try
            {
                session.DisableValueLock("invulnerability");
            }
            catch
            {
                // DisposeAsync retains and retries the captured original value.
            }
        }
    }

    private static TaskCompletionSource<TrainerSafetyStopEventArgs> CreateSafetyStopSignal(TrainerSession session)
    {
        TaskCompletionSource<TrainerSafetyStopEventArgs> signal = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.SafetyStopped += (_, args) => signal.TrySetResult(args);
        return signal;
    }

    private static async Task WaitForDurationOrSafetyStop(
        TimeSpan duration,
        Task<TrainerSafetyStopEventArgs> safetyStop,
        CancellationToken cancellationToken)
    {
        Task delay = Task.Delay(duration, cancellationToken);
        Task completed = await Task.WhenAny(delay, safetyStop).ConfigureAwait(false);
        if (completed == safetyStop)
        {
            TrainerSafetyStopEventArgs args = await safetyStop.ConfigureAwait(false);
            throw new InvalidOperationException(
                $"開發驗證因安全防護停止（{args.Key}）：{args.Cause.Message}",
                args.RestorationError is null
                    ? args.Cause
                    : new AggregateException(args.Cause, args.RestorationError));
        }

        await delay.ConfigureAwait(false);
    }

    private static void TryRestoreValue(TrainerSession session, string featureKey)
    {
        try
        {
            session.DisableValueLock(featureKey);
        }
        catch
        {
            // DisposeAsync retains and retries any tracked original value.
        }
    }

    private static void TryRestorePatch(TrainerSession session, string featureKey)
    {
        try
        {
            session.DisablePatch(featureKey);
        }
        catch
        {
            // DisposeAsync retains and retries any tracked patch.
        }
    }

    private static void TryRestoreHook(TrainerSession session, string featureKey)
    {
        try
        {
            session.DisableHookForVerification(featureKey);
        }
        catch
        {
            // DisposeAsync retains and retries any tracked hook.
        }
    }
}
