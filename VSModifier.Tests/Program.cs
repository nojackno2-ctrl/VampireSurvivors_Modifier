using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VSModifier.Core.Saves;

namespace VSModifier.Tests;

internal static class Program
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    public static async Task<int> Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            ("checksum calculation and application", TestChecksum),
            ("invalid checksum rejection", TestInvalidChecksumRejection),
            ("save editor operations", TestSaveEditor),
            ("backup and safe write", TestBackupAndSafeWrite),
            ("running game blocks writes", TestRunningGameBlocksWrite)
        ];

        int failures = 0;
        foreach ((string name, Func<Task> run) in tests)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static Task TestChecksum()
    {
        string placeholder = new('0', 64);
        string raw = $"{{\"Coins\":42.5,\"checksum\":\"{placeholder}\"}}";
        string checksumInput = "{\"Coins\":42.5,\"checksum\":\"\"}";
        string expected = Convert.ToHexStringLower(SHA256.HashData(Utf8WithoutBom.GetBytes(checksumInput)));

        Equal(expected, SaveChecksum.Calculate(raw), "Calculated checksum differs from the known input hash.");
        string applied = SaveChecksum.Apply(raw);
        True(SaveChecksum.IsValid(applied), "Applied checksum must validate.");
        True(SaveChecksum.TryRead(applied, out string stored), "Applied checksum must remain readable.");
        Equal(expected, stored, "Stored checksum differs from expected.");
        return Task.CompletedTask;
    }

    private static Task TestInvalidChecksumRejection()
    {
        string invalid = $"{{\"Coins\":1,\"checksum\":\"{new string('0', 64)}\"}}";
        Throws<InvalidDataException>(() => SaveDocument.Parse(invalid));
        return Task.CompletedTask;
    }

    private static Task TestSaveEditor()
    {
        SaveDocument document = SaveDocument.Parse(CreateValidSave());
        SaveEditor editor = new(document);
        editor.MaximizeCommonResources();
        editor.SetFlag("AlwaysQuickTreasureAnim", true);
        editor.SetEggAttribute("ANTONIO", "power", 12);
        editor.SetEggAttribute("ANTONIO", "luck", 3);
        editor.UnlockAll(new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["UnlockedCharacters"] = ["ANTONIO", "IMELDA", "ANTONIO"]
        });

        string output = document.SerializeWithChecksum();
        True(SaveChecksum.IsValid(output), "Edited save must have a valid checksum.");
        JsonObject root = JsonNode.Parse(output)!.AsObject();
        Equal(1_000_000_000_000d, root["Coins"]!.GetValue<double>(), "Coins were not maximized.");
        True(root["AlwaysQuickTreasureAnim"]!.GetValue<bool>(), "Quick treasure flag was not enabled.");
        Equal(15d, root["EggData"]!["ANTONIO"]!["total"]!.GetValue<double>(), "Egg total was not synchronized.");
        Equal(2, root["UnlockedCharacters"]!.AsArray().Count, "Unlock IDs were not deduplicated.");
        return Task.CompletedTask;
    }

    private static async Task TestBackupAndSafeWrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VSModifierTests_{Guid.NewGuid():N}");
        string savePath = Path.Combine(directory, "SaveData");
        string backupPath = Path.Combine(directory, "backups");
        Directory.CreateDirectory(directory);
        try
        {
            string original = CreateValidSave();
            await File.WriteAllTextAsync(savePath, original, Utf8WithoutBom);
            SaveFileService service = new(new FakeProcessDetector(false));
            SaveDocument document = await service.LoadAsync(savePath);
            new SaveEditor(document).SetNumber("Coins", 777);

            SaveWriteResult result = await service.SaveAsync(savePath, document, backupPath);
            string written = await File.ReadAllTextAsync(savePath, Utf8WithoutBom);
            string backup = await File.ReadAllTextAsync(result.BackupPath, Utf8WithoutBom);
            byte[] bytes = await File.ReadAllBytesAsync(savePath);

            Equal(original, backup, "Backup must contain the exact original save.");
            True(SaveChecksum.IsValid(written), "Written save checksum is invalid.");
            Equal(777d, JsonNode.Parse(written)!["Coins"]!.GetValue<double>(), "Edited value was not written.");
            True(bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF, "Save must not contain a UTF-8 BOM.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task TestRunningGameBlocksWrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VSModifierTests_{Guid.NewGuid():N}");
        string savePath = Path.Combine(directory, "SaveData");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(savePath, CreateValidSave(), Utf8WithoutBom);
            SaveDocument document = SaveDocument.Parse(await File.ReadAllTextAsync(savePath, Utf8WithoutBom));
            SaveFileService service = new(new FakeProcessDetector(true));
            await ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(savePath, document, Path.Combine(directory, "backups")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateValidSave()
    {
        string placeholder = new('0', 64);
        string raw = $"{{\"Coins\":1,\"LifetimeCoins\":2,\"TotalCoins\":3,\"Seals\":0,\"AdventureStars\":0,\"AlwaysQuickTreasureAnim\":false,\"CheatCodeUsed\":false,\"EggData\":{{}},\"UnlockedCharacters\":[],\"checksum\":\"{placeholder}\"}}";
        return SaveChecksum.Apply(raw);
    }

    private static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    private sealed class FakeProcessDetector(bool running) : IGameProcessDetector
    {
        public bool IsGameRunning() => running;
    }
}
