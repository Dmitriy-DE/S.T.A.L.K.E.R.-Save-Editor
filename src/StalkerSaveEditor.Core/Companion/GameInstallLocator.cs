using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace StalkerSaveEditor.Core.Companion;

/// <summary>Where an installation was found: Steam, GOG (Galaxy or Heroic), the retail GSC installer, or a folder the user chose.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameInstallSource>))]
public enum GameInstallSource
{
    Steam,
    Gog,
    Retail,
    Heroic,
    Selected,
}

/// <summary>One installed copy of an X-Ray game.</summary>
public sealed record GameInstallation(CompanionGame Game, string Directory, GameInstallSource Source, string? ModName);

/// <summary>
/// Finds installed X-Ray games beyond Steam: GOG Galaxy and the retail GSC installer on Windows
/// (registry), Heroic's GOG library on Linux, and the usual GOG folders. A directory only counts when it
/// has the game's fsgame.ltx; the source tells which game it is.
/// </summary>
public static class GameInstallLocator
{
    private static readonly Dictionary<CompanionGame, string[]> TitleMarkers = new Dictionary<CompanionGame, string[]>
    {
        [CompanionGame.ShadowOfChernobyl] = ["shadow of chernobyl", "shadow of chornobyl", "shoc", "тень чернобыля"],
        [CompanionGame.ClearSky] = ["clear sky", "чистое небо"],
        [CompanionGame.CallOfPripyat] = ["call of pripyat", "call of prypiat", "зов припяти"],
    };

    private static readonly Dictionary<CompanionGame, string> RetailRegistryKeys = new Dictionary<CompanionGame, string>
    {
        [CompanionGame.ShadowOfChernobyl] = @"SOFTWARE\WOW6432Node\GSC Game World\STALKER-SHOC",
        [CompanionGame.ClearSky] = @"SOFTWARE\WOW6432Node\GSC Game World\STALKER-STCS",
        [CompanionGame.CallOfPripyat] = @"SOFTWARE\WOW6432Node\GSC Game World\STALKER-COP",
    };

    /// <summary>All non-Steam installations of the game that were found (Steam is handled by the installer).</summary>
    public static IReadOnlyList<GameInstallation> FindNonSteam(CompanionGame game, string? home = null)
    {
        var definition = CompanionGameDefinition.For(game);
        var found = new List<GameInstallation>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void Add(string? directory, GameInstallSource source)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            string full;
            try
            {
                full = Path.GetFullPath(directory);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return;
            }

            if (!IsGameRoot(full, definition) || !seen.Add(full)) return;
            found.Add(new GameInstallation(game, full, source, DetectMod(full)));
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var directory in RetailDirectories(game)) Add(directory, GameInstallSource.Retail);
            foreach (var directory in GogGalaxyDirectories(game)) Add(directory, GameInstallSource.Gog);
            foreach (var root in new[] { @"C:\GOG Games", @"C:\Program Files (x86)\GOG Galaxy\Games", @"C:\Program Files\GOG Galaxy\Games" })
            {
                foreach (var directory in ChildDirectoriesMatching(root, game)) Add(directory, GameInstallSource.Gog);
            }
        }
        else
        {
            home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var directory in HeroicDirectories(game, home)) Add(directory, GameInstallSource.Heroic);
            foreach (var root in new[] { Path.Combine(home, "Games"), Path.Combine(home, "Games", "Heroic"), Path.Combine(home, "GOG Games") })
            {
                foreach (var directory in ChildDirectoriesMatching(root, game)) Add(directory, GameInstallSource.Gog);
            }
        }

        return found.AsReadOnly();
    }

    /// <summary>Title match used for folder names and store records.</summary>
    public static bool TitleMatches(string title, CompanionGame game)
    {
        var lower = title.ToLowerInvariant();
        // "Enhanced Edition" installs are separate games with their own mod channel (Workshop).
        if (lower.Contains("enhanced", StringComparison.Ordinal) || lower.EndsWith(" - ee", StringComparison.Ordinal)) return false;
        return TitleMarkers[game].Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>Directory names of Heroic's installed GOG games (<c>~/.config/heroic/gog_store/installed.json</c>).</summary>
    internal static IEnumerable<string> HeroicDirectories(CompanionGame game, string home)
    {
        var path = Path.Combine(home, ".config", "heroic", "gog_store", "installed.json");
        if (!File.Exists(path)) yield break;
        List<string> result = [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var entries = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("installed", out var installed)
                ? installed
                : document.RootElement;
            if (entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object &&
                        entry.TryGetProperty("install_path", out var install) && install.ValueKind == JsonValueKind.String &&
                        install.GetString() is { Length: > 0 } directory &&
                        TitleMatches(Path.GetFileName(directory.TrimEnd('/', '\\')), game))
                    {
                        result.Add(directory);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var directory in result) yield return directory;
    }

    private static string[] ChildDirectoriesMatching(string root, CompanionGame game)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return Directory.EnumerateDirectories(root).Where(directory => TitleMatches(Path.GetFileName(directory), game)).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RetailDirectories(CompanionGame game)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            string? value = null;
            try
            {
                using var key = hive.OpenSubKey(RetailRegistryKeys[game]);
                value = key?.GetValue("InstallPath") as string ?? key?.GetValue("Install Path") as string;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }

            if (value is not null) yield return value;
        }
    }

    [SupportedOSPlatform("windows")]
    private static List<string> GogGalaxyDirectories(CompanionGame game)
    {
        var results = new List<string>();
        try
        {
            using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            foreach (var id in games?.GetSubKeyNames() ?? [])
            {
                using var entry = games!.OpenSubKey(id);
                if (entry?.GetValue("gameName") is string name && TitleMatches(name, game) && entry.GetValue("path") is string path)
                {
                    results.Add(path);
                }
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return results;
    }

    private static bool IsGameRoot(string path, CompanionGameDefinition definition) =>
        Directory.Exists(path) && definition.FsgameFileNames.Any(file => File.Exists(Path.Combine(path, file)));

    private static string? DetectMod(string directory)
    {
        var gamedata = Path.Combine(directory, "gamedata");
        if (!Directory.Exists(gamedata)) return null;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(gamedata))
            {
                var name = Path.GetFileName(entry).ToLowerInvariant();
                foreach (var marker in new[] { "ogsm", "srp", "anomaly", "misery", "gunslinger", "amk" })
                {
                    if (name.Contains(marker, StringComparison.Ordinal)) return marker.ToUpperInvariant();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }
}
