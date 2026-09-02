using VSModifier.Memory.Profiles;

namespace VSModifier.Memory.Trainer;

public static class TrainerProfilePolicy
{
    public static readonly TimeSpan MinimumVerificationDuration = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MaximumVerificationDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumHookVerificationDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumTreasureBehaviorVerificationDuration = TimeSpan.FromSeconds(30);

    public static void RequireReleaseReady(GameVersionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Verified)
        {
            throw new InvalidOperationException("此版本偏移尚未完成實機驗證，已拒絕附加。");
        }

        if (profile.OnlineSession is null)
        {
            throw new InvalidOperationException("此版本缺少線上會話防護偏移，已拒絕附加。");
        }
    }

    public static FeatureDefinition RequireDevelopmentVerification(
        GameVersionProfile profile,
        string expectedProfileId,
        string featureKey,
        FeatureKind expectedKind,
        TimeSpan duration)
    {
        return RequireDevelopmentVerificationCore(
            profile,
            expectedProfileId,
            featureKey,
            expectedKind,
            duration,
            MaximumVerificationDuration);
    }

    public static FeatureDefinition RequireDevelopmentHookVerification(
        GameVersionProfile profile,
        string expectedProfileId,
        string featureKey,
        TimeSpan duration)
    {
        return RequireDevelopmentVerificationCore(
            profile,
            expectedProfileId,
            featureKey,
            FeatureKind.Hook,
            duration,
            MaximumHookVerificationDuration);
    }

    public static FeatureDefinition RequireTreasureBehaviorVerification(
        GameVersionProfile profile,
        string expectedProfileId,
        TimeSpan duration)
    {
        return RequireDevelopmentVerificationCore(
            profile,
            expectedProfileId,
            "maxTreasure",
            FeatureKind.Patch,
            duration,
            MaximumTreasureBehaviorVerificationDuration);
    }

    private static FeatureDefinition RequireDevelopmentVerificationCore(
        GameVersionProfile profile,
        string expectedProfileId,
        string featureKey,
        FeatureKind expectedKind,
        TimeSpan duration,
        TimeSpan maximumDuration)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureKey);
        if (!string.Equals(profile.ProfileId, expectedProfileId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"明確指定的 Profile ID {expectedProfileId} 與目前版本 {profile.ProfileId} 不符，已拒絕驗證。");
        }

        if (profile.OnlineSession is null)
        {
            throw new InvalidOperationException("此版本缺少線上會話防護偏移，已拒絕驗證。");
        }

        if (duration < MinimumVerificationDuration || duration > maximumDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"開發驗證時間必須介於 {MinimumVerificationDuration.TotalMilliseconds:R} 與 {maximumDuration.TotalMilliseconds:R} 毫秒。");
        }

        if (!profile.Features.TryGetValue(featureKey, out FeatureDefinition? feature))
        {
            throw new KeyNotFoundException($"Profile {profile.ProfileId} 沒有功能 {featureKey}。");
        }

        if (feature.Kind != expectedKind)
        {
            throw new InvalidOperationException($"功能 {featureKey} 不是 {expectedKind} 類型。");
        }

        return feature;
    }
}
