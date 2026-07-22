using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;

namespace VSModifier.Core.Game;

public sealed record GameInstallation(
    string RootPath,
    string ExecutablePath,
    string GameAssemblyPath,
    string MetadataPath);

public sealed partial class GameInstallationLocator
{
    [GeneratedRegex("\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    public IReadOnlyList<GameInstallation> FindInstallations()
    {
        HashSet<string> steamRoots = new(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            AddRegistryRoot(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistryRoot(steamRoots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            AddRegistryRoot(steamRoots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        }

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            steamRoots.Add(Path.Combine(programFilesX86, "Steam"));
        }

        HashSet<string> libraryRoots = new(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (string steamRoot in steamRoots.Where(Directory.Exists))
        {
            string libraryVdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryVdf))
            {
                continue;
            }

            try
            {
                string vdf = File.ReadAllText(libraryVdf);
                foreach (Match match in LibraryPathRegex().Matches(vdf))
                {
                    string path = match.Groups["path"].Value.Replace("\\\\", "\\");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        libraryRoots.Add(path);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Discovery remains best-effort across the other Steam libraries.
            }
        }

        List<GameInstallation> installations = [];
        foreach (string libraryRoot in libraryRoots)
        {
            string root = Path.Combine(libraryRoot, "steamapps", "common", "Vampire Survivors");
            string executable = Path.Combine(root, "VampireSurvivors.exe");
            string gameAssembly = Path.Combine(root, "GameAssembly.dll");
            string metadata = Path.Combine(root, "VampireSurvivors_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
            if (File.Exists(executable) && File.Exists(gameAssembly) && File.Exists(metadata))
            {
                installations.Add(new GameInstallation(root, executable, gameAssembly, metadata));
            }
        }

        return installations
            .DistinctBy(installation => installation.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryRoot(ISet<string> roots, RegistryKey hive, string subKey, string valueName)
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
        }
    }
}
