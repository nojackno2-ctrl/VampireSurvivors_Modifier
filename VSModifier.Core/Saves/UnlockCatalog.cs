using System.Text.Json;
using System.Text.Json.Nodes;

namespace VSModifier.Core.Saves;

public sealed class UnlockCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public int SchemaVersion { get; init; } = 1;

    public List<UnlockCatalogProfile> Profiles { get; init; } = [];

    public static UnlockCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        UnlockCatalog catalog = JsonSerializer.Deserialize<UnlockCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("unlocks.json 內容為空。");
        catalog.Validate();
        return catalog;
    }

    public UnlockCatalogProfile? FindByProfileId(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return Profiles.FirstOrDefault(profile => string.Equals(
            profile.ProfileId,
            profileId,
            StringComparison.Ordinal));
    }

    private void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"不支援 unlocks.json schemaVersion {SchemaVersion}。");
        }

        foreach (UnlockCatalogProfile profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                throw new InvalidDataException("unlocks.json Profile 缺少 profileId。");
            }

            foreach ((string propertyName, JsonNode? value) in profile.Arrays)
            {
                if (value is not JsonArray array)
                {
                    throw new InvalidDataException($"Profile {profile.ProfileId} 的 {propertyName} 必須是陣列。");
                }

                if (array.Any(item => item is not JsonValue))
                {
                    throw new InvalidDataException($"Profile {profile.ProfileId} 的 {propertyName} 只能包含字串或數字 ID。");
                }
            }
        }

        if (Profiles.GroupBy(profile => profile.ProfileId, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("unlocks.json 包含重複的 profileId。");
        }
    }
}

public sealed class UnlockCatalogProfile
{
    public required string ProfileId { get; init; }

    public string Label { get; init; } = string.Empty;

    public JsonObject Arrays { get; init; } = [];
}
