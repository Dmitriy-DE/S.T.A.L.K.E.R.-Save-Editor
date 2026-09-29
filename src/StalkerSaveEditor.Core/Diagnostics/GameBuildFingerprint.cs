namespace StalkerSaveEditor.Core.Diagnostics;

/// <summary>Whether the installed game's Steam build is one the editor's formats and fixes were checked against.</summary>
public enum GameBuildStatus
{
    Verified,
    Unknown,
    NotInstalled,
}

public sealed record GameBuildFingerprint(GameTarget Target, string? BuildId, GameBuildStatus Status);

/// <summary>
/// RL-6: Steam build ids (appmanifest buildid) the save formats, readers and Game Fixes were verified on (L4:
/// retail files and real saves). A different build keeps working but is flagged for re-checking.
/// </summary>
public static class GameBuildFingerprints
{
    private static readonly Dictionary<GameTarget, string[]> Verified = new()
    {
        [GameTarget.ShadowOfChernobyl] = ["11567845"],
        [GameTarget.ClearSky] = ["11450472"],
        [GameTarget.CallOfPripyat] = ["11450453"],
        [GameTarget.ShadowOfChernobylEnhancedEdition] = ["24067120"],
        [GameTarget.ClearSkyEnhancedEdition] = ["24067129"],
        [GameTarget.CallOfPripyatEnhancedEdition] = ["24067133"],
    };

    public static GameBuildFingerprint Classify(GameTarget target, string? buildId) =>
        new(target, buildId, buildId is null
            ? GameBuildStatus.NotInstalled
            : Verified.TryGetValue(target, out var builds) && builds.Contains(buildId, StringComparer.Ordinal)
                ? GameBuildStatus.Verified
                : GameBuildStatus.Unknown);

    /// <summary>The first detected install of <paramref name="target"/>, classified.</summary>
    public static GameBuildFingerprint Detect(GameTarget target, IReadOnlyList<GameDoctorInstallation>? installations = null)
    {
        installations ??= GameDoctor.DiscoverInstallations();
        return Classify(target, installations.FirstOrDefault(install => install.Target == target)?.BuildId);
    }

    public static GameTarget? TargetForFormat(string formatId) => formatId switch
    {
        "stalker-soc" => GameTarget.ShadowOfChernobyl,
        "stalker-cs" => GameTarget.ClearSky,
        "stalker-cop" => GameTarget.CallOfPripyat,
        "stalker-soc-ee" => GameTarget.ShadowOfChernobylEnhancedEdition,
        "stalker-cs-ee" => GameTarget.ClearSkyEnhancedEdition,
        "stalker-cop-ee" => GameTarget.CallOfPripyatEnhancedEdition,
        _ => null,
    };
}
