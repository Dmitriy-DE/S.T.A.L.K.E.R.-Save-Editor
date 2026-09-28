namespace StalkerSaveEditor.Core.Companion;

public enum CompanionGame
{
    ShadowOfChernobyl,
    ClearSky,
    CallOfPripyat,
}

public sealed record CompanionInstallStatus(
    CompanionGame Game,
    bool GameFound,
    string? GameDirectory,
    bool ModInstalled,
    string? Version,
    IReadOnlyList<string> Issues);

public sealed record CompanionManagedFileStatus(string RelativePath, bool Exists, bool MatchesExpectedHash);

public sealed record CompanionInstallerResult(
    bool Success,
    bool Changed,
    string Version,
    IReadOnlyList<string> Conflicts);

public sealed class CompanionInstallerException(
    string message,
    string? filePath = null,
    Exception? innerException = null)
    : IOException(message, innerException)
{
    public string? FilePath { get; } = filePath;
}

internal sealed record CompanionGameDefinition(
    CompanionGame Game,
    string Id,
    int SteamAppId,
    string ModDirectory,
    IReadOnlyList<string> InstallDirectoryNames,
    IReadOnlyList<string> FsgameFileNames)
{
    public static CompanionGameDefinition For(CompanionGame game) => game switch
    {
        CompanionGame.ShadowOfChernobyl => new(
            game,
            "soc",
            4_500,
            "soc",
            ["STALKER Shadow of Chernobyl", "STALKER Shadow of Chornobyl"],
            ["fsgame_soc.ltx", "fsgame.ltx"]),
        CompanionGame.ClearSky => new(
            game,
            "cs",
            20_510,
            "cs",
            ["STALKER Clear Sky"],
            ["fsgame.ltx"]),
        CompanionGame.CallOfPripyat => new(
            game,
            "cop",
            41_700,
            "cop",
            ["Stalker Call of Pripyat", "STALKER Call of Pripyat"],
            ["fsgame.ltx"]),
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };
}
