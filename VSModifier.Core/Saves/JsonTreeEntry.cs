using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VSModifier.Core.Saves;

public sealed class JsonTreeEntry
{
    private JsonTreeEntry(
        string name,
        string path,
        JsonNode? node,
        JsonNode? parent,
        string? propertyName,
        int? arrayIndex)
    {
        Name = name;
        Path = path;
        Node = node;
        Parent = parent;
        PropertyName = propertyName;
        ArrayIndex = arrayIndex;
        Kind = ReadKind(node);
        Label = BuildLabel(name, node, Kind);

        if (node is JsonObject jsonObject)
        {
            foreach ((string key, JsonNode? child) in jsonObject)
            {
                Children.Add(new JsonTreeEntry(key, $"{path}.{key}", child, jsonObject, key, null));
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (int index = 0; index < jsonArray.Count; index++)
            {
                Children.Add(new JsonTreeEntry($"[{index}]", $"{path}[{index}]", jsonArray[index], jsonArray, null, index));
            }
        }
    }

    public string Name { get; }

    public string Label { get; }

    public string Path { get; }

    public JsonValueKind Kind { get; }

    public JsonNode? Node { get; }

    public JsonNode? Parent { get; }

    public string? PropertyName { get; }

    public int? ArrayIndex { get; }

    public ObservableCollection<JsonTreeEntry> Children { get; } = [];

    public bool CanEditValue => Parent is not null && Kind is not JsonValueKind.Object and not JsonValueKind.Array;

    public string EditableValue => Kind == JsonValueKind.String && Node is JsonValue value && value.TryGetValue(out string? text)
        ? text ?? string.Empty
        : Node?.ToJsonString() ?? "null";

    public static JsonTreeEntry CreateRoot(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new JsonTreeEntry("$", "$", root, null, null, null);
    }

    public void ReplaceValue(JsonNode? replacement)
    {
        if (!CanEditValue)
        {
            throw new InvalidOperationException("只能直接修改非根節點的純量值。");
        }

        if (Parent is JsonObject jsonObject && PropertyName is not null)
        {
            jsonObject[PropertyName] = replacement;
            return;
        }

        if (Parent is JsonArray jsonArray && ArrayIndex is int index)
        {
            jsonArray[index] = replacement;
            return;
        }

        throw new InvalidOperationException("JSON 節點沒有可用的父容器。");
    }

    private static JsonValueKind ReadKind(JsonNode? node)
    {
        if (node is null)
        {
            return JsonValueKind.Null;
        }

        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.ValueKind;
    }

    private static string BuildLabel(string name, JsonNode? node, JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Object => $"{name}  {{{((JsonObject)node!).Count}}}",
            JsonValueKind.Array => $"{name}  [{((JsonArray)node!).Count}]",
            JsonValueKind.String => $"{name}: \"{Truncate(((JsonValue)node!).GetValue<string>())}\"",
            _ => $"{name}: {Truncate(node?.ToJsonString() ?? "null")}" 
        };
    }

    private static string Truncate(string value)
    {
        const int maxLength = 72;
        return value.Length <= maxLength ? value : $"{value[..maxLength]}…";
    }
}
