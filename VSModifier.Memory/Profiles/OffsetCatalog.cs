using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSModifier.Memory.Profiles;

public sealed class OffsetCatalog
{
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
        string json = File.ReadAllText(path);
        OffsetCatalog catalog = JsonSerializer.Deserialize<OffsetCatalog>(json, JsonOptions)
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
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
}

public sealed record ProfileMatchResult(GameVersionProfile? Profile, string? Error);

public sealed class GameVersionProfile
{
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
    Patch
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
