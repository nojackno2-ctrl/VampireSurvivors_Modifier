using System.Text.Json.Nodes;

namespace VSModifier.Core.Saves;

public sealed class SaveEditor(SaveDocument document)
{
    private readonly JsonObject _root = document?.Root ?? throw new ArgumentNullException(nameof(document));

    public void SetNumber(string propertyName, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "數值必須是有限值。");
        }

        _root[propertyName] = value;
    }

    public void SetInteger(string propertyName, int value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _root[propertyName] = value;
    }

    public void SetFlag(string propertyName, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _root[propertyName] = value;
    }

    public void MaximizeCommonResources()
    {
        SetNumber("Coins", 1_000_000_000_000d);
        SetNumber("LifetimeCoins", 1_000_000_000_000d);
        SetNumber("TotalCoins", 1_000_000_000_000d);
        SetInteger("Seals", 999_999);
        SetNumber("AdventureStars", 999_999d);
    }

    public void UnlockAll(IReadOnlyDictionary<string, IReadOnlyCollection<string>> idSets)
    {
        ArgumentNullException.ThrowIfNull(idSets);
        foreach ((string propertyName, IReadOnlyCollection<string> ids) in idSets)
        {
            JsonArray array = [];
            foreach (string id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                array.Add(id);
            }

            _root[propertyName] = array;
        }
    }

    public void SetAllProgressFlags(bool value)
    {
        string[] explicitFlags = ["CheatCodeUsed", "HasKilledTheFinalBoss", "Didit", "AlwaysQuickTreasureAnim", "SequentialChestMode"];
        HashSet<string> targets = new(explicitFlags, StringComparer.Ordinal);
        foreach ((string name, JsonNode? node) in _root)
        {
            if (node is JsonValue jsonValue
                && jsonValue.TryGetValue<bool>(out _)
                && (name.StartsWith("HasSeen", StringComparison.Ordinal)
                    || name.StartsWith("HasUsed", StringComparison.Ordinal)))
            {
                targets.Add(name);
            }
        }

        foreach (string propertyName in targets)
        {
            _root[propertyName] = value;
        }
    }

    public void SetEggAttribute(string characterId, string attribute, double value, bool updateTotal = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "蛋屬性必須是非負有限值。");
        }

        JsonObject eggData = GetOrCreateObject("EggData");
        JsonObject character = eggData[characterId] as JsonObject ?? new JsonObject();
        eggData[characterId] = character;
        character[attribute] = value;

        if (updateTotal && !string.Equals(attribute, "total", StringComparison.Ordinal))
        {
            double total = character
                .Where(pair => !string.Equals(pair.Key, "total", StringComparison.Ordinal))
                .Sum(pair => ReadNumber(pair.Value));
            character["total"] = total;
        }
    }

    private JsonObject GetOrCreateObject(string propertyName)
    {
        if (_root[propertyName] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = new();
        _root[propertyName] = created;
        return created;
    }

    private static double ReadNumber(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<double>(out double doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<long>(out long longValue))
        {
            return longValue;
        }

        return 0;
    }
}
