using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>User preferences kept in <c>settings.json</c> in the data folder. Null folders mean "detect automatically".</summary>
public sealed record AppSettings
{
    public List<string>? SaveDirectories { get; init; }
    public string? BackupDirectory { get; init; }
    /// <summary>Null until chosen: the system language is used.</summary>
    public string? Language { get; init; }
    public bool SoundEnabled { get; init; } = true;
    public int SoundVolume { get; init; } = 80;
    public bool MusicEnabled { get; init; }

    /// <summary>Visual preferences are applied live; ids keep the settings stable across languages.</summary>
    public string ThemeId { get; init; } = "zone";
    public string AccentId { get; init; } = "amber";
    /// <summary>0 means "fit the screen": the window picks a scale from the display it opens on.</summary>
    public int UiScalePercent { get; init; }

    /// <summary>The navigation is folded to icons; null until the user folds or unfolds it.</summary>
    public bool? NavigationCollapsed { get; init; }

    /// <summary>Send the redacted log to the developer once a day and after a crash.</summary>
    public bool SendReports { get; init; } = true;

    /// <summary>The user has seen the notice about reports; nothing is sent before that.</summary>
    public bool ReportsNoticeShown { get; init; }

    public DateTime? LastReportUtc { get; init; }

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    /// <summary>Reads the file; a missing or damaged file gives defaults (a damaged one is logged, never overwritten silently).</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Warn("settings not read, defaults used", exception);
            return new AppSettings();
        }
    }

    /// <summary>
    /// Atomic and durable: a unique temp file flushed to disk, then renamed. Two windows saving at once no longer
    /// share one ".tmp", and a crash mid-write leaves the previous settings instead of a truncated file.
    /// </summary>
    public void Save(string path) =>
        Core.Storage.AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, AppSettingsJson.Default.AppSettings));
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJson : JsonSerializerContext;
