using System.Net.Http.Headers;
using System.Text.Json;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Diagnostics;

/// <summary>
/// Sends the redacted diagnostics bundle to the project's report intake
/// (<c>POST /diagnostics</c> of the downloads worker: gzip, at most 2 MB, rate-limited).
/// Reports are sent only after the user has seen the notice, then at most once a day and after a crash.
/// </summary>
public static class DiagnosticsUploader
{
    public static readonly Uri Endpoint = new("https://save-editor-downloads.save-editor.workers.dev/diagnostics");
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly string[] ModLogNames = ["save_editor_companion.log", "save_editor_companion.log.old"];

    public static bool IsDue(DateTime? lastSentUtc, DateTime nowUtc, bool crashPending) =>
        crashPending || lastSentUtc is null || nowUtc - lastSentUtc.Value >= Interval;

    /// <summary>Uploads a bundle; returns the report id the server gave it.</summary>
    public static async Task<string> SendAsync(byte[] bundle, HttpClient? client = null, Uri? endpoint = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var owned = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var content = new ByteArrayContent(bundle);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            using var response = await client.PostAsync(endpoint ?? Endpoint, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"report rejected: {(int)response.StatusCode} {body}");
            }

            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("report_id", out var id) ? id.GetString() ?? "?" : "?";
        }
        finally
        {
            if (owned) client.Dispose();
        }
    }

    /// <summary>Where the experimental S.T.A.L.K.E.R. 2 mod writes its log (native Windows and Proton prefixes).</summary>
    public static IReadOnlyList<string> Stalker2ModLogs()
    {
        var saved = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (OperatingSystem.IsWindows() && localAppData.Length > 0) saved.Add(Path.Combine(localAppData, "Stalker2", "Saved"));
        try
        {
            foreach (var library in SteamLibraryFolderLocator.GetLibraries(CompanionInstaller.GetDefaultSteamRoots()))
            {
                saved.Add(Path.Combine(library, "steamapps", "compatdata", Stalker2CompanionInstaller.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "pfx", "drive_c", "users", "steamuser", "AppData", "Local", "Stalker2", "Saved"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return saved.SelectMany(directory => ModLogNames.Select(name => Path.Combine(directory, name)))
            .Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
    }
}
