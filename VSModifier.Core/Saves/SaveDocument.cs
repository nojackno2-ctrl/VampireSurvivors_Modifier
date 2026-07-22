using System.Text.Json;
using System.Text.Json.Nodes;

namespace VSModifier.Core.Saves;

public sealed class SaveDocument
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false
    };

    private SaveDocument(JsonObject root, string originalText)
    {
        Root = root;
        OriginalText = originalText;
    }

    public JsonObject Root { get; private set; }

    public string OriginalText { get; private set; }

    public bool OriginalChecksumIsValid => SaveChecksum.IsValid(OriginalText);

    public static SaveDocument Parse(string json, bool requireValidChecksum = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (requireValidChecksum && !SaveChecksum.IsValid(json))
        {
            throw new InvalidDataException("存檔 checksum 驗證失敗；為避免毀損，已拒絕載入。");
        }

        JsonNode? parsed = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });

        if (parsed is not JsonObject root)
        {
            throw new InvalidDataException("SaveData 根節點必須是 JSON object。");
        }

        return new SaveDocument(root, json);
    }

    public void ReplaceJson(string json, bool requireValidChecksum = false)
    {
        SaveDocument replacement = Parse(json, requireValidChecksum);
        Root = replacement.Root;
    }

    public string SerializeWithChecksum()
    {
        EnsureChecksumPlaceholder();
        string compact = Root.ToJsonString(CompactJson);
        return SaveChecksum.Apply(compact);
    }

    public void AcceptSavedText(string savedText)
    {
        if (!SaveChecksum.IsValid(savedText))
        {
            throw new InvalidDataException("無法接受 checksum 無效的已儲存內容。");
        }

        OriginalText = savedText;
    }

    private void EnsureChecksumPlaceholder()
    {
        string placeholder = new('0', 64);
        if (Root["checksum"] is JsonValue checksumValue
            && checksumValue.TryGetValue<string>(out string? existing)
            && existing?.Length == 64)
        {
            Root["checksum"] = placeholder;
            return;
        }

        Root["checksum"] = placeholder;
    }
}
