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

    public int SchemaVersion { get; init; } = 1;

    public List<GameVersionProfile> Profiles { get; init; } = [];

    public static OffsetCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        OffsetCatalog catalog = JsonSerializer.Deserialize<OffsetCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("offsets.json 內容為空。");
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidDataException($"不支援 offsets.json schemaVersion {catalog.SchemaVersion}。");
        }

        return catalog;
    }

    public GameVersionProfile? FindByHash(string sha256)
    {
        return Profiles.FirstOrDefault(profile =>
            string.Equals(profile.GameAssemblySha256, sha256, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class GameVersionProfile
{
    public required string GameAssemblySha256 { get; init; }

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
}

public sealed class AddressDefinition
{
    public required string Module { get; init; }

    public long BaseOffset { get; init; }

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
