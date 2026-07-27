using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
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

        if (args.Length == 2 && args[0].Equals("--inspect-trainer-chain", StringComparison.OrdinalIgnoreCase))
        {
            return RunTrainerChainInspection(args[1]);
        }

        if (args.Length > 0 && args[0].Equals("--verify-trainer-value", StringComparison.OrdinalIgnoreCase))
        {
            return await RunTrainerValueVerification(args);
        }

        if (args.Length > 0 && args[0].Equals("--verify-trainer-patch", StringComparison.OrdinalIgnoreCase))
        {
            return await RunTrainerPatchVerification(args);
        }

        if (args.Length > 0 && args[0].Equals("--verify-trainer-hook", StringComparison.OrdinalIgnoreCase))
        {
            return await RunTrainerHookVerification(args);
        }

        if (args.Length > 0 && args[0].Equals("--verify-force-item-behavior", StringComparison.OrdinalIgnoreCase))
        {
            return await RunForcedItemBehaviorVerification(args);
        }

        (string Name, Func<Task> Run)[] tests =
        [
            ("checksum calculation and application", TestChecksum),
            ("invalid checksum rejection", TestInvalidChecksumRejection),
            ("save editor operations", TestSaveEditor),
            ("versioned unlock catalog and safe merge", TestUnlockCatalogMerge),
            ("shipped version profile data consistency", TestShippedProfileData),
            ("JSON tree scalar editing", TestJsonTreeEditing),
            ("backup and safe write", TestBackupAndSafeWrite),
            ("running game blocks writes", TestRunningGameBlocksWrite),
            ("AOB wildcard matching", TestAobPattern),
            ("pointer chain resolution", TestPointerChain),
            ("profile AOB RIP-relative resolution", TestProfileAobResolution),
            ("reversible memory patch", TestMemoryPatch),
            ("atomic composite memory patch", TestMemoryPatchSet),
            ("x64 trainer hook payloads", TestTrainerHookPayloads),
            ("reversible remote code hook", TestRemoteCodeHook),
            ("offset catalog parsing", TestOffsetCatalog),
            ("offset catalog live reload detection", TestOffsetCatalogLiveReload),
            ("trainer verification policy", TestTrainerVerificationPolicy),
            ("trainer same-address restoration and one-shot values", TestTrainerValueSafety),
            ("value lock enforcement", TestValueLockService),
            ("shared safety guard runs once per tick", TestValueLockSharedGuard),
            ("online session blocks trainer attach", TestTrainerRejectsOnlineSession),
            ("native process memory round trip", TestProcessMemoryRoundTrip)
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

        GameVersionFingerprint fingerprint = GameVersionFingerprint.Calculate(
            installations[0].GameAssemblyPath,
            installations[0].UnityPlayerPath,
            installations[0].MetadataPath);
        OffsetCatalog catalog = OffsetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "offsets.json"));
        ProfileMatchResult match = catalog.Match(fingerprint);
        GameVersionProfile? profile = match.Profile;
        UnlockCatalog unlockCatalog = UnlockCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "ids", "unlocks.json"));
        UnlockCatalogProfile? unlockProfile = profile is null ? null : unlockCatalog.FindByProfileId(profile.ProfileId);
        Console.WriteLine($"PASS  located {candidates.Count} SaveData candidate(s).");
        Console.WriteLine($"PASS  live checksum valid: {document.OriginalChecksumIsValid}.");
        Console.WriteLine($"PASS  parsed {document.Root.Count} top-level fields without writing.");
        Console.WriteLine($"PASS  located {installations.Count} complete game installation(s).");
        Console.WriteLine($"PASS  current DLL/metadata profile combination registered: {profile is not null}.");
        Console.WriteLine($"PASS  matching version unlock catalog registered: {unlockProfile is not null}.");
        Console.WriteLine($"PASS  current profile remains fail-closed: {profile is { Verified: false }}.");
        return document.OriginalChecksumIsValid && profile is { Verified: false } && unlockProfile is not null ? 0 : 1;
    }

    private static int RunTrainerReadOnlyInspection()
    {
        GameInstallation installation = new GameInstallationLocator().FindInstallations().FirstOrDefault()
            ?? throw new InvalidOperationException("找不到完整遊戲安裝。");
        OffsetCatalog catalog = OffsetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "offsets.json"));
        TrainerDiagnosticResult result = TrainerDiagnostics.InspectReadOnly(
            catalog,
            installation.GameAssemblyPath,
            installation.UnityPlayerPath,
            installation.MetadataPath);
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

    private static int RunTrainerChainInspection(string featureKey)
    {
        GameInstallation installation = GetCurrentGameInstallation();
        OffsetCatalog catalog = LoadOffsetCatalog();
        GameVersionFingerprint fingerprint = GameVersionFingerprint.Calculate(
            installation.GameAssemblyPath,
            installation.UnityPlayerPath,
            installation.MetadataPath);
        GameVersionProfile profile = catalog.Match(fingerprint).Profile
            ?? throw new InvalidOperationException("目前遊戲版本沒有匹配的 Profile。");
        FeatureDefinition feature = profile.Features.TryGetValue(featureKey, out FeatureDefinition? found)
            ? found
            : throw new KeyNotFoundException($"Profile 沒有功能 {featureKey}。");

        using ProcessMemorySession memory = ProcessMemorySession.AttachReadOnly();
        ProcessModuleInfo module = memory.GetModuleInfo(feature.Address.Module);
        nint current = module.BaseAddress + checked((nint)feature.Address.BaseOffset);
        Console.WriteLine($"{featureKey}: root=0x{current:X} ({feature.Address.Module}+0x{feature.Address.BaseOffset:X})");
        for (int index = 0; index < feature.Address.PointerOffsets.Count - 1; index++)
        {
            long offset = feature.Address.PointerOffsets[index];
            nint pointerAddress = current + checked((nint)offset);
            nint next = memory.Read<nint>(pointerAddress);
            Console.WriteLine($"[{index}] [0x{pointerAddress:X}] (+0x{offset:X}) -> 0x{next:X}");
            if (next == 0)
            {
                return 1;
            }

            current = next;
        }

        long finalOffset = feature.Address.PointerOffsets.Count == 0
            ? 0
            : feature.Address.PointerOffsets[^1];
        nint resolved = current + checked((nint)finalOffset);
        byte[] raw = memory.ReadBytes(resolved - 32, 68);
        Console.WriteLine($"resolved=0x{resolved:X}; finalOffset=0x{finalOffset:X}");
        for (int offset = -32; offset <= 32; offset += 4)
        {
            int rawOffset = offset + 32;
            byte[] bytes = raw.AsSpan(rawOffset, 4).ToArray();
            Console.WriteLine(
                $"{offset,4:+#;-#;0}: {Convert.ToHexString(bytes)} "
                + $"int32={BitConverter.ToInt32(bytes)} float={BitConverter.ToSingle(bytes):R}");
        }

        return 0;
    }

    private static async Task<int> RunTrainerValueVerification(string[] args)
    {
        if (args.Length != 6
            || !Enum.TryParse(args[3], ignoreCase: true, out VerificationValueMode mode)
            || !Enum.IsDefined(mode)
            || !double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int durationMilliseconds))
        {
            Console.Error.WriteLine(
                "Usage: --verify-trainer-value <profile-id> <feature-key> <set|multiply|add> <value> <duration-ms>");
            return 2;
        }

        Console.WriteLine("DEV ONLY: 將只驗證一個功能，最長 5 秒；線上 guard 必須解析為 0，結束時會還原原始值。");
        GameInstallation installation = GetCurrentGameInstallation();
        OffsetCatalog catalog = LoadOffsetCatalog();
        ValueVerificationResult result = await TrainerVerificationRunner.VerifyValueAsync(
            catalog,
            installation.GameAssemblyPath,
            installation.UnityPlayerPath,
            installation.MetadataPath,
            args[1],
            args[2],
            mode,
            value,
            TimeSpan.FromMilliseconds(durationMilliseconds));
        Console.WriteLine($"PASS  profile={result.ProfileId}; feature={result.FeatureKey}; durationMs={result.Duration.TotalMilliseconds:R}");
        Console.WriteLine($"PASS  original={result.OriginalValue:R}; applied={result.AppliedValue:R}; restored={result.RestoredValue:R}");
        return 0;
    }

    private static async Task<int> RunTrainerPatchVerification(string[] args)
    {
        if (args.Length != 4
            || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int durationMilliseconds))
        {
            Console.Error.WriteLine("Usage: --verify-trainer-patch <profile-id> <feature-key> <duration-ms>");
            return 2;
        }

        Console.WriteLine("DEV ONLY: 將只驗證一個 patch，最長 5 秒；線上 guard 必須解析為 0，結束時會還原原始位元組。");
        GameInstallation installation = GetCurrentGameInstallation();
        OffsetCatalog catalog = LoadOffsetCatalog();
        PatchVerificationResult result = await TrainerVerificationRunner.VerifyPatchAsync(
            catalog,
            installation.GameAssemblyPath,
            installation.UnityPlayerPath,
            installation.MetadataPath,
            args[1],
            args[2],
            TimeSpan.FromMilliseconds(durationMilliseconds));
        Console.WriteLine($"PASS  profile={result.ProfileId}; feature={result.FeatureKey}; durationMs={result.Duration.TotalMilliseconds:R}");
        Console.WriteLine($"PASS  appliedBytes={result.AppliedBytesMatched}; restoredBytes={result.RestoredBytesMatched}");
        return 0;
    }

    private static async Task<int> RunTrainerHookVerification(string[] args)
    {
        if ((args.Length != 4 && args.Length != 5)
            || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int durationMilliseconds)
            || args.Length == 5
                && !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            Console.Error.WriteLine(
                "Usage: --verify-trainer-hook <profile-id> <feature-key> <duration-ms> [forced-item-id]");
            return 2;
        }

        int forcedItemId = args.Length == 5
            ? int.Parse(args[4], CultureInfo.InvariantCulture)
            : 1;
        Console.WriteLine(
            "DEV ONLY: 將只驗證一個 hook，最長 5 秒；線上 guard 必須解析為 0，結束時會還原原始指令並釋放 code cave。");
        GameInstallation installation = GetCurrentGameInstallation();
        OffsetCatalog catalog = LoadOffsetCatalog();
        HookVerificationResult result = await TrainerVerificationRunner.VerifyHookAsync(
            catalog,
            installation.GameAssemblyPath,
            installation.UnityPlayerPath,
            installation.MetadataPath,
            args[1],
            args[2],
            TimeSpan.FromMilliseconds(durationMilliseconds),
            forcedItemId);
        Console.WriteLine(
            $"PASS  profile={result.ProfileId}; feature={result.FeatureKey}; durationMs={result.Duration.TotalMilliseconds:R}");
        Console.WriteLine(
            $"PASS  installedJump={result.InstalledJumpMatched}; restoredBytes={result.RestoredBytesMatched}");
        return 0;
    }

    private static async Task<int> RunForcedItemBehaviorVerification(string[] args)
    {
        if (args.Length != 4
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int itemId)
            || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int durationMilliseconds))
        {
            Console.Error.WriteLine(
                "Usage: --verify-force-item-behavior <profile-id> <item-id> <duration-ms>");
            return 2;
        }

        Console.WriteLine(
            "DEV ONLY: 單人行為觀察期間會維持無敵與指定升級道具，最長 30 秒；線上 guard 失敗時立即還原。");
        GameInstallation installation = GetCurrentGameInstallation();
        OffsetCatalog catalog = LoadOffsetCatalog();
        ForcedItemBehaviorVerificationResult result =
            await TrainerVerificationRunner.VerifyForcedItemBehaviorAsync(
                catalog,
                installation.GameAssemblyPath,
                installation.UnityPlayerPath,
                installation.MetadataPath,
                args[1],
                itemId,
                TimeSpan.FromMilliseconds(durationMilliseconds));
        Console.WriteLine(
            $"PASS  profile={result.ProfileId}; itemId={result.ItemId}; durationMs={result.Duration.TotalMilliseconds:R}");
        Console.WriteLine(
            $"PASS  installedJump={result.InstalledJumpMatched}; restoredBytes={result.RestoredBytesMatched}");
        return 0;
    }

    private static GameInstallation GetCurrentGameInstallation()
    {
        return new GameInstallationLocator().FindInstallations().FirstOrDefault()
            ?? throw new InvalidOperationException("找不到完整遊戲安裝。");
    }

    private static OffsetCatalog LoadOffsetCatalog()
    {
        return OffsetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "offsets.json"));
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
        string address = value.Address == 0 ? string.Empty : $" address=0x{value.Address:X};";
        Console.WriteLine($"{(value.Success ? "PASS" : "WAIT")}  {value.Key}:{address} {detail}");
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
        editor.SetEggAttribute("antonio", "armor", 4);
        editor.SetEggAttribute("ANTONIO", "power", 12);
        editor.SetEggAttribute("ANTONIO", "luck", 3);
        editor.SetEggAttribute("ANTONIO", "futureVersionAttribute", 2);
        editor.ReplaceArray("UnlockedCharacters", new JsonNode?[]
        {
            JsonValue.Create("ANTONIO"),
            JsonValue.Create("IMELDA"),
            JsonValue.Create("ANTONIO")
        });

        string output = document.SerializeWithChecksum();
        True(SaveChecksum.IsValid(output), "Edited save must have a valid checksum.");
        JsonObject root = JsonNode.Parse(output)!.AsObject();
        Equal(1_000_000_000_000d, root["Coins"]!.GetValue<double>(), "Coins were not maximized.");
        True(root["AlwaysQuickTreasureAnim"]!.GetValue<bool>(), "Quick treasure flag was not enabled.");
        Equal(21d, root["EggData"]!["ANTONIO"]!["total"]!.GetValue<double>(), "Egg total was not synchronized.");
        Equal(4d, root["EggData"]!["ANTONIO"]!["armor"]!.GetValue<double>(), "Editing attack must not change defense eggs.");
        Equal(1, root["EggData"]!.AsObject().Count, "Character ID matching must be case-insensitive.");
        True(editor.TryGetEggAttribute("AnToNiO", "power", out double powerEggs) && powerEggs == 12,
            "Egg attribute lookup must be case-insensitive for character IDs.");
        True(editor.GetEggAttributeNames().Contains("futureVersionAttribute", StringComparer.Ordinal),
            "Every numeric EggData attribute from a future game version must be exposed.");
        True(!editor.GetEggAttributeNames().Contains("total", StringComparer.Ordinal),
            "Derived EggData total must not be directly editable as an independent attribute.");
        Throws<ArgumentException>(() => editor.SetEggAttribute("ANTONIO", "total", 999));
        Equal(3, root["UnlockedCharacters"]!.AsArray().Count, "Unlock array duplicates must be preserved.");

        editor.ReplaceArray("UnlockedArcanas", new JsonNode?[] { JsonValue.Create(3), JsonValue.Create(3), JsonValue.Create(7) });
        JsonArray arcanas = JsonNode.Parse(document.SerializeWithChecksum())!["UnlockedArcanas"]!.AsArray();
        Equal(3, arcanas.Count, "Numeric unlock array count differs.");
        Equal(3, arcanas[0]!.GetValue<int>(), "Numeric unlock type was not preserved.");
        return Task.CompletedTask;
    }

    private static Task TestJsonTreeEditing()
    {
        JsonObject root = JsonNode.Parse("{\"name\":\"Antonio\",\"count\":1,\"items\":[true,null]}")!.AsObject();
        JsonTreeEntry tree = JsonTreeEntry.CreateRoot(root);
        Equal(3, tree.Children.Count, "JSON tree top-level count differs.");
        JsonTreeEntry count = tree.Children.Single(entry => entry.Name == "count");
        True(count.CanEditValue, "Scalar JSON tree node must be editable.");
        count.ReplaceValue(JsonNode.Parse("2.5"));
        Equal(2.5d, root["count"]!.GetValue<double>(), "JSON tree replacement did not update the source object.");
        JsonTreeEntry nullEntry = tree.Children.Single(entry => entry.Name == "items").Children[1];
        Equal(JsonValueKind.Null, nullEntry.Kind, "JSON tree must preserve null node kind.");
        return Task.CompletedTask;
    }

    private static Task TestShippedProfileData()
    {
        OffsetCatalog offsets = OffsetCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "offsets.json"));
        UnlockCatalog unlocks = UnlockCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "ids", "unlocks.json"));
        ForcedItemCatalog forcedItems = ForcedItemCatalog.Load(
            Path.Combine(AppContext.BaseDirectory, "data", "ids", "forced-level-up-items.json"));
        True(offsets.Profiles.Count > 0, "At least one shipped game version profile is required.");
        Equal(85, forcedItems.Items.Count, "Forced level-up item catalog count differs.");
        Equal("Magic Wand", forcedItems.Items.Single(item => item.Id == 1).Name, "Forced item ID 1 differs.");
        Equal("Profusione D'Amore", forcedItems.Items.Single(item => item.Id == 155).Name, "Forced item ID 155 differs.");
        foreach (GameVersionProfile gameProfile in offsets.Profiles)
        {
            UnlockCatalogProfile unlockProfile = unlocks.FindByProfileId(gameProfile.ProfileId)
                ?? throw new InvalidOperationException($"Missing unlock catalog for {gameProfile.ProfileId}.");
            True(unlockProfile.Arrays.Count > 0, $"Unlock catalog {gameProfile.ProfileId} is empty.");
            True(!unlockProfile.Arrays.ContainsKey("UnlockedSkins")
                && !unlockProfile.Arrays.ContainsKey("UnlockedSkinsV2"),
                "Skin dictionaries must not be shipped as unlock arrays.");
            True(!unlockProfile.Arrays.ContainsKey("BoughtPowerups")
                && !unlockProfile.Arrays.ContainsKey("UnlockedPowerUpRanks"),
                "PowerUp rank arrays must not be generated from unique enum IDs.");
        }

        GameVersionProfile current = offsets.Profiles.Single(profile =>
            profile.ProfileId == "steam-current-2026-07-23");
        Equal(
            "248617379e77e795b0b2f12328e2a86968730fa9ede997d62a66db70be158e3a",
            current.GameAssemblySha256,
            "Current GameAssembly fingerprint differs from the verified dump input.");
        Equal(
            "6e82e833185a101ba30f00216fe2bed0beb78aa339fa452f1f5268839ebeb257",
            current.Il2CppMetadataSha256,
            "Current metadata fingerprint differs from the verified dump input.");
        True(!current.Verified, "The new game profile must stay fail-closed until in-run verification completes.");
        Equal(160283208L, current.OnlineSession!.BaseOffset, "Current online guard uses the old GM_TypeInfo RVA.");
        foreach ((string key, FeatureDefinition feature) in current.Features.Where(pair =>
                     pair.Value.Kind == FeatureKind.Value
                     && string.Equals(pair.Value.Address.Module, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase)))
        {
            Equal(160283208L, feature.Address.BaseOffset, $"Current feature {key} uses the old GM_TypeInfo RVA.");
        }

        FeatureDefinition treasure = current.Features["maxTreasure"];
        Equal(111042760L, treasure.Address.BaseOffset, "Current treasure primary patch RVA differs.");
        Equal("8B 4B 18", treasure.ExpectedBytes, "Current treasure primary expected bytes differ.");
        Equal(1, treasure.AdditionalPatches.Count, "Current treasure composite patch segment count differs.");
        Equal(111044288L, treasure.AdditionalPatches[0].Address.BaseOffset, "Current treasure secondary patch RVA differs.");
        Equal("F3 0F 2C 47 48", treasure.AdditionalPatches[0].ExpectedBytes,
            "Current treasure secondary expected bytes differ.");
        Equal(117434889L, current.Features["forceLevelUpItem"].Address.BaseOffset,
            "Forced level-up hook RVA differs.");
        Equal("48 89 83 30 02 00 00", current.Features["forceLevelUpItem"].ExpectedBytes,
            "Forced level-up hook expected bytes differ.");
        Equal(111363610L, current.Features["duplicateNextWeapon"].Address.BaseOffset,
            "Duplicate weapon hook RVA differs.");
        Equal(129280394L, current.Features["duplicateNextAccessory"].Address.BaseOffset,
            "Duplicate accessory hook RVA differs.");
        True(current.Features["forceLevelUpItem"].Kind == FeatureKind.Hook
            && current.Features["duplicateNextWeapon"].Kind == FeatureKind.Hook
            && current.Features["duplicateNextAccessory"].Kind == FeatureKind.Hook,
            "Current trainer hook features must use FeatureKind.Hook.");
        True(offsets.Profiles.Single(profile => profile.ProfileId == "steam-current-2026-07-22")
                .Features.Values.All(feature => feature.Kind != FeatureKind.Hook),
            "Current-build hook RVAs must not be copied into the retained old profile.");

        UnlockCatalogProfile oldUnlocks = unlocks.FindByProfileId("steam-current-2026-07-22")
            ?? throw new InvalidOperationException("Missing retained 2026-07-22 unlock profile.");
        UnlockCatalogProfile currentUnlocks = unlocks.FindByProfileId(current.ProfileId)
            ?? throw new InvalidOperationException("Missing current unlock profile.");
        True(ContainsString(oldUnlocks.Arrays["BoughtCharacters"], "TP_CHAOS"),
            "The retained old unlock profile must preserve TP_CHAOS.");
        True(!ContainsString(currentUnlocks.Arrays["BoughtCharacters"], "TP_CHAOS")
            && !ContainsString(currentUnlocks.Arrays["UnlockedCharacters"], "TP_CHAOS"),
            "The current unlock profile must reflect removal of TP_CHAOS.");

        return Task.CompletedTask;
    }

    private static bool ContainsString(JsonNode? node, string value)
    {
        return node is JsonArray array && array.Any(item =>
            item is JsonValue jsonValue
            && jsonValue.TryGetValue(out string? itemValue)
            && string.Equals(itemValue, value, StringComparison.Ordinal));
    }

    private static Task TestUnlockCatalogMerge()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VSModifierTests_{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "unlocks.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """
                {
                  "schemaVersion": 1,
                  "profiles": [
                    {
                      "profileId": "test-current",
                      "label": "test",
                      "arrays": {
                        "UnlockedCharacters": ["ANTONIO", "IMELDA"],
                        "BoughtPowerups": ["MIGHT", "COOLDOWN"],
                        "UnlockedArcanas": [1, 2]
                      }
                    }
                  ]
                }
                """, Utf8WithoutBom);

            UnlockCatalog catalog = UnlockCatalog.Load(path);
            UnlockCatalogProfile profile = catalog.FindByProfileId("test-current")
                ?? throw new InvalidOperationException("Expected unlock profile was not found.");
            True(catalog.FindByProfileId("unknown") is null, "Unknown unlock profile must not match.");

            SaveDocument document = SaveDocument.Parse(CreateValidSave());
            document.Root["UnlockedCharacters"] = JsonNode.Parse("[\"ANTONIO\",\"ANTONIO\"]");
            document.Root["BoughtPowerups"] = JsonNode.Parse("[\"MIGHT\",\"MIGHT\",\"MIGHT\"]");
            document.Root["UnlockedArcanas"] = JsonNode.Parse("[1,1]");
            document.Root["UnlockedSkinsV2"] = new JsonObject();
            SaveEditor editor = new(document);
            editor.MergeUnlockCatalog(profile.Arrays);

            JsonArray characters = document.Root["UnlockedCharacters"]!.AsArray();
            Equal(3, characters.Count, "Safe merge must preserve duplicates and append a missing character once.");
            Equal("IMELDA", characters[2]!.GetValue<string>(), "Missing character was not appended.");
            JsonArray powerups = document.Root["BoughtPowerups"]!.AsArray();
            Equal(4, powerups.Count, "PowerUp ranks must be preserved while appending a missing ID once.");
            Equal(3, powerups.Count(item => item?.GetValue<string>() == "MIGHT"), "Existing duplicate PowerUp ranks were changed.");
            JsonArray arcanas = document.Root["UnlockedArcanas"]!.AsArray();
            Equal(3, arcanas.Count, "Numeric unlock IDs must merge without removing duplicates.");

            JsonObject invalid = new()
            {
                ["UnlockedCharacters"] = new JsonArray("PUGNALA"),
                ["UnlockedSkinsV2"] = new JsonArray("SKIN_TEST")
            };
            Throws<InvalidDataException>(() => editor.MergeUnlockCatalog(invalid));
            Equal(3, document.Root["UnlockedCharacters"]!.AsArray().Count,
                "A structural mismatch must reject the whole merge atomically.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

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

    private static Task TestMemoryPatchSet()
    {
        FakeMemory memory = new();
        memory.WriteBytes((nint)0x1010, [0x74, 0x05]);
        memory.WriteBytes((nint)0x1020, [0x75, 0x06]);
        using MemoryPatch first = new(memory, (nint)0x1010, new byte[] { 0x90, 0x90 }, new byte[] { 0x74, 0x05 });
        using MemoryPatch invalidSecond = new(memory, (nint)0x1020, new byte[] { 0x90, 0x90 }, new byte[] { 0x74, 0x06 });
        using (MemoryPatchSet invalidSet = new([first, invalidSecond]))
        {
            Throws<InvalidDataException>(invalidSet.Enable);
        }

        True(memory.ReadBytes((nint)0x1010, 2).SequenceEqual(new byte[] { 0x74, 0x05 }),
            "Composite patch must roll back earlier segments when a later segment fails.");

        using MemoryPatch validFirst = new(memory, (nint)0x1010, new byte[] { 0x90, 0x90 }, new byte[] { 0x74, 0x05 });
        using MemoryPatch validSecond = new(memory, (nint)0x1020, new byte[] { 0x90, 0x90 }, new byte[] { 0x75, 0x06 });
        using (MemoryPatchSet validSet = new([validFirst, validSecond]))
        {
            validSet.Enable();
            True(memory.ReadBytes((nint)0x1020, 2).SequenceEqual(new byte[] { 0x90, 0x90 }),
                "Composite patch did not enable all segments.");
        }

        True(memory.ReadBytes((nint)0x1020, 2).SequenceEqual(new byte[] { 0x75, 0x06 }),
            "Composite patch did not restore all segments.");

        using MemoryPatch retryFirst = new(memory, (nint)0x1010, new byte[] { 0x90, 0x90 }, new byte[] { 0x74, 0x05 });
        using MemoryPatch retrySecond = new(memory, (nint)0x1020, new byte[] { 0x90, 0x90 }, new byte[] { 0x75, 0x06 });
        using MemoryPatchSet retrySet = new([retryFirst, retrySecond]);
        retrySet.Enable();
        memory.FailNextCodeWriteAt = (nint)0x1020;
        Throws<InvalidOperationException>(retrySet.Disable);
        True(memory.ReadBytes((nint)0x1020, 2).SequenceEqual(new byte[] { 0x90, 0x90 }),
            "A failed patch restoration must leave the failed segment enabled for retry.");
        retrySet.Disable();
        True(memory.ReadBytes((nint)0x1020, 2).SequenceEqual(new byte[] { 0x75, 0x06 }),
            "A second patch restoration attempt must restore the failed segment.");
        return Task.CompletedTask;
    }

    private static Task TestTrainerHookPayloads()
    {
        nint cave = (nint)0x1200;
        nint forceTarget = (nint)0x1010;
        byte[] forceOriginal = [0x48, 0x89, 0x83, 0x30, 0x02, 0x00, 0x00];
        RemoteHookPayload force = TrainerHookPayloadFactory.CreateForcedLevelUpItem(
            cave,
            forceTarget,
            forceOriginal);
        Equal(0, BitConverter.ToInt32(force.Code, force.DataOffsets[TrainerHookPayloadFactory.ItemId]),
            "Forced-item payload must start with item ID zero.");
        Equal((byte)0, force.Code[force.DataOffsets[TrainerHookPayloadFactory.EnabledFlag]],
            "Forced-item payload must start disabled.");
        int originalOffset = force.Code.AsSpan().IndexOf(forceOriginal);
        True(originalOffset >= 0, "Forced-item payload does not replay the overwritten instructions.");
        int returnJump = originalOffset + forceOriginal.Length;
        Equal((byte)0xE9, force.Code[returnJump], "Forced-item payload is missing its return jump.");
        Equal(
            forceTarget + forceOriginal.Length,
            DecodeRelativeDestination(force.Code, cave, returnJump),
            "Forced-item payload returns to the wrong instruction.");

        nint duplicateTarget = (nint)0x1050;
        byte[] duplicateOriginal = [0x45, 0x33, 0xC9, 0x45, 0x33, 0xC0, 0x8B, 0xD6];
        RemoteHookPayload duplicate = TrainerHookPayloadFactory.CreateDuplicateNext(
            cave,
            duplicateTarget,
            duplicateOriginal);
        True(duplicate.Code.AsSpan(0, duplicateOriginal.Length).SequenceEqual(duplicateOriginal),
            "Duplicate payload must replay the overwritten setup instructions first.");
        int xorOffset = duplicate.Code.AsSpan().IndexOf(new byte[] { 0x48, 0x31, 0xC0, 0xE9 });
        True(xorOffset >= 0, "Duplicate payload is missing its one-shot skip branch.");
        int skipJump = xorOffset + 3;
        Equal(
            duplicateTarget + duplicateOriginal.Length + 5,
            DecodeRelativeDestination(duplicate.Code, cave, skipJump),
            "Duplicate one-shot branch must skip exactly the following five-byte guard call.");
        int normalJump = skipJump + 5;
        Equal((byte)0xE9, duplicate.Code[normalJump], "Duplicate payload is missing its normal branch.");
        Equal(
            duplicateTarget + duplicateOriginal.Length,
            DecodeRelativeDestination(duplicate.Code, cave, normalJump),
            "Duplicate normal branch returns to the wrong instruction.");
        return Task.CompletedTask;
    }

    private static Task TestRemoteCodeHook()
    {
        FakeMemory memory = new();
        nint target = (nint)0x1010;
        byte[] original = [0x48, 0x89, 0x83, 0x30, 0x02, 0x00, 0x00];
        memory.WriteBytes(target, original);
        using (RemoteCodeHook hook = RemoteCodeHook.Install(
                   memory,
                   target,
                   original,
                   TrainerHookPayloadFactory.CreateForcedLevelUpItem))
        {
            Equal((byte)0xE9, memory.ReadBytes(target, original.Length)[0],
                "Installed hook did not replace the target with a rel32 jump.");
            hook.WriteData(TrainerHookPayloadFactory.ItemId, BitConverter.GetBytes(155));
            hook.WriteData(TrainerHookPayloadFactory.EnabledFlag, [1]);
            Equal(155, BitConverter.ToInt32(hook.ReadData(TrainerHookPayloadFactory.ItemId, sizeof(int))),
                "Hook item ID data did not round-trip.");
            Equal((byte)1, hook.ReadData(TrainerHookPayloadFactory.EnabledFlag, 1)[0],
                "Hook enabled flag did not round-trip.");
        }

        True(memory.ReadBytes(target, original.Length).SequenceEqual(original),
            "Remote hook did not restore the original target bytes.");
        Equal(1, memory.FreeExecutableCount, "Remote hook code cave was not released exactly once.");
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
                  "schemaVersion": 2,
                  "profiles": [
                    {
                      "profileId": "test-current",
                      "gameAssemblySha256": "{{hash}}",
                      "unityPlayerSha256": "{{new string('b', 64)}}",
                      "il2CppMetadataSha256": "{{new string('c', 64)}}",
                      "label": "test",
                      "verified": false,
                      "features": {}
                    }
                  ]
                }
                """, Utf8WithoutBom);
            OffsetCatalog catalog = OffsetCatalog.Load(path);
            Equal(1, catalog.Profiles.Count, "Profile count differs.");
            ProfileMatchResult exact = catalog.Match(new GameVersionFingerprint(
                hash.ToUpperInvariant(),
                new string('B', 64),
                new string('C', 64)));
            True(exact.Profile is not null, "Three-part fingerprint lookup must be case-insensitive.");

            ProfileMatchResult assemblyMismatch = catalog.Match(new GameVersionFingerprint(
                new string('d', 64),
                new string('b', 64),
                new string('c', 64)));
            True(assemblyMismatch.Profile is null && assemblyMismatch.Error!.Contains("GameAssembly", StringComparison.Ordinal),
                "GameAssembly mismatch must fail with a specific reason.");

            ProfileMatchResult unityMismatch = catalog.Match(new GameVersionFingerprint(
                hash,
                new string('d', 64),
                new string('c', 64)));
            True(unityMismatch.Profile is null && unityMismatch.Error!.Contains("UnityPlayer", StringComparison.Ordinal),
                "UnityPlayer mismatch must fail with a specific reason.");

            ProfileMatchResult metadataMismatch = catalog.Match(new GameVersionFingerprint(
                hash,
                new string('b', 64),
                new string('d', 64)));
            True(metadataMismatch.Profile is null && metadataMismatch.Error!.Contains("global-metadata", StringComparison.Ordinal),
                "Metadata mismatch must fail with a specific reason.");

            File.WriteAllText(path, $$"""
                {
                  "schemaVersion": 2,
                  "profiles": [
                    {
                      "profileId": "invalid-patch",
                      "gameAssemblySha256": "{{hash}}",
                      "unityPlayerSha256": "{{new string('b', 64)}}",
                      "il2CppMetadataSha256": "{{new string('c', 64)}}",
                      "label": "invalid",
                      "verified": false,
                      "features": {
                        "bad": {
                          "kind": "patch",
                          "address": { "module": "GameAssembly.dll", "baseOffset": 16 },
                          "expectedBytes": "90 90",
                          "patchBytes": "90"
                        }
                      }
                    }
                  ]
                }
                """, Utf8WithoutBom);
            Throws<InvalidDataException>(() => OffsetCatalog.Load(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TestOffsetCatalogLiveReload()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VSModifierTests_{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "offsets.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """{"schemaVersion":2,"profiles":[]}""");
            OffsetCatalogFile catalogFile = new(path);
            True(catalogFile.HasChanged(), "A catalog that has never been loaded should require reload.");

            OffsetCatalog first = catalogFile.Reload();
            Equal(0, first.Profiles.Count, "Initial catalog profile count differs.");
            True(!catalogFile.HasChanged(), "Catalog should be unchanged immediately after reload.");

            File.WriteAllText(path, """{"schemaVersion":2,"profiles":[],"revision":"future"}""");
            True(catalogFile.HasChanged(), "Changed catalog content was not detected.");

            OffsetCatalog second = catalogFile.Reload();
            Equal(0, second.Profiles.Count, "Reloaded catalog profile count differs.");
            True(!catalogFile.HasChanged(), "Reloaded catalog should become the observed version.");

            File.WriteAllText(path, """{"schemaVersion":999,"profiles":[]}""");
            True(catalogFile.HasChanged(), "Invalid replacement catalog was not detected.");
            Throws<InvalidDataException>(() => catalogFile.Reload());
            True(!catalogFile.HasChanged(), "Rejected catalog should not be retried every timer tick until the file changes again.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TestTrainerVerificationPolicy()
    {
        AddressDefinition online = new()
        {
            Module = "GameAssembly.dll",
            BaseOffset = 16
        };
        FeatureDefinition valueFeature = new()
        {
            Kind = FeatureKind.Value,
            ValueType = MemoryValueType.Float,
            Address = new AddressDefinition
            {
                Module = "GameAssembly.dll",
                BaseOffset = 32
            }
        };
        GameVersionProfile unverified = new()
        {
            ProfileId = "test-profile",
            GameAssemblySha256 = new string('a', 64),
            UnityPlayerSha256 = new string('b', 64),
            Il2CppMetadataSha256 = new string('c', 64),
            Verified = false,
            OnlineSession = online,
            Features = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal)
            {
                ["test.value"] = valueFeature
            }
        };

        Throws<InvalidOperationException>(() => TrainerProfilePolicy.RequireReleaseReady(unverified));
        Equal(
            valueFeature,
            TrainerProfilePolicy.RequireDevelopmentVerification(
                unverified,
                "test-profile",
                "test.value",
                FeatureKind.Value,
                TimeSpan.FromSeconds(1)),
            "Development verification must return the exact selected feature.");
        Throws<InvalidOperationException>(() => TrainerProfilePolicy.RequireDevelopmentVerification(
            unverified,
            "wrong-profile",
            "test.value",
            FeatureKind.Value,
            TimeSpan.FromSeconds(1)));
        Throws<InvalidOperationException>(() => TrainerProfilePolicy.RequireDevelopmentVerification(
            unverified,
            "test-profile",
            "test.value",
            FeatureKind.Patch,
            TimeSpan.FromSeconds(1)));
        Throws<ArgumentOutOfRangeException>(() => TrainerProfilePolicy.RequireDevelopmentVerification(
            unverified,
            "test-profile",
            "test.value",
            FeatureKind.Value,
            TimeSpan.FromSeconds(6)));

        GameVersionProfile verified = new()
        {
            ProfileId = unverified.ProfileId,
            GameAssemblySha256 = unverified.GameAssemblySha256,
            UnityPlayerSha256 = unverified.UnityPlayerSha256,
            Il2CppMetadataSha256 = unverified.Il2CppMetadataSha256,
            Verified = true,
            OnlineSession = online,
            Features = unverified.Features
        };
        TrainerProfilePolicy.RequireReleaseReady(verified);
        return Task.CompletedTask;
    }

    private static async Task TestTrainerValueSafety()
    {
        FakeMemory memory = new();
        AddressDefinition online = new() { Module = "GameAssembly.dll", BaseOffset = 16 };
        AddressDefinition valueAddress = new() { Module = "GameAssembly.dll", BaseOffset = 32 };
        GameVersionProfile profile = new()
        {
            ProfileId = "value-safety",
            GameAssemblySha256 = new string('a', 64),
            UnityPlayerSha256 = new string('b', 64),
            Il2CppMetadataSha256 = new string('c', 64),
            Verified = false,
            OnlineSession = online,
            Features = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal)
            {
                ["test.value"] = new FeatureDefinition
                {
                    Kind = FeatureKind.Value,
                    ValueType = MemoryValueType.Float,
                    MinValue = 0,
                    MaxValue = 10,
                    Address = valueAddress
                }
            }
        };

        nint currentValueAddress = (nint)0x1010;
        memory.WriteBytes((nint)0x1000, [0]);
        memory.WriteBytes((nint)0x1010, BitConverter.GetBytes(2f));
        memory.WriteBytes((nint)0x1020, BitConverter.GetBytes(9f));
        await using TrainerSession session = new(
            memory,
            profile,
            definition => definition.BaseOffset == online.BaseOffset
                ? (nint)0x1000
                : currentValueAddress);

        Equal(4d, session.WriteValue("test.value", 4), "One-shot value write did not read back.");
        Throws<ArgumentOutOfRangeException>(() => session.WriteValue("test.value", 11));
        session.EnableValueLock("test.value", 5);
        await Task.Delay(140);
        currentValueAddress = (nint)0x1020;
        session.DisableValueLock("test.value");
        Equal(4f, BitConverter.ToSingle(memory.ReadBytes((nint)0x1010, sizeof(float))),
            "Value lock did not restore the original bytes at the exact captured address.");
        Equal(9f, BitConverter.ToSingle(memory.ReadBytes((nint)0x1020, sizeof(float))),
            "Value restoration must not follow a moved pointer chain into a new object.");
    }

    private static async Task TestValueLockService()
    {
        int counter = 0;
        await using (ValueLockService service = new(TimeSpan.FromMilliseconds(15)))
        {
            service.Set("counter", () => Interlocked.Increment(ref counter));
            await Task.Delay(80);
            service.Remove("counter");
            True(Volatile.Read(ref counter) >= 2, "Value lock did not enforce repeatedly.");
        }

        int guardedCounter = 0;
        int guardChecks = 0;
        TaskCompletionSource<ValueLockFailureEventArgs> failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (ValueLockService failClosed = new(TimeSpan.FromMilliseconds(15), stopAllOnFailure: true))
        {
            failClosed.LockFailed += (_, args) => failure.TrySetResult(args);
            failClosed.Set("counter", () => Interlocked.Increment(ref guardedCounter));
            failClosed.Set("onlineGuard", () =>
            {
                if (Interlocked.Increment(ref guardChecks) >= 2)
                {
                    throw new OnlineSessionException("online");
                }
            });
            ValueLockFailureEventArgs result = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Equal("onlineGuard", result.Key, "The failing safety guard key was not reported.");
            True(failClosed.ActiveLocks.Count == 0, "Fail-closed mode must remove every active lock.");
            await Task.Delay(40);
            int stoppedValue = Volatile.Read(ref guardedCounter);
            await Task.Delay(50);
            Equal(stoppedValue, Volatile.Read(ref guardedCounter), "No lock may continue after a fail-closed safety event.");
        }
    }

    private static async Task TestValueLockSharedGuard()
    {
        int guardRuns = 0;
        int enforcements = 0;
        int guardRunsAtFirstEnforcement = -1;
        await using (ValueLockService service = new(
            TimeSpan.FromMilliseconds(15),
            stopAllOnFailure: true,
            guard: () => Interlocked.Increment(ref guardRuns)))
        {
            for (int index = 0; index < 4; index++)
            {
                service.Set($"lock{index}", () =>
                {
                    if (Interlocked.Increment(ref enforcements) == 1)
                    {
                        guardRunsAtFirstEnforcement = Volatile.Read(ref guardRuns);
                    }
                });
            }

            True(guardRunsAtFirstEnforcement >= 1, "The shared guard must run before the first enforced write.");
            int baselineGuardRuns = Volatile.Read(ref guardRuns);
            int baselineEnforcements = Volatile.Read(ref enforcements);
            await Task.Delay(90);
            int guardDelta = Volatile.Read(ref guardRuns) - baselineGuardRuns;
            int enforceDelta = Volatile.Read(ref enforcements) - baselineEnforcements;
            True(guardDelta >= 2, "The shared guard must keep running on every period.");
            True(
                enforceDelta >= guardDelta * 2,
                "Four locks must be enforced per guard run; the guard cost must not scale with the lock count.");
        }

        int blockedEnforcements = 0;
        int guardChecks = 0;
        TaskCompletionSource<ValueLockFailureEventArgs> failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (ValueLockService failClosed = new(
            TimeSpan.FromMilliseconds(15),
            stopAllOnFailure: true,
            guard: () =>
            {
                if (Interlocked.Increment(ref guardChecks) >= 2)
                {
                    throw new OnlineSessionException("online");
                }
            }))
        {
            failClosed.LockFailed += (_, args) => failure.TrySetResult(args);
            failClosed.Set("value", () => Interlocked.Increment(ref blockedEnforcements));
            ValueLockFailureEventArgs result = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Equal(ValueLockService.DefaultGuardKey, result.Key, "A guard failure must be reported under the guard key.");
            True(failClosed.ActiveLocks.Count == 0, "A failed guard must clear every lock.");
            int stopped = Volatile.Read(ref blockedEnforcements);
            await Task.Delay(60);
            Equal(stopped, Volatile.Read(ref blockedEnforcements), "No lock may run after the shared guard failed.");
            Throws<InvalidOperationException>(() => failClosed.Set("late", () => { }));
        }
    }

    /// <summary>
    /// 以本測試行程自身當目標，實際走一次 ReadProcessMemory／WriteProcessMemory，
    /// 涵蓋 span 版讀取與模組快取；不需要遊戲執行，也不會碰到遊戲記憶體。
    /// </summary>
    private static Task TestProcessMemoryRoundTrip()
    {
        byte[] payload = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];
        GCHandle pinned = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            nint address = pinned.AddrOfPinnedObject();
            using Process current = Process.GetCurrentProcess();
            using ProcessMemorySession session = ProcessMemorySession.Attach(current.ProcessName);
            Equal(current.Id, session.ProcessId, "The self-attach test must target this very process.");

            True(
                session.ReadBytes(address, payload.Length).AsSpan().SequenceEqual(payload),
                "Array read returned different bytes.");

            Span<byte> destination = stackalloc byte[payload.Length];
            session.ReadBytes(address, destination);
            True(destination.SequenceEqual(payload), "Span read returned different bytes.");

            Equal(0x44332211, session.Read<int>(address), "Typed read decoded the wrong value.");

            byte[] replacement = [0xDE, 0xAD, 0xBE, 0xEF, 0x0A, 0x0B, 0x0C, 0x0D];
            session.WriteBytes(address, replacement);
            True(payload.AsSpan().SequenceEqual(replacement), "Native write did not reach the pinned buffer.");
            True(
                session.ReadBytes(address, replacement.Length).AsSpan().SequenceEqual(replacement),
                "Written bytes did not read back.");

            ProcessModuleInfo first = session.GetModuleInfo(current.MainModule!.ModuleName);
            ProcessModuleInfo second = session.GetModuleInfo(current.MainModule!.ModuleName);
            Equal(first, second, "The module cache must return the same resolved module info.");
            True(first.BaseAddress != 0, "The resolved module base address must not be null.");
        }
        finally
        {
            pinned.Free();
        }

        return Task.CompletedTask;
    }

    private static Task TestTrainerRejectsOnlineSession()
    {
        FakeMemory memory = new();
        AddressDefinition online = new() { Module = "GameAssembly.dll", BaseOffset = 16 };
        GameVersionProfile profile = new()
        {
            ProfileId = "online-guard",
            GameAssemblySha256 = new string('a', 64),
            UnityPlayerSha256 = new string('b', 64),
            Il2CppMetadataSha256 = new string('c', 64),
            Verified = false,
            OnlineSession = online,
            Features = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal)
        };

        memory.WriteBytes((nint)0x1000, [1]);
        Throws<OnlineSessionException>(() => new TrainerSession(memory, profile, _ => (nint)0x1000));

        memory.WriteBytes((nint)0x1000, [0]);
        TrainerSession offlineSession = new(memory, profile, _ => (nint)0x1000);
        True(offlineSession.IsAttached, "An offline session must attach.");
        return offlineSession.DisposeAsync().AsTask();
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

    private static nint DecodeRelativeDestination(byte[] code, nint baseAddress, int jumpOffset)
    {
        int displacement = BitConverter.ToInt32(code, jumpOffset + 1);
        return baseAddress + jumpOffset + 5 + displacement;
    }

    private sealed class FakeMemory : IRemoteCodeMemory
    {
        private const long BaseAddress = 0x1000;
        private readonly byte[] _bytes = new byte[0x400];

        public int CodeWriteCount { get; private set; }

        public nint? FailNextCodeWriteAt { get; set; }

        public int FreeExecutableCount { get; private set; }

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
            if (FailNextCodeWriteAt == address)
            {
                FailNextCodeWriteAt = null;
                throw new IOException("Simulated protected write failure.");
            }

            WriteBytes(address, bytes);
        }

        public nint AllocateExecutableNear(nint targetAddress, int length)
        {
            True(length <= 0x200, "Fake remote allocation is too small for the requested payload.");
            return (nint)0x1200;
        }

        public void FreeExecutable(nint address)
        {
            Equal((nint)0x1200, address, "Unexpected fake remote allocation address.");
            FreeExecutableCount++;
        }
    }
}
