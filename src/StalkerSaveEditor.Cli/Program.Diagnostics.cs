using System.Globalization;
using System.Text.Json;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Cli;

internal static partial class Program
{
    private static int Crash(string[] args)
    {
        const string usage = "Usage: crash discover [--steam-root PATH] [--json] | crash analyse LOG_OR_MDMP [--game NAME] [--json]";
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

        var analysis = CrashLogAnalyzer.AnalyzeFile(args[2], game);
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
        foreach (var evidence in analysis.Evidence) Console.WriteLine("  " + evidence);
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
}
