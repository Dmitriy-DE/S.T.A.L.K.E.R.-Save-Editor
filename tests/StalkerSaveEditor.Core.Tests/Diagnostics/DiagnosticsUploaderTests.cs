using System.IO.Compression;
using System.Net;
using System.Text;
using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

[Collection("AppLog")]
public sealed class DiagnosticsUploaderTests : IDisposable
{
    private readonly TemporaryAppLog _log = new();
    private readonly string _directory;

    public DiagnosticsUploaderTests()
    {
        _directory = _log.Directory;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => _log.Dispose();

    [Fact]
    public void Sends_once_a_day_and_right_after_a_crash()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(DiagnosticsUploader.IsDue(null, now, crashPending: false));
        Assert.False(DiagnosticsUploader.IsDue(now.AddHours(-3), now, crashPending: false));
        Assert.True(DiagnosticsUploader.IsDue(now.AddHours(-3), now, crashPending: true));
        Assert.True(DiagnosticsUploader.IsDue(now.AddDays(-1), now, crashPending: false));
    }

    [Fact]
    public async Task Posts_gzip_and_returns_the_report_id()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);

        var id = await DiagnosticsUploader.SendAsync([1, 2, 3], client, new Uri("https://example.test/diagnostics"));

        Assert.Equal("abc", id);
        Assert.Equal("application/gzip", handler.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, handler.Body);
    }

    [Fact]
    public async Task A_rejected_report_is_an_error_not_a_silent_success()
    {
        using var client = new HttpClient(new RecordingHandler(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAsync<HttpRequestException>(() => DiagnosticsUploader.SendAsync([1], client, new Uri("https://example.test/diagnostics")));
    }

    [Fact]
    public void The_bundle_carries_the_stalker2_mod_log_redacted()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var modLog = Path.Combine(_directory, "save_editor_companion.log");
        File.WriteAllText(modLog, $"error in give: {home}/x");

        using var gzip = new GZipStream(new MemoryStream(DiagnosticsBundle.Create(null, [modLog])), CompressionMode.Decompress);
        var text = new StreamReader(gzip, Encoding.UTF8).ReadToEnd();

        Assert.Contains("--- save_editor_companion.log ---", text, StringComparison.Ordinal);
        Assert.Contains("error in give", text, StringComparison.Ordinal);
        Assert.DoesNotContain(home + "/", text, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.Created) : HttpMessageHandler
    {
        public string? ContentType { get; private set; }
        public byte[]? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.Created ? "{\"report_id\":\"abc\"}" : "{\"error\":\"limit\"}") };
        }
    }
}
