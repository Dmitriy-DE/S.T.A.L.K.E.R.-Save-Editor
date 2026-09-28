using System.Globalization;
using System.Text;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Diagnostics;

public enum CheckStatus
{
    Ok,
    Warn,
    Fail,
}

/// <summary>One Environment Doctor result: group ("game:stalker-cop", "app", "codecs"…), what, status, details and a hint.</summary>
public sealed record EnvironmentCheck(string Group, string Name, CheckStatus Status, string Detail, string Hint = "");

/// <summary>
/// «Проверить окружение» (port of the Python oracle's RL-3 checks): read-only facts about games,
/// saves, the companion, codecs and the application's own data. Steam checks live in the Steam
/// project and are passed in by the caller.
/// </summary>
public static class EnvironmentDoctor
{
    public static IReadOnlyList<EnvironmentCheck> Run(
        IEnumerable<Func<IEnumerable<EnvironmentCheck>>>? extra = null,
        bool includeMachineChecks = true)
    {
        var checks = new List<EnvironmentCheck>();
        if (includeMachineChecks)
        {
            Safe(checks, "saves", "discovery", CheckSaves);
            Safe(checks, "companion", "games", CheckCompanion);
        }

        Safe(checks, "app", "data", CheckDataDirectory);
        Safe(checks, "codecs", "kraken", CheckKraken);
        foreach (var provider in extra ?? [])
        {
            Safe(checks, "extra", "provider", provider);
        }

        return checks.AsReadOnly();
    }

    /// <summary>Plain-text report for the diagnostics bundle.</summary>
    public static string Format(IEnumerable<EnvironmentCheck> checks)
    {
        var builder = new StringBuilder();
        foreach (var check in checks)
        {
            builder.Append(CultureInfo.InvariantCulture, $"[{check.Status}] {check.Group} / {check.Name}: {check.Detail}");
            if (check.Hint.Length > 0) builder.Append(" — ").Append(check.Hint);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static IEnumerable<EnvironmentCheck> CheckSaves()
    {
        var result = SaveSlotDiscovery.Discover();
        var byRelease = result.Slots.GroupBy(slot => slot.CandidateReleaseId).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var release in new[] { "stalker-soc", "stalker-cs", "stalker-cop", "stalker2" })
        {
            var slots = byRelease.GetValueOrDefault(release) ?? [];
            var readable = slots.Count(slot => slot.FormatId is not null);
            var damaged = slots.Length - readable;
            yield return slots.Length == 0
                ? new EnvironmentCheck($"game:{release}", "saves", CheckStatus.Warn, "no saves found", "choose the save folder in Settings")
                : new EnvironmentCheck(
                    $"game:{release}",
                    "saves",
                    damaged == 0 ? CheckStatus.Ok : CheckStatus.Warn,
                    string.Create(CultureInfo.InvariantCulture, $"{readable} readable, {damaged} unreadable"),
                    damaged == 0 ? string.Empty : "unreadable files are shown but cannot be edited");
        }
    }

    private static IEnumerable<EnvironmentCheck> CheckCompanion()
    {
        var installer = new CompanionInstaller(Path.Combine(AppContext.BaseDirectory, "mods", "companion"));
        foreach (var game in Enum.GetValues<CompanionGame>())
        {
            CompanionInstallStatus status;
            string? failure = null;
            try
            {
                status = installer.GetStatus(game);
            }
            catch (CompanionInstallerException exception)
            {
                status = null!;
                failure = exception.Message;
            }

            if (failure is not null)
            {
                yield return new EnvironmentCheck($"companion:{game}", "status", CheckStatus.Warn, failure);
                continue;
            }

            if (!status.GameFound)
            {
                yield return new EnvironmentCheck($"companion:{game}", "game", CheckStatus.Warn, "game not found in Steam libraries", "select the game folder on the Companion screen");
                continue;
            }

            yield return new EnvironmentCheck(
                $"companion:{game}",
                "mod",
                status.Issues.Count == 0 ? CheckStatus.Ok : CheckStatus.Warn,
                status.ModInstalled ? $"installed {status.Version}" : "not installed",
                string.Join("; ", status.Issues));
        }
    }

    private static IEnumerable<EnvironmentCheck> CheckDataDirectory()
    {
        var directory = AppPaths.DataDirectory;
        var probe = Path.Combine(directory, $".write-test-{Environment.ProcessId}");
        EnvironmentCheck result;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(probe, [1]);
            File.Delete(probe);
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
            result = new EnvironmentCheck(
                "app",
                "data folder",
                free < 200L * 1024 * 1024 ? CheckStatus.Warn : CheckStatus.Ok,
                string.Create(CultureInfo.InvariantCulture, $"{AppLog.Redact(directory)}, {free / (1024 * 1024)} MB free"),
                free < 200L * 1024 * 1024 ? "backups need free disk space" : string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            result = new EnvironmentCheck("app", "data folder", CheckStatus.Fail, exception.Message, "backups and drafts cannot be written");
        }

        yield return result;
    }

    private static IEnumerable<EnvironmentCheck> CheckKraken()
    {
        EnvironmentCheck result;
        try
        {
            var payload = Encoding.ASCII.GetBytes(new string('x', 4096) + "save editor kraken self-test");
            var packed = KrakenCodec.Compress(payload);
            var roundTrip = KrakenCodec.Decompress(packed, payload.Length);
            result = roundTrip.AsSpan().SequenceEqual(payload)
                ? new EnvironmentCheck("codecs", "kraken (S2)", CheckStatus.Ok, "native library loads and round-trips")
                : new EnvironmentCheck("codecs", "kraken (S2)", CheckStatus.Fail, "round-trip mismatch", "reinstall the application");
        }
        catch (Exception exception) when (exception is InvalidDataException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            result = new EnvironmentCheck("codecs", "kraken (S2)", CheckStatus.Fail, exception.Message, "S.T.A.L.K.E.R. 2 saves cannot be opened; reinstall the application");
        }

        yield return result;
    }

    private static void Safe(List<EnvironmentCheck> checks, string group, string name, Func<IEnumerable<EnvironmentCheck>> run)
    {
        try
        {
            checks.AddRange(run());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A broken check must not hide the others.
            checks.Add(new EnvironmentCheck(group, name, CheckStatus.Fail, exception.GetType().Name + ": " + exception.Message));
        }
    }
}
