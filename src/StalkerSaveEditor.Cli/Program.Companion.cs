using StalkerSaveEditor.Core.Companion;

namespace StalkerSaveEditor.Cli;

internal static partial class Program
{
    private const string CompanionUsage =
        "Usage: companion <status|install|uninstall> <soc|cs|cop|s2|all> [--game-dir DIR] [--mods DIR]  (s2 is experimental and needs UE4SS)";

    /// <summary>
    /// Companion mod for the installer and scripts. "all" acts on every game that is found and skips
    /// the others (exit 0), so a Setup component never fails on a machine without some of the games.
    /// </summary>
    private static int Companion(string[] args)
    {
        if (args.Length < 3 || args[1] is not ("status" or "install" or "uninstall")) throw new ArgumentException(CompanionUsage);
        string? gameDirectory = null;
        var mods = Path.Combine(AppContext.BaseDirectory, "mods", "companion");
        for (var index = 3; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--game-dir" when index + 1 < args.Length:
                    gameDirectory = args[++index];
                    break;
                case "--mods" when index + 1 < args.Length:
                    mods = args[++index];
                    break;
                default:
                    throw new ArgumentException(CompanionUsage);
            }
        }

        if (args[2] == "s2") return CompanionS2(args[1], gameDirectory, mods);

        CompanionGame[] games = args[2] switch
        {
            "soc" => [CompanionGame.ShadowOfChernobyl],
            "cs" => [CompanionGame.ClearSky],
            "cop" => [CompanionGame.CallOfPripyat],
            "all" when gameDirectory is null => Enum.GetValues<CompanionGame>(),
            _ => throw new ArgumentException(CompanionUsage),
        };

        var installer = new CompanionInstaller(mods);
        var failed = false;
        foreach (var game in games)
        {
            var status = installer.GetStatus(game, gameDirectory);
            if (!status.GameFound)
            {
                Console.WriteLine($"{game}: game not found");
                failed |= args[2] != "all";
                continue;
            }

            if (args[1] == "status")
            {
                Console.WriteLine($"{game}: {status.GameDirectory}: " + (status.ModInstalled ? "installed " + status.Version : "not installed") +
                    (status.Issues.Count == 0 ? string.Empty : " (" + string.Join("; ", status.Issues) + ")"));
                continue;
            }

            try
            {
                var result = args[1] == "install" ? installer.Install(game, gameDirectory) : installer.Uninstall(game, gameDirectory);
                Console.WriteLine($"{game}: {args[1]} " + (result.Success ? result.Changed ? "done" : "already up to date" : "failed: " + string.Join("; ", result.Conflicts)));
                failed |= !result.Success;
            }
            catch (CompanionInstallerException exception)
            {
                Console.WriteLine($"{game}: {args[1]} failed: {exception.Message}");
                failed = true;
            }
        }

        return failed ? 1 : 0;
    }

    private static int CompanionS2(string command, string? gameDirectory, string mods)
    {
        var installer = new Stalker2CompanionInstaller(mods);
        var status = command switch
        {
            "install" => installer.Install(gameDirectory),
            "uninstall" => Stalker2CompanionInstaller.Uninstall(gameDirectory),
            _ => Stalker2CompanionInstaller.GetStatus(gameDirectory),
        };
        Console.WriteLine("Stalker2 (experimental): " + (status.ModInstalled ? "installed " + status.ModBuild : "not installed") +
            (status.Issue is null ? string.Empty : " (" + status.Issue + ")"));
        return status.Issue is null || command == "status" ? 0 : 1;
    }
}
