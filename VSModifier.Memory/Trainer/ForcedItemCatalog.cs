using System.Text.Json;

namespace VSModifier.Memory.Trainer;

public sealed class ForcedItemCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public int SchemaVersion { get; init; } = 1;

    public List<ForcedItemDefinition> Items { get; init; } = [];

    public static ForcedItemCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ForcedItemCatalog catalog = JsonSerializer.Deserialize<ForcedItemCatalog>(
            File.ReadAllBytes(path),
            JsonOptions) ?? throw new InvalidDataException("forced-level-up-items.json 內容為空。");
        catalog.Validate();
        return catalog;
    }

    private void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"不支援 forced-level-up-items.json schemaVersion {SchemaVersion}。");
        }

        if (Items.Count == 0
            || Items.Any(item => item.Id <= 0 || string.IsNullOrWhiteSpace(item.Name)))
        {
            throw new InvalidDataException("強制升級道具清單包含無效的 ID 或名稱。");
        }

        if (Items.GroupBy(item => item.Id).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("強制升級道具清單包含重複 ID。");
        }
    }
}

public sealed record ForcedItemDefinition(int Id, string Name)
{
    public string DisplayName => $"{Name}（ID {Id}）";
}
