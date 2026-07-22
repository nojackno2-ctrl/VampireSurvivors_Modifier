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

    public void ReplaceArray(string propertyName, IEnumerable<JsonNode?> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(values);
        JsonArray array = [];
        foreach (JsonNode? value in values)
        {
            array.Add(value?.DeepClone());
        }

        _root[propertyName] = array;
    }

    public void MergeUnlockCatalog(JsonObject catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Dictionary<string, JsonArray> mergedArrays = new(StringComparer.Ordinal);
        foreach ((string propertyName, JsonNode? value) in catalog)
        {
            if (value is not JsonArray catalogArray)
            {
                throw new InvalidDataException($"解鎖資料 {propertyName} 必須是陣列。");
            }

            if (_root[propertyName] is not null and not JsonArray)
            {
                throw new InvalidDataException($"存檔欄位 {propertyName} 不是陣列，已拒絕套用以避免破壞結構。");
            }

            JsonArray merged = [];
            HashSet<string> existingIds = new(StringComparer.Ordinal);
            if (_root[propertyName] is JsonArray existingArray)
            {
                foreach (JsonNode? item in existingArray)
                {
                    merged.Add(item?.DeepClone());
                    if (item is not null)
                    {
                        existingIds.Add(item.ToJsonString());
                    }
                }
            }

            foreach (JsonNode? item in catalogArray)
            {
                if (item is null)
                {
                    throw new InvalidDataException($"解鎖資料 {propertyName} 不得包含 null ID。");
                }

                if (existingIds.Add(item.ToJsonString()))
                {
                    merged.Add(item.DeepClone());
                }
            }

            mergedArrays.Add(propertyName, merged);
        }

        foreach ((string propertyName, JsonArray merged) in mergedArrays)
        {
            _root[propertyName] = merged;
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
        if (string.Equals(attribute, "total", StringComparison.Ordinal))
        {
            throw new ArgumentException("total 是由各金蛋屬性加總的衍生值，不能獨立修改。", nameof(attribute));
        }

        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "蛋屬性必須是非負有限值。");
        }

        JsonObject eggData = GetOrCreateObject("EggData");
        string normalizedCharacterId = eggData
            .Select(pair => pair.Key)
            .FirstOrDefault(existing => string.Equals(existing, characterId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? characterId.Trim().ToUpperInvariant();
        JsonObject character = eggData[normalizedCharacterId] as JsonObject ?? new JsonObject();
        eggData[normalizedCharacterId] = character;
        character[attribute] = value;

        if (updateTotal && !string.Equals(attribute, "total", StringComparison.Ordinal))
        {
            double total = character
                .Where(pair => !string.Equals(pair.Key, "total", StringComparison.Ordinal))
                .Sum(pair => ReadNumber(pair.Value));
            character["total"] = total;
        }
    }

    public bool TryGetEggAttribute(string characterId, string attribute, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(characterId)
            || string.IsNullOrWhiteSpace(attribute)
            || _root["EggData"] is not JsonObject eggData)
        {
            return false;
        }

        JsonObject? character = eggData
            .FirstOrDefault(pair => string.Equals(pair.Key, characterId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Value as JsonObject;
        if (character is null || !TryReadNumber(character[attribute], out value))
        {
            return false;
        }

        return true;
    }

    public IReadOnlyList<string> GetEggAttributeNames()
    {
        if (_root["EggData"] is not JsonObject eggData)
        {
            return [];
        }

        return eggData
            .SelectMany(character => (character.Value as JsonObject)?.AsEnumerable()
                ?? Enumerable.Empty<KeyValuePair<string, JsonNode?>>())
            .Where(attribute => !string.Equals(attribute.Key, "total", StringComparison.Ordinal)
                && TryReadNumber(attribute.Value, out _))
            .Select(attribute => attribute.Key)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
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
        return TryReadNumber(node, out double value) ? value : 0;
    }

    private static bool TryReadNumber(JsonNode? node, out double result)
    {
        result = 0;
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<double>(out result))
        {
            return double.IsFinite(result);
        }

        if (value.TryGetValue<long>(out long longValue))
        {
            result = longValue;
            return true;
        }

        if (value.TryGetValue<decimal>(out decimal decimalValue))
        {
            result = (double)decimalValue;
            return double.IsFinite(result);
        }

        return false;
    }
}
