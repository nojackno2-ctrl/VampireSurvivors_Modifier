using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VSModifier.IdExtractor;

internal static partial class Program
{
    private static readonly string[] WantedEnums =
    [
        "AchievementType", "ArcanaType", "CharacterType", "ItemType",
        "SecretType", "SkinType", "StageType", "WeaponType"
    ];

    [GeneratedRegex("^public enum (?<name>[A-Za-z0-9_.]+) ", RegexOptions.CultureInvariant)]
    private static partial Regex EnumStartRegex();

    [GeneratedRegex("^\\s*public const (?<type>[A-Za-z0-9_.]+) (?<name>[A-Za-z0-9_]+) = (?<value>-?[0-9]+);", RegexOptions.CultureInvariant)]
    private static partial Regex EnumValueRegex();

    public static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: VSModifier.IdExtractor <dump.cs> <profile-id> <unlocks.json>");
            return 2;
        }

        Dictionary<string, List<(string Name, int Value)>> enums = ReadEnums(args[0]);
        JsonObject arrays = BuildCatalog(enums);
        string profileId = args[1];
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        string outputPath = args[2];
        JsonObject output = LoadOrCreateCatalog(outputPath);
        JsonArray profiles = output["profiles"]!.AsArray();
        JsonObject profile = new()
        {
            ["profileId"] = profileId,
            ["label"] = profileId,
            ["arrays"] = arrays
        };
        int existingIndex = -1;
        for (int index = 0; index < profiles.Count; index++)
        {
            if (string.Equals(profiles[index]?["profileId"]?.GetValue<string>(), profileId, StringComparison.Ordinal))
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            profiles[existingIndex] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            outputPath,
            output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Wrote {arrays.Count} unlock arrays for profile {profileId} without copying dump.cs into the project.");
        return 0;
    }

    private static JsonObject LoadOrCreateCatalog(string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["profiles"] = new JsonArray()
            };
        }

        JsonObject root = JsonNode.Parse(File.ReadAllText(outputPath)) as JsonObject
            ?? throw new InvalidDataException("unlocks.json 根節點必須是 object。");
        if (root["schemaVersion"]?.GetValue<int>() != 1 || root["profiles"] is not JsonArray)
        {
            throw new InvalidDataException("unlocks.json 必須使用 schemaVersion 1 的 profiles 格式。");
        }

        return root;
    }

    private static Dictionary<string, List<(string Name, int Value)>> ReadEnums(string dumpPath)
    {
        HashSet<string> wanted = new(WantedEnums, StringComparer.Ordinal);
        Dictionary<string, List<(string Name, int Value)>> result = new(StringComparer.Ordinal);
        string? current = null;
        foreach (string line in File.ReadLines(dumpPath))
        {
            Match start = EnumStartRegex().Match(line);
            if (start.Success)
            {
                string name = start.Groups["name"].Value;
                current = wanted.Contains(name) ? name : null;
                if (current is not null)
                {
                    result[current] = [];
                }

                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line == "}")
            {
                current = null;
                continue;
            }

            Match value = EnumValueRegex().Match(line);
            if (value.Success && string.Equals(value.Groups["type"].Value, current, StringComparison.Ordinal))
            {
                result[current].Add((value.Groups["name"].Value, int.Parse(value.Groups["value"].Value)));
            }
        }

        string[] missing = WantedEnums.Where(name => !result.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"dump.cs 缺少 enum：{string.Join(", ", missing)}");
        }

        return result;
    }

    private static JsonObject BuildCatalog(Dictionary<string, List<(string Name, int Value)>> enums)
    {
        JsonObject catalog = new();
        string[] characters = Names(enums["CharacterType"], "VOID");
        string[] weapons = Names(enums["WeaponType"], "VOID");
        string[] stages = Names(enums["StageType"]);
        catalog["BoughtCharacters"] = ToArray(characters);
        catalog["UnlockedCharacters"] = ToArray(characters);
        catalog["UnlockedWeapons"] = ToArray(weapons);
        catalog["CollectedWeapons"] = ToArray(weapons);
        catalog["CollectedItems"] = ToArray(Names(enums["ItemType"], "VOID"));
        catalog["UnlockedStages"] = ToArray(stages);
        catalog["UnlockedHypers"] = ToArray(stages);
        catalog["UnlockedArcanas"] = new JsonArray(enums["ArcanaType"]
            .Where(value => value.Value >= 0 && !string.Equals(value.Name, "VOID", StringComparison.OrdinalIgnoreCase))
            .Select(value => JsonValue.Create(value.Value) as JsonNode)
            .ToArray());
        catalog["Achievements"] = ToArray(Names(enums["AchievementType"]));
        catalog["Secrets"] = ToArray(Names(enums["SecretType"], "none"));
        catalog["BoughtSkins"] = ToArray(Names(enums["SkinType"], "EMPTY"));
        return catalog;
    }

    private static string[] Names(IEnumerable<(string Name, int Value)> values, params string[] excluded)
    {
        HashSet<string> exclusions = new(excluded, StringComparer.OrdinalIgnoreCase);
        return values
            .Where(value => value.Value >= 0 && !exclusions.Contains(value.Name))
            .Select(value => value.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        return new JsonArray(values.Select(value => JsonValue.Create(value) as JsonNode).ToArray());
    }
}
