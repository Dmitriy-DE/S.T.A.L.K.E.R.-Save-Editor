using System.Text.Json;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;

namespace StalkerSaveEditor.Cli;

internal static partial class Program
{
    private static int Fixes(string[] args)
    {
        const string usage = "Usage: fixes list [--game TARGET] [--json] | fixes status TARGET GAME_DIR [--json] | fixes apply-preset <essential|recommended|all-safe> TARGET GAME_DIR [--json] | fixes apply-preset <essential|recommended|all-safe> all [--steam-root PATH] [--json] | fixes install ID GAME_DIR | fixes update ID GAME_DIR | fixes remove ID GAME_DIR";
        if (args.Length >= 5 && args[1] == "extract")
        {
            // fixes extract TARGET GAME_DIR OUT_DIR [--archives-only] [PREFIX...]: research only — the game's own
            // files as the game resolves them (archives, loose files on top; or the archives alone), to write and
            // check a fix against the real originals.
            if (!GameTargetCatalog.TryParse(args[2], out var extractTarget)) throw new ArgumentException(usage);
            var archivesOnly = args.Skip(5).Contains("--archives-only");
            var (files, issues) = GameFileExtractor.Extract(extractTarget, args[3], args[4],
                args.Skip(5).Where(argument => argument != "--archives-only").ToArray(), archivesOnly);
            foreach (var issue in issues) Console.Error.WriteLine("Warning: " + issue);
            Console.WriteLine($"{files} files -> {args[4]}");
            return 0;
        }

        if (args.Length >= 2 && args[1] == "list")
        {
            GameTarget? selectedTarget = null;
            var json = false;
            for (var index = 2; index < args.Length; index++)
            {
                if (args[index] == "--json") json = true;
                else if (args[index] == "--game" && index + 1 < args.Length && GameTargetCatalog.TryParse(args[++index], out var parsed))
                    selectedTarget = parsed;
                else
                    throw new ArgumentException(usage);
            }

            var entries = GameFixCatalog.All
                .Where(definition => selectedTarget is null || definition.Game == selectedTarget)
                .Select(definition => new GameFixListJsonEntry(
                    definition.Id,
                    GameTargetCatalog.Get(definition.Game).Id,
                    definition.Version,
                    definition.Title,
                    definition.Category,
                    definition.Maturity,
                    definition.SupportedSteamBuildIds.ToArray(),
                    definition.DependsOn.ToArray(),
                    definition.ConflictsWith.ToArray(),
                    GameFixEngine.ManagedPaths(definition).ToArray()))
                .ToArray();
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(entries, CliJsonContext.Default.GameFixListJsonEntries));
            else if (entries.Length == 0)
                Console.WriteLine("No evidence-validated game-file fixes are currently catalogued.");
            else
                foreach (var entry in entries) Console.WriteLine($"{entry.Id} [{entry.Game}] {entry.Category}: {entry.Title}");
            return 0;
        }

        if (args.Length >= 2 && args[1] == "status")
        {
            if (args.Length is < 4 or > 5 || !GameTargetCatalog.TryParse(args[2], out var target) ||
                args.Length == 5 && args[4] != "--json")
                throw new ArgumentException(usage);
            var report = GameDoctor.Analyze(target, args[3]);
            var catalogue = GameFixCatalog.ForGame(target);
            var recommended = GameFixCatalog.ForPreset(target, GameFixPreset.Recommended);
            var compatibleRecommended = recommended.Count > 0 && report.SteamBuildId is { } buildId &&
                recommended.All(definition => definition.SupportedSteamBuildIds.Contains(buildId, StringComparer.Ordinal))
                ? recommended
                : [];
            var result = new GameFixStatusJsonResult(
                GameTargetCatalog.Get(target).Id,
                report.SteamBuildId,
                report.InstalledFixes.ToArray(),
                catalogue.Select(definition => new GameFixAvailableJsonEntry(
                    definition.Id,
                    definition.Title,
                    definition.Category,
                    definition.Maturity,
                    definition.SupportedSteamBuildIds,
                    report.InstalledFixes.FirstOrDefault(fix => fix.Id == definition.Id)?.State ?? GameFixState.NotInstalled))
                    .ToArray(),
                compatibleRecommended.Select(definition => definition.Id).ToArray(),
                GameFixCatalog.DatasetVersion,
                catalogue.Count == 0
                    ? "No evidence-validated game-file fixes are currently catalogued."
                    : recommended.Count == 0
                        ? "Catalogued fixes are experimental or research-only and are not included in safe presets."
                        : compatibleRecommended.Count == 0
                            ? "The detected build is not supported by the complete safe preset."
                        : null);
            if (args.Length == 5)
                Console.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.GameFixStatusJsonResult));
            else
                Console.WriteLine($"{GameTargetCatalog.Get(target).Title}; Steam build {report.SteamBuildId ?? "unknown"}; " +
                    $"{catalogue.Count} catalogued; {compatibleRecommended.Count} compatible safe recommendations; " +
                    $"{report.InstalledFixes.Count} installed; " +
                    $"{catalogue.Count(definition => definition.Maturity == GameFixMaturity.Experimental)} experimental.");
            return 0;
        }

        if (args.Length >= 2 && args[1] == "apply-preset")
        {
            var preset = ParseFixPreset(args.Length > 2 ? args[2] : string.Empty);
            if (args.Length >= 4 && args[3].Equals("all", StringComparison.OrdinalIgnoreCase))
                return ApplyPresetToDiscoveredInstallations(args, preset, usage);

            if (args.Length is < 5 or > 6 || !GameTargetCatalog.TryParse(args[3], out var target) ||
                args.Length == 6 && args[5] != "--json")
                throw new ArgumentException(usage);
            var application = new ToolkitSnapshotService().ApplyFixPreset(target, args[4], preset);
            if (!application.Applied)
                throw new InvalidOperationException((application.Error ?? "The preset could not be applied.") +
                    (application.SafetySnapshotId is null ? string.Empty : $" Safety snapshot {application.SafetySnapshotId} is retained."));
            var result = application.PresetResult!;
            if (args.Length == 6)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new GameFixPresetJsonResult(
                        GameTargetCatalog.Get(target).Id,
                        result.Preset,
                        result.SelectedFixCount,
                        result.InstalledFixIds.ToArray(),
                        result.AlreadyInstalledFixIds.ToArray(),
                        result.Changed,
                        application.SafetySnapshotId),
                    CliJsonContext.Default.GameFixPresetJsonResult));
            }
            else
            {
                Console.WriteLine($"{result.Preset}: installed {result.InstalledFixIds.Count} of {result.SelectedFixCount} safe fix(es); " +
                    $"{result.AlreadyInstalledFixIds.Count} already current." +
                    (application.SafetySnapshotId is null ? string.Empty : $" Safety snapshot {application.SafetySnapshotId}."));
            }
            return 0;
        }

        if (args.Length == 4 && args[1] is ("install" or "update" or "remove"))
        {
            var engine = new StalkerSaveEditor.Core.Patching.GameFixEngine();
            if (args[1] is "install" or "update")
            {
                if (!GameFixCatalog.TryGet(args[2], out var definition) || definition is null)
                    throw new KeyNotFoundException("Fix is not present in the evidence-validated catalogue: " + args[2]);
                var result = args[1] == "install"
                    ? engine.Install(definition, args[3])
                    : engine.Update(definition, args[3]);
                Console.WriteLine($"{result.State}: {definition.Id}" + (result.Changed
                    ? args[1] == "update" ? " (updated)" : " (changed)"
                    : " (already current)"));
            }
            else
            {
                var result = engine.Uninstall(args[2], args[3]);
                Console.WriteLine($"{result.State}: {args[2]}" + (result.Changed ? " (restored)" : " (unchanged)"));
            }
            return 0;
        }

        throw new ArgumentException(usage);
    }

    private static GameFixPreset ParseFixPreset(string value) => value.ToLowerInvariant() switch
    {
        "essential" or "essential-only" => GameFixPreset.EssentialOnly,
        "recommended" => GameFixPreset.Recommended,
        "all-safe" or "all-safe-fixes" => GameFixPreset.AllSafeFixes,
        _ => throw new ArgumentException("Preset must be essential, recommended, or all-safe. Custom selections use individual fix install/remove commands."),
    };

    private static int ApplyPresetToDiscoveredInstallations(string[] args, GameFixPreset preset, string usage)
    {
        var json = false;
        var steamRoots = new List<string>();
        for (var index = 4; index < args.Length; index++)
        {
            if (args[index] == "--json") json = true;
            else if (args[index] == "--steam-root" && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                steamRoots.Add(args[++index]);
            else
                throw new ArgumentException(usage);
        }

        var installations = GameDoctor.DiscoverInstallations(steamRoots.Count == 0 ? null : steamRoots);
        var results = new List<GameFixPresetInstallationJsonResult>();
        foreach (var installation in installations)
        {
            var target = installation.Target;
            var selected = GameFixCatalog.ForPreset(target, preset);
            if (selected.Count == 0) continue;
            var targetId = GameTargetCatalog.Get(target).Id;
            if (installation.BuildId is not { } buildId ||
                selected.Any(definition => !definition.SupportedSteamBuildIds.Contains(buildId, StringComparer.Ordinal)))
            {
                results.Add(new GameFixPresetInstallationJsonResult(
                    targetId, installation.BuildId, selected.Count, [], [],
                    "The discovered build is not supported by every selected fix.", null));
                continue;
            }

            try
            {
                var application = new ToolkitSnapshotService().ApplyFixPreset(target, installation.Directory, preset);
                if (!application.Applied)
                {
                    results.Add(new GameFixPresetInstallationJsonResult(
                        targetId, installation.BuildId, selected.Count, [], [], application.Error, application.SafetySnapshotId));
                    continue;
                }
                var applied = application.PresetResult!;
                results.Add(new GameFixPresetInstallationJsonResult(
                    targetId,
                    installation.BuildId,
                    applied.SelectedFixCount,
                    applied.InstalledFixIds.ToArray(),
                    applied.AlreadyInstalledFixIds.ToArray(),
                    null,
                    application.SafetySnapshotId));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or InvalidOperationException or FormatException or NotSupportedException or
                InvalidDataException or OverflowException)
            {
                results.Add(new GameFixPresetInstallationJsonResult(
                    targetId, installation.BuildId, selected.Count, [], [], exception.Message, null));
            }
        }

        var response = new GameFixPresetAllJsonResult(preset, GameFixCatalog.DatasetVersion, results.ToArray());
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(response, CliJsonContext.Default.GameFixPresetAllJsonResult));
        }
        else if (results.Count == 0)
        {
            Console.WriteLine("No compatible game installations with fixes in this preset were found; no files changed.");
        }
        else
        {
            foreach (var result in results)
            {
                if (result.Error is { } error)
                    Console.WriteLine($"{result.Game}: skipped or failed — {error}" +
                        (result.SafetySnapshotId is null ? string.Empty : $" Safety snapshot {result.SafetySnapshotId}."));
                else
                    Console.WriteLine($"{result.Game}: installed {result.InstalledFixIds.Length} of {result.SelectedFixCount}; " +
                        $"{result.AlreadyInstalledFixIds.Length} already current." +
                        (result.SafetySnapshotId is null ? string.Empty : $" Safety snapshot {result.SafetySnapshotId}."));
            }
        }

        return results.Any(result => result.Error is not null &&
            result.Error != "The discovered build is not supported by every selected fix.") ? 1 : 0;
    }

    private static int S2Mods(string[] args)
    {
        const string usage = "Usage: mods <s2-disable|s2-restore> GAME_DIR";
        if (args.Length != 3 || args[1] is not ("s2-disable" or "s2-restore")) throw new ArgumentException(usage);
        var result = args[1] == "s2-disable"
            ? Stalker2ModToggle.Disable(args[2])
            : Stalker2ModToggle.Restore(args[2]);
        Console.WriteLine((result.Changed ? "Changed" : "Unchanged") + ": " + result.Message);
        return 0;
    }
}
