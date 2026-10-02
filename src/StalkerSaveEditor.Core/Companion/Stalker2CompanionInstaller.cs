using System.Text.Json;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Companion;

public sealed record Stalker2CompanionStatus(
    bool GameFound,
    string? GameDirectory,
    bool LoaderFound,
    bool ModInstalled,
    string? ModBuild,
    string? Issue);

/// <summary>
/// EXPERIMENTAL S.T.A.L.K.E.R. 2 companion: a UE4SS Lua mod (mods/companion/s2). The game needs UE4SS
/// installed by the player; this only copies the mod into UE4SS's Mods folder and removes it again.
/// It never writes anywhere else and refuses to replace a SaveEditorCompanion folder it did not create.
/// </summary>
public sealed class Stalker2CompanionInstaller(string modSourceRoot)
{
    public const int SteamAppId = 1643320;
    public const string ModFolderName = "SaveEditorCompanion";
    private const string MarkerFileName = "save_editor_install.json";

    private string SourceDirectory => Path.Combine(modSourceRoot, "s2", ModFolderName);

    /// <summary>
    /// Folder the S2 mod polls for commands (main.lua: %LOCALAPPDATA%\Stalker2\Saved). On Windows the user's own
    /// LocalAppData; under Proton the same path inside the game's prefix next to the library that holds the game.
    /// </summary>
    public static string ProtocolDirectory(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stalker2", "Saved");
        }

        var common = Directory.GetParent(Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar))
            ?? throw new DirectoryNotFoundException("The S2 game folder has no parent.");
        var steamapps = common.Parent ?? throw new DirectoryNotFoundException("The S2 game folder is not inside steamapps/common.");
        return Path.Combine(steamapps.FullName, "compatdata", SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "pfx", "drive_c", "users", "steamuser", "AppData", "Local", "Stalker2", "Saved");
    }

    public static Stalker2CompanionStatus GetStatus(string? gameDirectory = null, IReadOnlyList<string>? steamRoots = null)
    {
        var game = gameDirectory ?? FindGame(steamRoots);
        if (game is null || !Directory.Exists(game))
        {
            return new Stalker2CompanionStatus(false, null, false, false, null, "S.T.A.L.K.E.R. 2 not found in Steam libraries");
        }

        var mods = ModsDirectory(game);
        if (mods is null)
        {
            return new Stalker2CompanionStatus(true, game, false, false, null, "UE4SS is not installed in Stalker2/Binaries/Win64");
        }

        var target = Path.Combine(mods, ModFolderName);
        var marker = Path.Combine(target, MarkerFileName);
        if (!Directory.Exists(target)) return new Stalker2CompanionStatus(true, game, true, false, null, null);
        if (!File.Exists(marker))
        {
            return new Stalker2CompanionStatus(true, game, true, false, null, $"{target} exists but was not installed by the editor");
        }

        return new Stalker2CompanionStatus(true, game, true, true, ReadBuild(marker), null);
    }

    public Stalker2CompanionStatus Install(string? gameDirectory = null, IReadOnlyList<string>? steamRoots = null)
    {
        var status = GetStatus(gameDirectory, steamRoots);
        if (!status.GameFound || !status.LoaderFound) return status;
        if (status.Issue is not null) throw new CompanionInstallerException(status.Issue, status.GameDirectory);
        if (!Directory.Exists(SourceDirectory)) throw new CompanionInstallerException($"S2 companion source is missing: {SourceDirectory}", SourceDirectory);

        var target = Path.Combine(ModsDirectory(status.GameDirectory!)!, ModFolderName);
        var staging = target + ".installing";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        CopyDirectory(SourceDirectory, staging);
        var build = BundledBuild();
        File.WriteAllText(Path.Combine(staging, MarkerFileName), JsonSerializer.Serialize(new Dictionary<string, string> { ["build"] = build ?? "unknown" }, Stalker2MarkerJson.Default.DictionaryStringString));
        // The installed copy (ours: the marker was checked above) is set aside, not deleted, until the new one is in
        // place: a failure in between puts it back instead of leaving the game without the mod.
        var previous = target + ".previous";
        if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
        var hadTarget = Directory.Exists(target);
        if (hadTarget) Directory.Move(target, previous);
        try
        {
            Directory.Move(staging, target);
        }
        catch (Exception exception) when (hadTarget && exception is IOException or UnauthorizedAccessException)
        {
            if (!Directory.Exists(target)) Directory.Move(previous, target);
            throw;
        }

        if (hadTarget) Directory.Delete(previous, recursive: true);
        return GetStatus(status.GameDirectory, steamRoots);
    }

    public static Stalker2CompanionStatus Uninstall(string? gameDirectory = null, IReadOnlyList<string>? steamRoots = null)
    {
        var status = GetStatus(gameDirectory, steamRoots);
        if (!status.ModInstalled) return status;
        Directory.Delete(Path.Combine(ModsDirectory(status.GameDirectory!)!, ModFolderName), recursive: true);
        return GetStatus(status.GameDirectory, steamRoots);
    }

    /// <summary>MOD_BUILD of the shipped main.lua.</summary>
    public string? BundledBuild()
    {
        var script = Path.Combine(SourceDirectory, "Scripts", "main.lua");
        if (!File.Exists(script)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(script), "local\\s+MOD_BUILD\\s*=\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>UE4SS 3.x keeps mods in Win64/ue4ss/Mods, older builds in Win64/Mods; null without UE4SS.</summary>
    public static string? ModsDirectory(string gameDirectory)
    {
        var win64 = Path.Combine(gameDirectory, "Stalker2", "Binaries", "Win64");
        foreach (var (dll, mods) in new[]
                 {
                     (Path.Combine(win64, "ue4ss", "UE4SS.dll"), Path.Combine(win64, "ue4ss", "Mods")),
                     (Path.Combine(win64, "UE4SS.dll"), Path.Combine(win64, "Mods")),
                 })
        {
            if (File.Exists(dll)) return mods;
        }

        return null;
    }

    private static string? FindGame(IReadOnlyList<string>? steamRoots)
    {
        foreach (var library in SteamLibraryFolderLocator.GetLibraries(steamRoots ?? SaveDirectoryLocator.DefaultSteamRoots()))
        {
            if (SteamLibraryFolderLocator.GetManifestInstallDirectory(library, SteamAppId) is { } directory && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return null;
    }

    private static string? ReadBuild(string marker)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(marker), Stalker2MarkerJson.Default.DictionaryStringString)?.GetValueOrDefault("build");
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class Stalker2MarkerJson : System.Text.Json.Serialization.JsonSerializerContext;
