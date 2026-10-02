using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Cli;

internal static partial class Program
{
    private const string Usage =
        "Usage: StalkerSaveEditor.Cli <version|companion|doctor|fixes|crash|mods|info|inventory|orphans|set-money|set-stack|dump-record|diff-record|edit> ...\n" +
        "Exit codes: 0 done, 2 wrong arguments, 3 refused (unsupported or unsafe), 4 unreadable or damaged input, 5 file or system error.";

    internal static int Main(string[] args)
    {
        if (SteamWorkerCommandLine.TryRun(args, out var workerExit)) return workerExit;

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

    private static int UnsupportedWrite(string command) =>
        throw new NotSupportedException(command + " is not supported by a confirmed Core writer.");

    private static int UnknownCommand()
    {
        Console.Error.WriteLine(Usage);
        return 2;
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
