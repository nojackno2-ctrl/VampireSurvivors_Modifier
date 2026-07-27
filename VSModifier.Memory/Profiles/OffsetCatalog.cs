using System.Text.Json;
using System.Text.Json.Serialization;
using VSModifier.Memory.Scanning;

namespace VSModifier.Memory.Profiles;

public sealed class OffsetCatalog
{
    private static readonly HashSet<string> RequiredVerifiedFeatures = new(StringComparer.Ordinal)
    {
        "invulnerability", "quickTreasure", "maxTreasure", "gameSpeed", "damageMultiplier",
        "stat.growth", "stat.greed", "stat.luck", "stat.cooldown", "stat.moveSpeed",
        "stat.area", "stat.amount", "stat.duration", "stat.speed", "stat.armor",
        "stat.regen", "stat.magnet", "stat.revivals", "stat.rerolls", "stat.skips",
        "stat.banish", "stat.curse", "stat.charm", "stat.defang"
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public int SchemaVersion { get; init; } = 2;

    public List<GameVersionProfile> Profiles { get; init; } = [];

    public static OffsetCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static OffsetCatalog Parse(ReadOnlySpan<byte> utf8Json)
    {
        OffsetCatalog catalog = JsonSerializer.Deserialize<OffsetCatalog>(utf8Json, JsonOptions)
            ?? throw new InvalidDataException("offsets.json 內容為空。");
        if (catalog.SchemaVersion != 2)
        {
            throw new InvalidDataException($"不支援 offsets.json schemaVersion {catalog.SchemaVersion}。");
        }

        catalog.Validate();

        return catalog;
    }

    public ProfileMatchResult Match(GameVersionFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        GameVersionProfile[] gameAssemblyMatches = Profiles
            .Where(profile => string.Equals(
                profile.GameAssemblySha256,
                fingerprint.GameAssemblySha256,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (gameAssemblyMatches.Length == 0)
        {
            return new ProfileMatchResult(null, "offsets.json 沒有此 GameAssembly.dll 版本。");
        }

        GameVersionProfile[] unityPlayerMatches = gameAssemblyMatches
            .Where(profile => string.Equals(
                profile.UnityPlayerSha256,
                fingerprint.UnityPlayerSha256,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (unityPlayerMatches.Length == 0)
        {
            return new ProfileMatchResult(null, "GameAssembly.dll 已識別，但 UnityPlayer.dll 版本不符；可能是混合或尚未支援的遊戲更新。");
        }

        GameVersionProfile? exact = unityPlayerMatches.FirstOrDefault(profile => string.Equals(
            profile.Il2CppMetadataSha256,
            fingerprint.Il2CppMetadataSha256,
            StringComparison.OrdinalIgnoreCase));
        return exact is null
            ? new ProfileMatchResult(null, "兩個 DLL 已識別，但 global-metadata.dat 版本不符；可能是混合或尚未支援的遊戲更新。")
            : new ProfileMatchResult(exact, null);
    }

    private void Validate()
    {
        foreach (GameVersionProfile profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                throw new InvalidDataException("offsets.json Profile 缺少 profileId。");
            }

            if (!IsSha256(profile.GameAssemblySha256)
                || !IsSha256(profile.UnityPlayerSha256)
                || !IsSha256(profile.Il2CppMetadataSha256))
            {
                throw new InvalidDataException($"Profile {profile.Label} 的模組 SHA-256 格式無效。");
            }

            if (profile.Verified && profile.OnlineSession is null)
            {
                throw new InvalidDataException($"已驗證 Profile {profile.Label} 缺少線上會話防護。");
            }

            if (profile.OnlineSession is not null)
            {
                ValidateAddress(profile.OnlineSession, $"Profile {profile.Label} onlineSession");
            }

            foreach ((string featureKey, FeatureDefinition feature) in profile.Features)
            {
                ValidateFeature(profile.Label, featureKey, feature);
            }

            if (profile.Verified)
            {
                string[] missingFeatures = RequiredVerifiedFeatures
                    .Where(featureKey => !profile.Features.ContainsKey(featureKey))
                    .ToArray();
                if (missingFeatures.Length > 0)
                {
                    throw new InvalidDataException(
                        $"已驗證 Profile {profile.Label} 缺少必要功能：{string.Join(", ", missingFeatures)}。");
                }
            }
        }

        bool duplicate = Profiles
            .GroupBy(
                profile => $"{profile.GameAssemblySha256}:{profile.UnityPlayerSha256}:{profile.Il2CppMetadataSha256}",
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
        if (duplicate)
        {
            throw new InvalidDataException("offsets.json 包含重複的 GameAssembly／UnityPlayer／metadata 版本組合。");
        }

        if (Profiles.GroupBy(profile => profile.ProfileId, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("offsets.json 包含重複的 profileId。");
        }
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }

    private static void ValidateFeature(string profileLabel, string featureKey, FeatureDefinition feature)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
        {
            throw new InvalidDataException($"Profile {profileLabel} 包含空白功能鍵。");
        }

        if (!Enum.IsDefined(feature.Kind) || !Enum.IsDefined(feature.ValueType))
        {
            throw new InvalidDataException($"Profile {profileLabel} 的功能 {featureKey} 種類或數值型別無效。");
        }

        ValidateAddress(feature.Address, $"Profile {profileLabel} feature {featureKey}");
        if (feature.Kind == FeatureKind.Value)
        {
            if (!string.IsNullOrWhiteSpace(feature.ExpectedBytes)
                || !string.IsNullOrWhiteSpace(feature.PatchBytes)
                || feature.AdditionalPatches.Count > 0)
            {
                throw new InvalidDataException($"數值功能 {featureKey} 不得包含 patch 位元組。");
            }

            if (feature.MinValue is double minimum && !double.IsFinite(minimum)
                || feature.MaxValue is double maximum && !double.IsFinite(maximum)
                || feature.MinValue is double min
                    && feature.MaxValue is double max
                    && min > max)
            {
                throw new InvalidDataException($"數值功能 {featureKey} 的允許範圍無效。");
            }

            return;
        }

        if (feature.MinValue is not null || feature.MaxValue is not null || feature.PreserveZero)
        {
            throw new InvalidDataException($"非數值功能 {featureKey} 不得包含數值範圍或 preserveZero。");
        }

        if (feature.Kind == FeatureKind.Hook)
        {
            byte[] expected = ParseHexBytes(feature.ExpectedBytes, $"{featureKey} expectedBytes");
            if (expected.Length < 5)
            {
                throw new InvalidDataException($"Hook {featureKey} 的 expectedBytes 不足五個位元組。");
            }

            if (!string.IsNullOrWhiteSpace(feature.PatchBytes)
                || feature.AdditionalPatches.Count > 0
                || feature.Address.PointerOffsets.Count > 0)
            {
                throw new InvalidDataException($"Hook {featureKey} 只能使用直接程式碼位址與 expectedBytes。");
            }

            return;
        }

        ValidatePatchBytes(featureKey, feature.ExpectedBytes, feature.PatchBytes);
        foreach ((PatchSegmentDefinition segment, int index) in feature.AdditionalPatches.Select((segment, index) => (segment, index)))
        {
            ValidateAddress(segment.Address, $"Profile {profileLabel} feature {featureKey} patch {index + 2}");
            ValidatePatchBytes($"{featureKey} patch {index + 2}", segment.ExpectedBytes, segment.PatchBytes);
        }
    }

    private static void ValidateAddress(AddressDefinition address, string field)
    {
        if (string.IsNullOrWhiteSpace(address.Module))
        {
            throw new InvalidDataException($"{field} 缺少 module。");
        }

        if (address.BaseOffset < 0)
        {
            throw new InvalidDataException($"{field} 的 baseOffset 不得為負數。");
        }

        if (string.IsNullOrWhiteSpace(address.Aob))
        {
            if (address.RipRelativeOffset is not null)
            {
                throw new InvalidDataException($"{field} 沒有 AOB，不能設定 ripRelativeOffset。");
            }

            return;
        }

        if (address.BaseOffset != 0)
        {
            throw new InvalidDataException($"{field} 使用 AOB 時不得同時設定 baseOffset。");
        }

        AobPattern pattern;
        try
        {
            pattern = AobPattern.Parse(address.Aob);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new InvalidDataException($"{field} 的 AOB 格式無效。", exception);
        }

        if (address.RipRelativeOffset is int relativeOffset
            && (relativeOffset < 0 || relativeOffset + sizeof(int) > pattern.Length))
        {
            throw new InvalidDataException($"{field} 的 ripRelativeOffset 超出 AOB 範圍。");
        }
    }

    private static void ValidatePatchBytes(string featureKey, string? expectedText, string? patchText)
    {
        byte[] expected = ParseHexBytes(expectedText, $"{featureKey} expectedBytes");
        byte[] patch = ParseHexBytes(patchText, $"{featureKey} patchBytes");
        if (expected.Length != patch.Length)
        {
            throw new InvalidDataException($"Patch {featureKey} 的 expectedBytes 與 patchBytes 長度不同。");
        }
    }

    private static byte[] ParseHexBytes(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"缺少 {field}。");
        }

        try
        {
            return Convert.FromHexString(string.Concat(value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"{field} 不是有效的十六進位位元組。", exception);
        }
    }
}

public sealed record ProfileMatchResult(GameVersionProfile? Profile, string? Error);

public sealed class GameVersionProfile
{
    public required string ProfileId { get; init; }

    public required string GameAssemblySha256 { get; init; }

    public required string UnityPlayerSha256 { get; init; }

    public required string Il2CppMetadataSha256 { get; init; }

    public string Label { get; init; } = string.Empty;

    public bool Verified { get; init; }

    public AddressDefinition? OnlineSession { get; init; }

    public Dictionary<string, FeatureDefinition> Features { get; init; } = new(StringComparer.Ordinal);
}

public sealed class FeatureDefinition
{
    public required FeatureKind Kind { get; init; }

    public required AddressDefinition Address { get; init; }

    public MemoryValueType ValueType { get; init; }

    public bool PreserveZero { get; init; }

    public double? MinValue { get; init; }

    public double? MaxValue { get; init; }

    public string? ExpectedBytes { get; init; }

    public string? PatchBytes { get; init; }

    public List<PatchSegmentDefinition> AdditionalPatches { get; init; } = [];
}

public sealed class PatchSegmentDefinition
{
    public required AddressDefinition Address { get; init; }

    public required string ExpectedBytes { get; init; }

    public required string PatchBytes { get; init; }
}

public sealed class AddressDefinition
{
    public required string Module { get; init; }

    public long BaseOffset { get; init; }

    public string? Aob { get; init; }

    public int AobOffset { get; init; }

    public int? RipRelativeOffset { get; init; }

    public List<long> PointerOffsets { get; init; } = [];
}

public enum FeatureKind
{
    Value,
    Patch,
    Hook
}

public enum MemoryValueType
{
    Boolean,
    Byte,
    Int32,
    Int64,
    Float,
    Double
}
