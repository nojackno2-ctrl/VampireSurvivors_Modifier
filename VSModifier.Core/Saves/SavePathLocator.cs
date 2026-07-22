using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security;

namespace VSModifier.Core.Saves;

public sealed record SaveCandidate(string Path, DateTime LastWriteTimeUtc);

public sealed class SavePathLocator
{
    public const string AppId = "1794680";

    public IReadOnlyList<SaveCandidate> FindCandidates()
    {
        HashSet<string> steamRoots = new(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            AddRegistrySteamRoot(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistrySteamRoot(steamRoots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            AddRegistrySteamRoot(steamRoots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            steamRoots.Add(Path.Combine(programFilesX86, "Steam"));
        }

        List<SaveCandidate> candidates = [];
        foreach (string steamRoot in steamRoots.Where(Directory.Exists))
        {
            string userdata = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdata))
            {
                continue;
            }

            foreach (string accountDirectory in Directory.EnumerateDirectories(userdata))
            {
                if (!ulong.TryParse(Path.GetFileName(accountDirectory), out _))
                {
                    continue;
                }

                string candidate = Path.Combine(accountDirectory, AppId, "remote", "SaveData");
                if (File.Exists(candidate))
                {
                    candidates.Add(new SaveCandidate(candidate, File.GetLastWriteTimeUtc(candidate)));
                }
            }
        }

        return candidates
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
            .ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistrySteamRoot(
        ISet<string> roots,
        RegistryKey hive,
        string subKey,
        string valueName)
    {
        try
        {
            using RegistryKey? key = hive.OpenSubKey(subKey);
            if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
            {
                roots.Add(value.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            // Registry discovery is best-effort; the standard Program Files fallback remains available.
        }
    }
}
