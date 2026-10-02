using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Cli;

internal static class Program
{
    private const string Usage =
        "Usage: StalkerSaveEditor.Cli <version|companion|doctor|fixes|crash|mods|info|inventory|orphans|set-money|set-stack|dump-record|diff-record|edit> ...\n" +
        "Exit codes: 0 done, 2 wrong arguments, 3 refused (unsupported or unsafe), 4 unreadable or damaged input, 5 file or system error.";

    internal static int Main(string[] args)
    {
        if (args is ["--steam-native-worker"])
        {
            return RunWorker(SteamNativeWorkerHost.RunAsync);
        }

        if (args.Length >= 2 && args[0] == "--steam-native-op" && args[1] == "session")
        {
            var appId = ReadAppId(args[2..]);
            return appId is null
                ? 2
                : RunWorker(() => SteamNativeWorkerHost.RunGameSessionAsync(appId.Value));
        }

        if (args is ["--steam-native-op", "achievements", "--app-id", var appIdText]
            && int.TryParse(appIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var listAppId)
            && listAppId > 0)
        {
            return RunWorker(() => SteamAchievementsWorkerHost.RunAchievementsAsync(listAppId));
        }

        if (args is ["--steam-native-op", "achievement", "--app-id", var setAppIdText, "--name", var apiName, "--achieved", "1"]
            && int.TryParse(setAppIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var unlockAppId)
            && unlockAppId > 0)
        {
            return RunWorker(() => SteamAchievementsWorkerHost.RunAchievementAsync(unlockAppId, apiName, achieved: true));
        }

        if (args is ["--steam-native-op", "achievement", "--app-id", var clearAppIdText, "--name", var clearApiName, "--achieved", "0"]
            && int.TryParse(clearAppIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var clearAppId)
            && clearAppId > 0)
        {
            return RunWorker(() => SteamAchievementsWorkerHost.RunAchievementAsync(clearAppId, clearApiName, achieved: false));
        }

        try
        {
            return RunCommand(args);
        }
        catch (Exception exception) when (ExitCodeFor(exception) is { } code)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            return code;
        }
    }

    internal const int ExitUsage = 2;
    internal const int ExitRefused = 3;
    internal const int ExitInvalidInput = 4;
    internal const int ExitIo = 5;

    /// <summary>Stable exit codes for scripts; null for exceptions that are bugs and should crash loudly.</summary>
    internal static int? ExitCodeFor(Exception exception) => exception switch
    {
        ArgumentException => ExitUsage,
        StalkerSaveEditor.Core.Patching.GameFixOperationException { Failure: StalkerSaveEditor.Core.Patching.GameFixFailure.Refused } => ExitRefused,
        NotSupportedException or InvalidOperationException => ExitRefused,
        InvalidDataException or JsonException or FormatException or OverflowException or KeyNotFoundException => ExitInvalidInput,
        IOException or UnauthorizedAccessException => ExitIo,
        _ => null,
    };

    /// <summary>
    /// Steam worker modes talk to the editor over pipes. A failure they did not report themselves must still end as
    /// one line on stderr and a non-zero exit code, not as an unhandled-exception dump.
    /// </summary>
    private static int RunWorker(Func<Task<int>> worker)
    {
        try
        {
            return worker().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            return 1;
        }
    }

    private static int RunCommand(string[] args)
    {
        if (args is ["version"])
        {
            Console.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        if (args is ["--help"] or ["-h"] or [])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        return args[0] switch
        {
            "companion" => Companion(args),
            "crash" => Crash(args),
            "doctor" => Doctor(args),
            "fixes" => Fixes(args),
            "mods" => S2Mods(args),
            "info" => ReadCommand(args, ReadMode.Info),
            "inventory" => ReadCommand(args, ReadMode.Inventory),
            "orphans" => ReadCommand(args, ReadMode.Orphans),
            "set-money" => SetMoney(args),
            "set-stack" => SetStack(args),
            "edit" => Edit(args),
            "dump-record" => DumpRecord(args),
            "diff-record" => DiffRecord(args),
            "move" or "detach" or "attach-orphan" or "raw" => UnsupportedWrite(args[0]),
            _ => UnknownCommand(),
        };
    }

    private static int Crash(string[] args)
    {
        const string usage = "Usage: crash discover [--steam-root PATH] [--json] | crash analyse LOG [--game NAME] [--json]";
        if (args.Length >= 2 && args[1] == "discover")
        {
            var steamRoots = new List<string>();
            var discoverJson = false;
            for (var index = 2; index < args.Length; index++)
            {
                if (args[index] == "--json") discoverJson = true;
                else if (args[index] == "--steam-root" && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    steamRoots.Add(args[++index]);
                else
                    throw new ArgumentException(usage);
            }

            var logs = CrashLogDiscovery.DiscoverRecentLogs(
                saveDirectoryOptions: new StalkerSaveEditor.Core.Storage.SaveDirectoryDiscoveryOptions
                {
                    SteamRoots = steamRoots.Count == 0 ? null : steamRoots,
                });
            if (discoverJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(logs.ToArray(), CliJsonContext.Default.DiscoveredCrashLogArray));
            }
            else if (logs.Count == 0)
            {
                Console.WriteLine("No S.T.A.L.K.E.R. trilogy log files were found in discovered game or save-profile locations.");
            }
            else
            {
                foreach (var log in logs)
                    Console.WriteLine($"{GameTargetCatalog.Get(log.Game).Title}; build {log.BuildId ?? "unknown"}; " +
                        $"{log.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture)}; {log.Path}");
            }

            return 0;
        }

        if (args.Length < 3 || args[1] != "analyse") throw new ArgumentException(usage);
        string? game = null;
        var json = false;
        for (var index = 3; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--json":
                    json = true;
                    break;
                case "--game" when index + 1 < args.Length:
                    game = args[++index];
                    break;
                default:
                    throw new ArgumentException(usage);
            }
        }

        var fileTime = new DateTimeOffset(File.GetLastWriteTimeUtc(args[2]), TimeSpan.Zero);
        var analysis = CrashLogAnalyzer.Analyze(File.ReadAllText(args[2]), game, fileTime);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(analysis, CliJsonContext.Default.CrashLogAnalysis));
            return 0;
        }

        Console.WriteLine("Kind: " + analysis.Kind);
        if (analysis.Game is not null) Console.WriteLine("Game: " + analysis.Game);
        Console.WriteLine("Summary: " + analysis.Summary);
        if (analysis.FileLastWriteTimeUtc is { } fileTimeUtc)
            Console.WriteLine("Log file modified (UTC): " + fileTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        if (analysis.File is not null) Console.WriteLine("File: " + analysis.File);
        if (analysis.Line is not null) Console.WriteLine("Line: " + analysis.Line.Value.ToString(CultureInfo.InvariantCulture));
        if (analysis.Exception is not null) Console.WriteLine("Context: " + analysis.Exception);
        Console.WriteLine("Known issue: " + (analysis.KnownIssueId ?? "no validated signature match"));
        if (analysis.KnownIssue is { } issue)
        {
            Console.WriteLine("  " + issue.Title + ": " + issue.Explanation);
            Console.WriteLine("  Advice: " + issue.Advice +
                (issue.FixId is null ? string.Empty : "; game fix " + issue.FixId) +
                (issue.QuestRuleId is null ? string.Empty : "; Quest Doctor rule " + issue.QuestRuleId));
            Console.WriteLine("  Source: " + issue.Source);
        }
        return 0;
    }

    private static int Doctor(string[] args)
    {
        if (args.Length >= 2 && args[1] == "discover")
        {
            var json = false;
            var steamRoots = new List<string>();
            for (var index = 2; index < args.Length; index++)
            {
                if (args[index] == "--json")
                {
                    json = true;
                }
                else if (args[index] == "--steam-root" && index + 1 < args.Length)
                {
                    steamRoots.Add(args[++index]);
                }
                else
                {
                    throw new ArgumentException("Usage: doctor discover [--steam-root PATH]... [--json]");
                }
            }

            var installations = GameDoctor.DiscoverInstallations(steamRoots.Count == 0 ? null : steamRoots);
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(installations, CliJsonContext.Default.GameDoctorInstallationArray));
            }
            else if (installations.Count == 0)
            {
                Console.WriteLine("No structurally valid supported game installations were found.");
            }
            else
            {
                foreach (var installation in installations)
                {
                    Console.WriteLine($"{GameTargetCatalog.Get(installation.Target).Title} [{installation.Source}] " +
                        $"(build {installation.BuildId ?? "unknown"}): {installation.Directory}");
                }
            }
            return 0;
        }

        if (args.Length >= 2 && args[1] == "save")
        {
            const string saveUsage = "Usage: doctor save SAVE_FILE [--json]";
            if (args.Length is < 3 or > 4 || args.Length == 4 && args[3] != "--json")
                throw new ArgumentException(saveUsage);
            var saveReport = SaveDoctor.Analyze(File.ReadAllBytes(args[2]));
            if (args.Length == 4)
            {
                Console.WriteLine(JsonSerializer.Serialize(saveReport, CliJsonContext.Default.SaveDoctorReport));
            }
            else
            {
                Console.WriteLine("Save Doctor: " + saveReport.Status);
                if (saveReport.Overview is { } overview)
                    Console.WriteLine($"Format: {overview.FormatId}; parsed inventory records: {overview.ItemCount}");
                foreach (var check in saveReport.Checks)
                    Console.WriteLine($"[{check.Status}] {check.Id}: {check.Summary}" +
                        (check.Detail.Length == 0 ? string.Empty : " — " + check.Detail));
            }
            return saveReport.Status == SaveDoctorStatus.Error ? 1 : 0;
        }

        if (args.Length >= 2 && args[1] == "quest-repair")
        {
            if (args.Length < 3) throw new ArgumentException("Usage: doctor quest-repair SAVE_FILE [--output PATH] [--backup-dir DIR]");
            var repairOptions = ParseOptions(args, 3);
            var sourceBytes = File.ReadAllBytes(args[2]);
            var repaired = QuestDoctor.PrepareRepair(sourceBytes);
            if (repaired is null)
            {
                Console.WriteLine("No proven broken quest; the save was not changed.");
                return 0;
            }

            var repairReceipt = LocalSaveStorage.ExportLocal(
                args[2],
                repairOptions.OutputPath ?? DefaultOutputPath(args[2]),
                repaired,
                repairOptions.BackupDirectory ?? DefaultBackupDirectory());
            QuestDoctor.VerifyRepair(File.ReadAllBytes(repairReceipt.OutputPath));
            foreach (var state in QuestDoctor.Analyze(sourceBytes).States.Where(state => state.State == QuestTaskStatus.Broken))
            {
                Console.WriteLine("Repaired " + state.TaskId + ": added " + state.MissingInfoPortion);
            }
            Console.WriteLine("Output: " + repairReceipt.OutputPath);
            Console.WriteLine("Backup: " + repairReceipt.BackupPath);
            Console.WriteLine("SHA256: " + repairReceipt.OutputSha256);
            return 0;
        }

        if (args.Length >= 2 && args[1] == "quest")
        {
            const string questUsage = "Usage: doctor quest SAVE_FILE [--json]";
            if (args.Length is < 3 or > 4 || args.Length == 4 && args[3] != "--json")
                throw new ArgumentException(questUsage);
            var questReport = QuestDoctor.Analyze(File.ReadAllBytes(args[2]));
            if (args.Length == 4)
            {
                Console.WriteLine(JsonSerializer.Serialize(questReport, CliJsonContext.Default.QuestDoctorReport));
            }
            else
            {
                Console.WriteLine("Quest Doctor: " + questReport.Status);
                Console.WriteLine("Format: " + (questReport.FormatId ?? "unknown"));
                Console.WriteLine(questReport.Summary);
                foreach (var state in questReport.States)
                {
                    Console.WriteLine($"  [{state.State}] {state.TaskId}: {state.Title}");
                    if (state.State == "broken")
                    {
                        Console.WriteLine($"    missing info portion: {state.MissingInfoPortion}; preventing fix: {state.PreventingFixId ?? "none"}");
                    }
                }
            }
            return questReport.Status == SaveDoctorStatus.Error ? 1 : 0;
        }

        const string usage = "Usage: doctor game <soc|cs|cop|soc-ee|cs-ee|cop-ee|s2> GAME_DIR [--json]";
        if (args.Length is < 4 or > 5 || args[1] != "game" ||
            !GameTargetCatalog.TryParse(args[2], out var target) ||
            args.Length == 5 && args[4] != "--json")
        {
            throw new ArgumentException(usage);
        }

        var report = GameDoctor.Analyze(target, args[3]);
        if (args.Length == 5)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CliJsonContext.Default.GameDoctorReport));
        }
        else
        {
            Console.WriteLine("Game Doctor: " + GameTargetCatalog.Get(target).Title);
            Console.WriteLine("Directory: " + report.GameDirectory);
            Console.WriteLine("Steam build: " + (report.SteamBuildId ?? "unknown"));
            foreach (var check in report.Checks)
            {
                Console.WriteLine($"[{check.Status}] {check.Id}: {check.Summary}" +
                    (check.Detail.Length == 0 ? string.Empty : " — " + check.Detail));
            }
            foreach (var path in report.LooseFiles) Console.WriteLine("Loose file: " + path);
        }

        return report.Checks.Any(check => check.Status == GameDoctorStatus.Error) ? 1 : 0;
    }

    /// <summary>
    /// fixes build-pack TARGET GAME_DIR PACK_GAMEDATA --store DIR --out FILE: diffs a fix pack against the game's
    /// archives, copies the changed/new files into a content store (named by SHA-256) and writes the overlay list.
    /// </summary>
    private static int FixesBuildPack(string[] args)
    {
        const string usage = "Usage: fixes build-pack TARGET GAME_DIR PACK_GAMEDATA --store DIR --out FILE";
        if (!GameTargetCatalog.TryParse(args[2], out var target)) throw new ArgumentException(usage);
        string? store = null, output = null;
        for (var index = 5; index < args.Length; index++)
        {
            if (args[index] == "--store" && index + 1 < args.Length) store = args[++index];
            else if (args[index] == "--out" && index + 1 < args.Length) output = args[++index];
            else throw new ArgumentException(usage);
        }

        if (store is null || output is null) throw new ArgumentException(usage);
        var build = FixPackBuilder.Build(target, args[3], args[4]);
        Directory.CreateDirectory(store);
        foreach (var file in build.Files)
        {
            var destination = Path.Combine(store, file.ContentSha256);
            if (!File.Exists(destination)) File.Copy(file.SourcePath, destination);
        }

        using (var stream = File.Create(output))
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("game", GameTargetCatalog.Get(target).Id);
            writer.WriteStartArray("overlays");
            foreach (var file in build.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("relativePath", file.RelativePath);
                writer.WriteString("contentSha256", file.ContentSha256);
                if (file.OriginalSha256 is { } original) writer.WriteString("expectedFileSha256", original);
                else writer.WriteNull("expectedFileSha256");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("unchanged", build.Unchanged.Count);
            writer.WriteStartArray("issues");
            foreach (var issue in build.Issues) writer.WriteStringValue(issue);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        Console.WriteLine($"{build.Files.Count} files ({build.Files.Count(file => file.OriginalSha256 is null)} new), {build.Unchanged.Count} unchanged, {build.Issues.Count} issues -> {output}");
        return 0;
    }

    private static int Fixes(string[] args)
    {
        const string usage = "Usage: fixes list [--game TARGET] [--json] | fixes status TARGET GAME_DIR [--json] | fixes apply-preset <essential|recommended|all-safe> TARGET GAME_DIR [--json] | fixes apply-preset <essential|recommended|all-safe> all [--steam-root PATH] [--json] | fixes install ID GAME_DIR | fixes update ID GAME_DIR | fixes remove ID GAME_DIR";
        if (args.Length >= 5 && args[1] == "build-pack")
        {
            return FixesBuildPack(args);
        }

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

    private static int ReadCommand(string[] args, ReadMode mode)
    {
        if (args.Length < 2 || (mode != ReadMode.Inventory && args.Length != 2) ||
            (mode == ReadMode.Inventory && args.Length > 3))
        {
            throw new ArgumentException("Invalid arguments. " + Usage);
        }

        if (mode == ReadMode.Inventory && args.Length == 3 && args[2] != "--all")
        {
            throw new ArgumentException("Unknown inventory option: " + args[2]);
        }

        var sourcePath = args[1];
        var bytes = File.ReadAllBytes(sourcePath);
        var release = DetectRequired(bytes);
        if (mode != ReadMode.Info) Console.WriteLine("Format: " + release);

        if (release == "stalker2")
        {
            var save = Stalker2SaveReader.FromBytes(bytes);
            if (mode == ReadMode.Info)
            {
                PrintInfo(bytes, release, save.CrcOk ? "CRC: OK" : "CRC: BAD", save.PackedSize,
                    save.UnpackedSize, save.Money, save.OwnedHandles.Count, save.GridHandleCount,
                    save.GridCellCount, save.Inventory.Count, save.Orphans.Count);
                foreach (var warning in save.Warnings) Console.WriteLine("Warning: " + warning);
                return 0;
            }

            if (mode == ReadMode.Inventory)
            {
                Console.WriteLine("POS        TYPE                 KEY                       COUNT   HANDLE");
                foreach (var item in save.Inventory)
                {
                    Console.WriteLine("{0,-10} {1,-20} {2,-25} {3,7}  0x{4:X8}",
                        item.PositionLabel ?? "?", item.Category, item.TypeKey, item.Count, item.Handle);
                }
                return 0;
            }

            Console.WriteLine("TYPE                 KEY     COUNT  RECORDPOS     HANDLE");
            foreach (var item in save.Orphans)
            {
                Console.WriteLine("{0,-20} {1,-7} {2,5}  0x{3:X8}  0x{4:X8}",
                    item.Category, item.TypeKey, item.Count, item.RecordOffset, item.Handle);
            }
            return 0;
        }

        var xray = ReadXRay(bytes, release);
        if (mode == ReadMode.Info)
        {
            Console.WriteLine("Integrity: X-Ray LZO/container OK");
            Console.WriteLine("Format: " + release);
            Console.WriteLine("Packed: " + bytes.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Raw: " + XRayContainer.FromBytes(bytes).UnpackedSize.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("SHA256: " + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Console.WriteLine("Money: " + xray.Money.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Inventory objects: " + xray.Inventory.Count.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        if (mode == ReadMode.Inventory)
        {
            Console.WriteLine("POS        TYPE                 KEY                       COUNT   HANDLE");
            foreach (var item in xray.Inventory)
            {
                Console.WriteLine("{0,-10} {1,-20} {2,-25} {3,7}  0x{4:X4}",
                    item.PlacementType ?? "inventory", item.Category, item.TypeKey,
                    item.Count?.ToString(CultureInfo.InvariantCulture) ?? "unknown", item.Handle);
            }
            return 0;
        }

        throw new NotSupportedException("Orphan object analysis is not exposed for X-Ray by the confirmed Core reader.");
    }

    private static void PrintInfo(
        byte[] bytes,
        string release,
        string integrity,
        int packedSize,
        int unpackedSize,
        uint money,
        int owned,
        int gridHandles,
        int gridCells,
        int inventory,
        int orphans)
    {
        Console.WriteLine(integrity);
        Console.WriteLine("Format: " + release);
        Console.WriteLine("Packed: " + packedSize.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Raw: " + unpackedSize.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("SHA256: " + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Console.WriteLine("Money: " + money.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Owned handles: " + owned.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Grid handles parsed/total: " + gridHandles.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Grid cells: " + gridCells.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Inventory objects: " + inventory.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Orphans: " + orphans.ToString(CultureInfo.InvariantCulture));
    }

    private static int SetMoney(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("set-money requires SAVE and MONEY.");
        var options = ParseOptions(args, 3);
        var money = ParseUInt(args[2]);
        return PrepareAndExport(args[1], options, plan => new EditPlan(plan, money: money));
    }

    private static int SetStack(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("set-stack requires SAVE, HANDLE and COUNT.");
        var options = ParseOptions(args, 4);
        var handle = ParseUInt(args[2]);
        var count = ParseUInt(args[3]);
        return PrepareAndExport(args[1], options, plan => new EditPlan(plan, stackCounts: new Dictionary<uint, uint> { [handle] = count }));
    }

    private static int Edit(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("edit requires SAVE.");
        var options = ParseOptions(args, 2);
        if (options.Unsupported.Count > 0)
        {
            throw new NotSupportedException("The requested write kind is not supported by a confirmed Core writer: " +
                string.Join(", ", options.Unsupported));
        }

        if (options.Money is null && options.Stacks.Count == 0 && options.Adds.Count == 0 &&
            options.Durability.Count == 0 && options.Upgrades.Count == 0 && options.Placements.Count == 0)
        {
            throw new InvalidOperationException("No changes requested.");
        }

        return PrepareAndExport(args[1], options, sourceSha => new EditPlan(
            sourceSha,
            money: options.Money,
            stackCounts: options.Stacks,
            adds: options.Adds,
            durability: options.Durability,
            upgrades: options.Upgrades,
            placements: options.Placements));
    }

    private static int PrepareAndExport(
        string sourcePath,
        CommandOptions options,
        Func<string, EditPlan> makePlan)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var release = DetectRequired(sourceBytes);
        var plan = makePlan(Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant());
        const EditKind readBackVerifiedKinds = EditKind.Money | EditKind.StackCounts;
        if ((plan.EditKinds & ~readBackVerifiedKinds) != EditKind.None)
        {
            throw new NotSupportedException(
                "Edit kinds other than money and stack counts are not supported because EditService.VerifyReadBack does not verify them completely.");
        }

        if (!EditService.CanEdit(release, plan.EditKinds))
        {
            throw new NotSupportedException("The capability registry does not allow this edit for " + release + ".");
        }

        var needsCatalog = plan.Adds.Count > 0 || plan.Upgrades.Count > 0;
        var catalogs = needsCatalog ? CatalogBundleReader.LoadEmbedded()[release] : null;
        var prepared = EditService.PrepareEdit(sourceBytes, plan, release, catalogs);
        var outputPath = options.OutputPath ?? DefaultOutputPath(sourcePath);
        var backupDirectory = options.BackupDirectory ?? DefaultBackupDirectory();
        var receipt = LocalSaveStorage.ExportLocal(sourcePath, outputPath, prepared, backupDirectory);
        var readBack = File.ReadAllBytes(receipt.OutputPath);
        EditService.VerifyReadBack(readBack, release, plan);
        Console.WriteLine("Output: " + receipt.OutputPath);
        Console.WriteLine("Size: " + readBack.Length.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Backup: " + receipt.BackupPath);
        Console.WriteLine("SHA256: " + receipt.OutputSha256);
        if (plan.Money is { } money) Console.WriteLine("Money: " + money.ToString(CultureInfo.InvariantCulture));
        foreach (var (handle, count) in plan.StackCounts)
        {
            Console.WriteLine("Stack 0x{0:X8}: {1}", handle, count);
        }
        foreach (var add in plan.Adds) Console.WriteLine("Add " + add.ItemKey + " x" + add.Quantity);
        foreach (var (handle, condition) in plan.Durability)
        {
            Console.WriteLine("Durability 0x{0:X8}: {1}", handle, condition.ToString("0.####", CultureInfo.InvariantCulture));
        }
        foreach (var (handle, upgrades) in plan.Upgrades)
        {
            Console.WriteLine("Upgrades 0x{0:X4}: {1}", handle, string.Join(",", upgrades));
        }
        foreach (var change in plan.Placements)
        {
            Console.WriteLine("Placement 0x{0:X4}: {1}", change.Handle, change.Type);
        }
        return 0;
    }

    private static int DumpRecord(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("dump-record requires SAVE and HANDLE.");
        var bytes = File.ReadAllBytes(args[1]);
        var release = DetectRequired(bytes);
        var handle = ParseUInt(args[2]);
        var limit = ReadLimit(args, 3, 768);
        var window = GetRecordWindow(bytes, release, handle, limit);
        Console.WriteLine("base=0x{0:X}, bytes={1}", window.Offset, window.Bytes.Length);
        var blob = window.Bytes.AsSpan();
        for (var offset = 0; offset < blob.Length; offset += 16)
        {
            var row = blob.Slice(offset, Math.Min(16, blob.Length - offset));
            Console.WriteLine("+0x{0:X4}  {1}", offset, Convert.ToHexString(row.ToArray()).ToLowerInvariant());
        }
        return 0;
    }

    private static int DiffRecord(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("diff-record requires SAVE_A, SAVE_B and HANDLE.");
        var firstBytes = File.ReadAllBytes(args[1]);
        var secondBytes = File.ReadAllBytes(args[2]);
        var firstRelease = DetectRequired(firstBytes);
        var secondRelease = DetectRequired(secondBytes);
        if (!string.Equals(firstRelease, secondRelease, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Both files must have the same detected release.");
        }

        var handle = ParseUInt(args[3]);
        var limit = ReadLimit(args, 4, 2048);
        var first = GetRecordWindow(firstBytes, firstRelease, handle, limit).Bytes;
        var second = GetRecordWindow(secondBytes, secondRelease, handle, limit).Bytes;
        var differs = false;
        var length = Math.Max(first.Length, second.Length);
        for (var index = 0; index < length; index++)
        {
            var left = index < first.Length ? first[index] : (byte?)null;
            var right = index < second.Length ? second[index] : (byte?)null;
            if (left == right) continue;
            differs = true;
            Console.WriteLine("+0x{0:X4}: {1}  ->  {2}", index,
                left is null ? "" : left.Value.ToString("x2", CultureInfo.InvariantCulture),
                right is null ? "" : right.Value.ToString("x2", CultureInfo.InvariantCulture));
        }
        if (!differs) Console.WriteLine("No differences in compared record window");
        return 0;
    }

    private static RecordSlice GetRecordWindow(byte[] bytes, string release, uint handle, int limit)
    {
        if (release == "stalker2")
        {
            var save = Stalker2SaveReader.FromBytes(bytes);
            var item = save.Inventory.FirstOrDefault(entry => entry.Handle == handle)
                ?? throw new InvalidOperationException("The requested handle is not in the parsed inventory.");
            var raw = save.Raw.Span;
            var start = item.RecordOffset;
            var end = Math.Min(Math.Min(item.RecordEndGuess, checked(start + limit)), raw.Length);
            if (start < 0 || end < start) throw new InvalidDataException("The object record window is invalid.");
            return new RecordSlice(start, raw.Slice(start, end - start).ToArray());
        }

        var parsed = ReadXRay(bytes, release);
        if (handle is 0 or > ushort.MaxValue) throw new InvalidOperationException("The handle is outside the X-Ray range.");
        var record = parsed.FindObjectRecord((ushort)handle)
            ?? throw new InvalidOperationException("The requested handle is not in the parsed X-Ray object registry.");
        var length = Math.Min(limit, record.Bytes.Length);
        return new RecordSlice(record.Offset, record.Bytes[..length].ToArray());
    }

    private sealed record RecordSlice(int Offset, byte[] Bytes);

    private static int ReadLimit(string[] args, int start, int defaultValue)
    {
        if (start == args.Length) return defaultValue;
        if (start + 2 != args.Length || args[start] != "--limit") throw new ArgumentException("Expected --limit VALUE.");
        var limit = int.Parse(args[start + 1], CultureInfo.InvariantCulture);
        if (limit is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(args), "Record limit must be in 1…65536.");
        return limit;
    }

    private static CommandOptions ParseOptions(string[] args, int start)
    {
        var result = new CommandOptions();
        for (var index = start; index < args.Length; index++)
        {
            var option = args[index];
            string Value()
            {
                if (++index >= args.Length) throw new ArgumentException("Missing value after " + option + ".");
                return args[index];
            }

            switch (option)
            {
                case "-o":
                case "--output":
                    result.OutputPath = Value();
                    break;
                case "--backup-dir":
                    result.BackupDirectory = Value();
                    break;
                case "--money":
                    result.Money = ParseUInt(Value());
                    break;
                case "--stack":
                    AddUnique(result.Stacks, Value(), ParseAssignment);
                    break;
                case "--add":
                    var add = ParseStringAssignment(Value());
                    result.Adds.Add(new ItemAddRequest(add.Key, ParseUInt(add.Value)));
                    break;
                case "--durability":
                    var condition = ParseStringAssignment(Value());
                    result.Durability.Add(ParseUInt(condition.Key), double.Parse(condition.Value, CultureInfo.InvariantCulture));
                    break;
                case "--upgrade":
                    var upgrade = ParseStringAssignment(Value());
                    var keys = upgrade.Value.Length == 0 ? Array.Empty<string>() : upgrade.Value.Split(',');
                    result.Upgrades.Add(checked((ushort)ParseUInt(upgrade.Key)), Array.AsReadOnly(keys));
                    break;
                case "--placement":
                    var placement = ParseStringAssignment(Value());
                    var pieces = placement.Value.Split(':', 2);
                    result.Placements.Add(new XRayPlacementChange(
                        ParseUInt(placement.Key),
                        pieces[0],
                        pieces.Length == 2 && pieces[0] == "slot" ? int.Parse(pieces[1], CultureInfo.InvariantCulture) : null));
                    break;
                case "--move":
                case "--detach":
                case "--attach":
                case "--raw":
                    result.Unsupported.Add(option);
                    _ = Value();
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + option);
            }
        }
        return result;
    }

    private static void AddUnique(Dictionary<uint, uint> destination, string specification, Func<string, KeyValuePair<uint, uint>> parse)
    {
        var pair = parse(specification);
        if (!destination.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Duplicate handle in edit options.");
    }

    private static KeyValuePair<uint, uint> ParseAssignment(string value)
    {
        var parsed = ParseStringAssignment(value);
        return new KeyValuePair<uint, uint>(ParseUInt(parsed.Key), ParseUInt(parsed.Value));
    }

    private static KeyValuePair<string, string> ParseStringAssignment(string value)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1) throw new FormatException("Expected KEY=VALUE.");
        return new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]);
    }

    private static uint ParseUInt(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        if (value.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToUInt32(value[2..], 2);
        }

        if (value.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToUInt32(value[2..], 8);
        }

        return uint.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static string DetectRequired(ReadOnlySpan<byte> data) =>
        EditService.DetectFormat(data) ?? throw new InvalidDataException("Unknown or unsupported save format.");

    private static XRayTrilogySave ReadXRay(ReadOnlySpan<byte> data, string release) =>
        release.EndsWith("-ee", StringComparison.Ordinal)
            ? XRayEnhancedReader.FromBytes(data)
            : XRayTrilogyReader.FromBytes(data);

    private static string DefaultOutputPath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(fullPath) + "_edited" + Path.GetExtension(fullPath));
    }

    private static string DefaultBackupDirectory() => StalkerSaveEditor.Core.Diagnostics.AppPaths.Backups;

    private static int UnsupportedWrite(string command) =>
        throw new NotSupportedException(command + " is not supported by a confirmed Core writer.");

    private static int UnknownCommand()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    private static int? ReadAppId(string[] args)
    {
        if (args.Length != 2 || args[0] != "--app-id" ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ||
            appId <= 0)
        {
            Console.Error.WriteLine("Usage: StalkerSaveEditor.Cli --steam-native-op session --app-id <positive-id>");
            return null;
        }

        return appId;
    }

    private enum ReadMode
    {
        Info,
        Inventory,
        Orphans,
    }

    private sealed class CommandOptions
    {
        public string? OutputPath { get; set; }
        public string? BackupDirectory { get; set; }
        public uint? Money { get; set; }
        public Dictionary<uint, uint> Stacks { get; } = [];
        public List<ItemAddRequest> Adds { get; } = [];
        public Dictionary<uint, double> Durability { get; } = [];
        public Dictionary<ushort, IReadOnlyList<string>> Upgrades { get; } = [];
        public List<XRayPlacementChange> Placements { get; } = [];
        public List<string> Unsupported { get; } = [];
    }
}

internal sealed record GameFixListJsonEntry(
    string Id,
    string Game,
    string Version,
    string Title,
    GameFixCategory Category,
    GameFixMaturity Maturity,
    string[] SupportedSteamBuildIds,
    string[] DependsOn,
    string[] ConflictsWith,
    string[] Files);

internal sealed record GameFixAvailableJsonEntry(
    string Id,
    string Title,
    GameFixCategory Category,
    GameFixMaturity Maturity,
    IReadOnlyList<string> SupportedSteamBuildIds,
    GameFixState Installed);

internal sealed record GameFixStatusJsonResult(
    string Game,
    string? BuildId,
    GameFixInstalledInfo[] InstalledFixes,
    GameFixAvailableJsonEntry[] AvailableFixes,
    string[] RecommendedFixIds,
    string CatalogueVersion,
    string? CatalogueNote);

internal sealed record GameFixPresetJsonResult(
    string Game,
    GameFixPreset Preset,
    int SelectedFixCount,
    string[] InstalledFixIds,
    string[] AlreadyInstalledFixIds,
    bool Changed,
    string? SafetySnapshotId);

internal sealed record GameFixPresetInstallationJsonResult(
    string Game,
    string? BuildId,
    int SelectedFixCount,
    string[] InstalledFixIds,
    string[] AlreadyInstalledFixIds,
    string? Error,
    string? SafetySnapshotId);

internal sealed record GameFixPresetAllJsonResult(
    GameFixPreset Preset,
    string CatalogueVersion,
    GameFixPresetInstallationJsonResult[] Installations);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CrashLogAnalysis))]
[JsonSerializable(typeof(DiscoveredCrashLog[]), TypeInfoPropertyName = "DiscoveredCrashLogArray")]
[JsonSerializable(typeof(SaveDoctorReport))]
[JsonSerializable(typeof(QuestDoctorReport))]
[JsonSerializable(typeof(GameDoctorReport))]
[JsonSerializable(typeof(GameDoctorInstallation[]), TypeInfoPropertyName = "GameDoctorInstallationArray")]
[JsonSerializable(typeof(GameFixListJsonEntry[]), TypeInfoPropertyName = "GameFixListJsonEntries")]
[JsonSerializable(typeof(GameFixStatusJsonResult))]
[JsonSerializable(typeof(GameFixPresetJsonResult))]
[JsonSerializable(typeof(GameFixPresetAllJsonResult))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
