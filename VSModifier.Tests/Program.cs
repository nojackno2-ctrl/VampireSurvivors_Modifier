using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VSModifier.Core.Game;
using VSModifier.Core.Saves;
using VSModifier.Memory.Locking;
using VSModifier.Memory.Patching;
using VSModifier.Memory.ProcessMemory;
using VSModifier.Memory.Profiles;
using VSModifier.Memory.Scanning;
using VSModifier.Memory.Trainer;

namespace VSModifier.Tests;

internal static class Program
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--live-read-only", StringComparer.OrdinalIgnoreCase))
        {
            return await RunLiveReadOnlyCheck();
        }

        if (args.Contains("--inspect-trainer-read-only", StringComparer.OrdinalIgnoreCase))
        {
            return RunTrainerReadOnlyInspection();
        }

        if (args.Contains("--inspect-time-scale-read-only", StringComparer.OrdinalIgnoreCase))
        {
            return RunTimeScaleReadOnlyInspection();
        }

        (string Name, Func<Task> Run)[] tests =
        [
            ("checksum calculation and application", TestChecksum),
            ("invalid checksum rejection", TestInvalidChecksumRejection),
            ("save editor operations", TestSaveEditor),
            ("backup and safe write", TestBackupAndSafeWrite),
            ("running game blocks writes", TestRunningGameBlocksWrite),
            ("AOB wildcard matching", TestAobPattern),
            ("pointer chain resolution", TestPointerChain),
            ("profile AOB RIP-relative resolution", TestProfileAobResolution),
            ("reversible memory patch", TestMemoryPatch),
            ("offset catalog parsing", TestOffsetCatalog),
            ("value lock enforcement", TestValueLockService)
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

    private static async Task<int> RunLiveReadOnlyCheck()
    {
        IReadOnlyList<SaveCandidate> candidates = new SavePathLocator().FindCandidates();
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine("FAIL  no Steam SaveData candidate was found.");
            return 1;
        }

        SaveDocument document = await new SaveFileService().LoadAsync(candidates[0].Path);
        IReadOnlyList<GameInstallation> installations = new GameInstallationLocator().FindInstallations();
        if (installations.Count == 0)
        {
            Console.Error.WriteLine("FAIL  no complete game installation was found.");
            return 1;
        }

        string fingerprint = GameAssemblyFingerprint.CalculateSha256(installations[0].GameAssemblyPath);
        OffsetCatalog catalog = OffsetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "offsets.json"));
        GameVersionProfile? profile = catalog.FindByHash(fingerprint);
        Console.WriteLine($"PASS  located {candidates.Count} SaveData candidate(s).");
        Console.WriteLine($"PASS  live checksum valid: {document.OriginalChecksumIsValid}.");
        Console.WriteLine($"PASS  parsed {document.Root.Count} top-level fields without writing.");
        Console.WriteLine($"PASS  located {installations.Count} complete game installation(s).");
        Console.WriteLine($"PASS  current GameAssembly profile registered: {profile is not null}.");
        Console.WriteLine($"PASS  current profile remains fail-closed: {profile is { Verified: false }}.");
        return document.OriginalChecksumIsValid && profile is { Verified: false } ? 0 : 1;
    }

    private static int RunTrainerReadOnlyInspection()
    {
        GameInstallation installation = new GameInstallationLocator().FindInstallations().FirstOrDefault()
            ?? throw new InvalidOperationException("找不到完整遊戲安裝。");
        OffsetCatalog catalog = OffsetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "offsets.json"));
        TrainerDiagnosticResult result = TrainerDiagnostics.InspectReadOnly(catalog, installation.GameAssemblyPath);
        Console.WriteLine($"Profile: {result.ProfileLabel}; verified={result.ProfileVerified}");
        PrintDiagnostic(result.OnlineSession);
        foreach (DiagnosticValue feature in result.Features)
        {
            PrintDiagnostic(feature);
        }

        int successes = result.Features.Count(feature => feature.Success);
        Console.WriteLine($"Read-only feature chains: {successes}/{result.Features.Count} resolved.");
        return result.OnlineSession.Success ? 0 : 1;
    }

    private static int RunTimeScaleReadOnlyInspection()
    {
        const long timeScaleGetterPointerRva = 0x9DF0F08;
        const long timeScaleSetterPointerRva = 0x9DF0F10;

        using ProcessMemorySession memory = ProcessMemorySession.AttachReadOnly();
        ProcessModuleInfo gameAssembly = memory.GetModuleInfo("GameAssembly.dll");
        ProcessModuleInfo unityPlayer = memory.GetModuleInfo("UnityPlayer.dll");
        nint getter = ReadPointer(memory, checked(gameAssembly.BaseAddress + (nint)timeScaleGetterPointerRva));
        nint setter = ReadPointer(memory, checked(gameAssembly.BaseAddress + (nint)timeScaleSetterPointerRva));

        PrintNativeTarget("Time.get_timeScale", getter, unityPlayer);
        PrintNativeTarget("Time.set_timeScale", setter, unityPlayer);
        return getter != 0 && setter != 0 ? 0 : 1;
    }

    private static nint ReadPointer(ProcessMemorySession memory, nint address)
    {
        return (nint)BitConverter.ToInt64(memory.ReadBytes(address, sizeof(long)));
    }

    private static void PrintNativeTarget(string name, nint address, ProcessModuleInfo unityPlayer)
    {
        long rva = (long)address - (long)unityPlayer.BaseAddress;
        bool belongsToUnityPlayer = rva >= 0 && rva < unityPlayer.Size;
        Console.WriteLine(
            $"{name}: address=0x{address:X}; "
            + (belongsToUnityPlayer ? $"UnityPlayer.dll+0x{rva:X}" : "outside UnityPlayer.dll"));
    }

    private static void PrintDiagnostic(DiagnosticValue value)
    {
        string detail = value.Success
            ? value.Value.HasValue ? $"value={value.Value.Value:R}" : "bytes readable"
            : $"error={value.Error}";
        Console.WriteLine($"{(value.Success ? "PASS" : "WAIT")}  {value.Key}: {detail}");
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
        Equal(3, root["UnlockedCharacters"]!.AsArray().Count, "Unlock array duplicates must be preserved.");

        editor.ReplaceArray("UnlockedArcanas", new JsonNode?[] { JsonValue.Create(3), JsonValue.Create(3), JsonValue.Create(7) });
        JsonArray arcanas = JsonNode.Parse(document.SerializeWithChecksum())!["UnlockedArcanas"]!.AsArray();
        Equal(3, arcanas.Count, "Numeric unlock array count differs.");
        Equal(3, arcanas[0]!.GetValue<int>(), "Numeric unlock type was not preserved.");
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

    private static Task TestAobPattern()
    {
        AobPattern pattern = AobPattern.Parse("48 8B ?? 90");
        IReadOnlyList<int> matches = pattern.FindAll([0x48, 0x8B, 0x01, 0x90, 0x00, 0x48, 0x8B, 0xFF, 0x90]);
        Equal(2, matches.Count, "AOB match count differs.");
        Equal(0, matches[0], "First AOB offset differs.");
        Equal(5, matches[1], "Second AOB offset differs.");

        FakeMemory memory = new();
        memory.WriteBytes((nint)0x1005, new byte[] { 0x48, 0x8B, 0x7F, 0x90 });
        IReadOnlyList<nint> scanned = AobScanner.Scan(memory, (nint)0x1000, 0x20, pattern, chunkSize: 6);
        Equal(1, scanned.Count, "Chunked AOB scanner match count differs.");
        Equal((nint)0x1005, scanned[0], "Chunked AOB scanner missed a boundary match.");
        return Task.CompletedTask;
    }

    private static Task TestPointerChain()
    {
        FakeMemory memory = new();
        memory.Write((nint)0x1020, (nint)0x1100);
        nint resolved = PointerChain.Resolve(memory, (nint)0x1000, [0x20, 0x08]);
        Equal((nint)0x1108, resolved, "Pointer chain produced the wrong address.");
        return Task.CompletedTask;
    }

    private static Task TestProfileAobResolution()
    {
        FakeMemory memory = new();
        nint match = (nint)0x1010;
        nint pointerSlot = (nint)0x1080;
        int displacement = checked((int)((long)pointerSlot - ((long)match + 7)));
        memory.WriteBytes(match,
        [
            0x48, 0x8B, 0x05,
            .. BitConverter.GetBytes(displacement),
            0xF3, 0x0F, 0x10, 0x80, 0xAC, 0x01, 0x00, 0x00, 0xC3
        ]);
        memory.Write(pointerSlot, (nint)0x1100);
        AddressDefinition definition = new()
        {
            Module = "UnityPlayer.dll",
            Aob = "48 8B 05 ?? ?? ?? ?? F3 0F 10 80 AC 01 00 00 C3",
            RipRelativeOffset = 3,
            PointerOffsets = [0, 0x1AC]
        };

        nint resolved = ProfileAddressResolver.ResolveFromModule(memory, (nint)0x1000, 0x200, definition);
        Equal((nint)0x12AC, resolved, "AOB RIP-relative resolution produced the wrong address.");
        return Task.CompletedTask;
    }

    private static Task TestMemoryPatch()
    {
        FakeMemory memory = new();
        memory.WriteBytes((nint)0x1010, [0x74, 0x05]);
        using MemoryPatch patch = new(memory, (nint)0x1010, new byte[] { 0x90, 0x90 }, new byte[] { 0x74, 0x05 });
        patch.Enable();
        True(memory.ReadBytes((nint)0x1010, 2).SequenceEqual(new byte[] { 0x90, 0x90 }), "Patch bytes were not written.");
        patch.Disable();
        True(memory.ReadBytes((nint)0x1010, 2).SequenceEqual(new byte[] { 0x74, 0x05 }), "Original bytes were not restored.");
        True(memory.CodeWriteCount >= 2, "Code writes did not use the protected path.");
        return Task.CompletedTask;
    }

    private static Task TestOffsetCatalog()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VSModifierTests_{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "offsets.json");
        Directory.CreateDirectory(directory);
        try
        {
            string hash = new('a', 64);
            File.WriteAllText(path, $$"""
                {
                  "schemaVersion": 1,
                  "profiles": [
                    {
                      "gameAssemblySha256": "{{hash}}",
                      "label": "test",
                      "verified": false,
                      "features": {}
                    }
                  ]
                }
                """, Utf8WithoutBom);
            OffsetCatalog catalog = OffsetCatalog.Load(path);
            Equal(1, catalog.Profiles.Count, "Profile count differs.");
            True(catalog.FindByHash(hash.ToUpperInvariant()) is not null, "Hash lookup must be case-insensitive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static async Task TestValueLockService()
    {
        int counter = 0;
        await using ValueLockService service = new(TimeSpan.FromMilliseconds(15));
        service.Set("counter", () => Interlocked.Increment(ref counter));
        await Task.Delay(80);
        service.Remove("counter");
        True(Volatile.Read(ref counter) >= 2, "Value lock did not enforce repeatedly.");
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

    private sealed class FakeMemory : IProtectedMemoryAccessor
    {
        private const long BaseAddress = 0x1000;
        private readonly byte[] _bytes = new byte[0x400];

        public int CodeWriteCount { get; private set; }

        public byte[] ReadBytes(nint address, int length)
        {
            int offset = checked((int)((long)address - BaseAddress));
            return _bytes.AsSpan(offset, length).ToArray();
        }

        public void WriteBytes(nint address, ReadOnlySpan<byte> bytes)
        {
            int offset = checked((int)((long)address - BaseAddress));
            bytes.CopyTo(_bytes.AsSpan(offset, bytes.Length));
        }

        public void WriteCodeBytes(nint address, ReadOnlySpan<byte> bytes)
        {
            CodeWriteCount++;
            WriteBytes(address, bytes);
        }
    }
}
