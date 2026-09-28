using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Updater;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Updater;

public sealed class UpdateServiceTests
{
    private const string ManifestUrl = "http://updates.test/latest.json";
    private static readonly byte[] ReleaseBytes = "verified release archive"u8.ToArray();

    [Fact]
    public async Task Check_reports_current_and_available_and_selects_the_requested_artifact()
    {
        var payload = CreateManifest("1.2.0", ReleaseBytes);
        var service = CreateService(new FakeHandler(_ => JsonResponse(payload)), currentVersion: "1.2.0");

        var current = await service.CheckAsync();
        var availableService = CreateService(new FakeHandler(_ => JsonResponse(payload)), currentVersion: "1.1.9");
        var available = await availableService.CheckAsync();

        Assert.Equal(UpdateState.Current, current.State);
        Assert.Equal(UpdateState.Available, available.State);
        Assert.Equal("SaveEditor-windows-x86_64.zip", available.Artifact!.File);
        Assert.Equal(ReleaseBytes.LongLength, available.Artifact.Size);
    }

    [Fact]
    public async Task Check_rejects_a_redirect_before_requesting_an_untrusted_host()
    {
        var requests = new List<Uri>();
        var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://evil.test/latest.json") },
            };
        });
        var service = CreateService(handler);

        var result = await service.CheckAsync();

        Assert.Equal(UpdateState.Invalid, result.State);
        Assert.Single(requests);
        Assert.Equal("updates.test", requests[0].Host);
    }

    [Fact]
    public async Task Download_reuses_verified_bytes_and_rejects_changed_server_content()
    {
        var manifest = CreateManifest("1.2.0", ReleaseBytes);
        var artifactRequests = 0;
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/latest.json")
            {
                return JsonResponse(manifest);
            }

            artifactRequests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ReleaseBytes) };
        });
        using var service = CreateService(handler);
        var artifact = (await service.CheckAsync()).Artifact!;
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "update.zip");

        var first = await service.DownloadAsync(artifact, destination);
        var reused = await service.DownloadAsync(artifact, destination);

        Assert.Equal(destination, first);
        Assert.Equal(first, reused);
        Assert.Equal(1, artifactRequests);
        Assert.Equal(ReleaseBytes, await File.ReadAllBytesAsync(destination));

        await File.WriteAllBytesAsync(destination, "corrupt"u8.ToArray());
        handler.Replace(request => request.RequestUri!.AbsolutePath == "/latest.json"
            ? JsonResponse(manifest)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Enumerable.Repeat((byte)'x', ReleaseBytes.Length).ToArray()),
            });

        await Assert.ThrowsAsync<UpdateManifestException>(() => service.DownloadAsync(artifact, destination));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.part"));
    }

    [Fact]
    public async Task Download_rejects_a_body_larger_than_the_manifest_size()
    {
        var manifest = CreateManifest("1.2.0", ReleaseBytes);
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/latest.json"
            ? JsonResponse(manifest)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([.. ReleaseBytes, 0]),
            });
        using var service = CreateService(handler);
        var artifact = (await service.CheckAsync()).Artifact!;
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "update.zip");

        await Assert.ThrowsAsync<UpdateManifestException>(() => service.DownloadAsync(artifact, destination));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task Check_marks_an_invalid_manifest_without_throwing()
    {
        using var service = CreateService(new FakeHandler(_ => JsonResponse("{}")));

        var result = await service.CheckAsync();

        Assert.Equal(UpdateState.Invalid, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    public async Task Installer_handoff_reports_pkexec_cancel_codes_without_retry(int exitCode)
    {
        var archiveBytes = "verified Debian package"u8.ToArray();
        var artifact = new UpdateArtifact(
            "linux-deb-amd64",
            "x86_64",
            "package",
            "editor.deb",
            archiveBytes.LongLength,
            Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant(),
            "https://updates.test/editor.deb");
        using var directory = new TemporaryDirectory();
        var archive = Path.Combine(directory.Path, artifact.File);
        await File.WriteAllBytesAsync(archive, archiveBytes);
        var processRunner = new FakeUpdateProcessRunner(exitCode);
        using var service = CreateService(new FakeHandler(_ => JsonResponse(CreateManifest("1.2.0", ReleaseBytes))), processRunner: processRunner);
        var installation = new UpdateInstallation("linux", "x86_64", "package", directory.Path, Path.Combine(directory.Path, "SaveEditor"));
        var progress = new ImmediateProgress<UpdateProgress>([]);

        var result = await service.InstallAsync(artifact, archive, installation, progress);

        Assert.Equal(UpdateInstallState.Cancelled, result.State);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(1, processRunner.CallCount);
        Assert.Equal("/usr/bin/pkexec", processRunner.LastStartInfo!.FileName);
        Assert.Equal("/usr/bin/apt-get", processRunner.LastStartInfo.ArgumentList[0]);
        Assert.Contains(archive, processRunner.LastStartInfo.ArgumentList);
        Assert.Contains(progress.Items, item => item.Stage == "install");
    }

    [Fact]
    public async Task Windows_installer_handoff_runs_only_after_verifying_the_artifact()
    {
        var body = "verified Windows installer"u8.ToArray();
        var artifact = new UpdateArtifact(
            "windows-installer-x86_64",
            "x86_64",
            "installer",
            "setup.exe",
            body.LongLength,
            Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            "http://updates.test/setup.exe");
        using var directory = new TemporaryDirectory();
        var archive = Path.Combine(directory.Path, artifact.File);
        await File.WriteAllBytesAsync(archive, body);
        var processRunner = new FakeUpdateProcessRunner(0);
        using var service = CreateService(
            new FakeHandler(_ => JsonResponse(CreateManifest("1.2.0", ReleaseBytes))),
            target: "windows",
            kind: "installer",
            processRunner: processRunner);

        var result = await service.InstallAsync(
            artifact,
            archive,
            new UpdateInstallation("windows", "x86_64", "installer", directory.Path, archive));

        Assert.Equal(UpdateInstallState.Succeeded, result.State);
        Assert.Equal(archive, processRunner.LastStartInfo!.FileName);
        Assert.False(processRunner.LastStartInfo.UseShellExecute);
    }

    [Fact]
    public async Task Install_refuses_tampered_local_bytes_before_starting_a_process()
    {
        var body = "verified Windows installer"u8.ToArray();
        var artifact = new UpdateArtifact(
            "windows-installer-x86_64",
            "x86_64",
            "installer",
            "setup.exe",
            body.LongLength,
            Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            "http://updates.test/setup.exe");
        using var directory = new TemporaryDirectory();
        var archive = Path.Combine(directory.Path, artifact.File);
        await File.WriteAllBytesAsync(archive, Enumerable.Repeat((byte)'x', body.Length).ToArray());
        var processRunner = new FakeUpdateProcessRunner(0);
        using var service = CreateService(
            new FakeHandler(_ => JsonResponse(CreateManifest("1.2.0", ReleaseBytes))),
            target: "windows",
            kind: "installer",
            processRunner: processRunner);

        var result = await service.InstallAsync(
            artifact,
            archive,
            new UpdateInstallation("windows", "x86_64", "installer", directory.Path, archive));

        Assert.Equal(UpdateInstallState.Failed, result.State);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Install_returns_cancelled_when_the_caller_cancels()
    {
        var body = "verified Windows installer"u8.ToArray();
        var artifact = new UpdateArtifact(
            "windows-installer-x86_64",
            "x86_64",
            "installer",
            "setup.exe",
            body.LongLength,
            Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            "http://updates.test/setup.exe");
        using var directory = new TemporaryDirectory();
        var archive = Path.Combine(directory.Path, artifact.File);
        await File.WriteAllBytesAsync(archive, body);
        var processRunner = new FakeUpdateProcessRunner(0);
        using var service = CreateService(
            new FakeHandler(_ => JsonResponse(CreateManifest("1.2.0", ReleaseBytes))),
            target: "windows",
            kind: "installer",
            processRunner: processRunner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.InstallAsync(
            artifact,
            archive,
            new UpdateInstallation("windows", "x86_64", "installer", directory.Path, archive),
            cancellationToken: cancellation.Token);

        Assert.Equal(UpdateInstallState.Cancelled, result.State);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3-rc.1", "1.2.3", -1)]
    [InlineData("1.2.3", "1.2.3-rc.1", 1)]
    [InlineData("1.2.3-rc.2", "1.2.3-rc.10", -1)]
    public void Version_comparison_matches_semver_precedence(string left, string right, int expected)
    {
        Assert.Equal(expected, UpdateService.CompareVersions(left, right));
    }

    [Fact]
    public void Service_startup_removes_only_old_per_process_update_files()
    {
        using var directory = new TemporaryDirectory();
        var stale = Path.Combine(directory.Path, "SaveEditor-update-554654-editor.deb");
        var current = Path.Combine(directory.Path, "SaveEditor-update-editor.deb");
        var unrelated = Path.Combine(directory.Path, "unrelated.deb");
        File.WriteAllText(stale, "stale");
        File.WriteAllText(current, "current");
        File.WriteAllText(unrelated, "unrelated");

        using var service = CreateService(
            new FakeHandler(_ => JsonResponse(CreateManifest("1.2.0", ReleaseBytes))),
            staleDownloadDirectory: directory.Path);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(unrelated));
    }

    [Theory]
    [InlineData("windows", "portable")]
    [InlineData("windows", "installer")]
    [InlineData("linux", "portable")]
    [InlineData("linux", "package")]
    public async Task Accepts_the_manifest_written_by_the_release_tool(string target, string kind)
    {
        // tests/Fixtures/release/latest.json is produced by tools/release/publish_release.py.
        var manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "release", "latest.json"));
        using var service = new UpdateService(
            "0.9.0",
            "https://save-editor-downloads.save-editor.workers.dev/latest.json",
            target,
            "x86_64",
            kind,
            new FakeHandler(_ => JsonResponse(manifest)),
            ["save-editor-downloads.save-editor.workers.dev"],
            ["https"],
            Path.Combine(Path.GetTempPath(), $"updater-cache-{Guid.NewGuid():N}"),
            static _ => null,
            new FakeUpdateProcessRunner(0));

        var result = await service.CheckAsync();

        Assert.NotNull(result.Artifact);
        Assert.Equal("1.0.0", result.Manifest!.Version);
        Assert.Null(result.Error);
    }

    private static UpdateService CreateService(
        FakeHandler handler,
        string currentVersion = "1.0.0",
        FakeUpdateProcessRunner? processRunner = null,
        string? staleDownloadDirectory = null,
        string target = "windows",
        string kind = "portable") =>
        new(
            currentVersion,
            ManifestUrl,
            target,
            "x86_64",
            kind,
            handler,
            ["updates.test"],
            ["http", "https"],
            staleDownloadDirectory ?? Path.Combine(Path.GetTempPath(), $"updater-cache-{Guid.NewGuid():N}"),
            static name => name switch
            {
                "pkexec" => "/usr/bin/pkexec",
                "apt-get" => "/usr/bin/apt-get",
                _ => null,
            },
            processRunner ?? new FakeUpdateProcessRunner(0));

    private static string CreateManifest(string version, byte[] body)
    {
        var artifacts = new Dictionary<string, object>
        {
            ["windows-x86_64"] = Artifact("windows-x86_64", "portable", "SaveEditor-windows-x86_64.zip", body),
            ["linux-x86_64"] = Artifact("linux-x86_64", "portable", "SaveEditor-linux-x86_64.tar.gz", body),
            ["linux-deb-amd64"] = Artifact("linux-deb-amd64", "package", "editor.deb", body),
        };
        return JsonSerializer.Serialize(new
        {
            schema = 1,
            channel = "stable",
            version,
            source_commit = new string('a', 40),
            published_at = "2026-09-26T12:00:00Z",
            artifacts,
        });
    }

    private static object Artifact(string target, string kind, string file, byte[] body) => new
    {
        target,
        architecture = "x86_64",
        kind,
        file,
        size = body.LongLength,
        sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
        url = $"http://updates.test/{file}",
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage> _send = send;

        public void Replace(Func<HttpRequestMessage, HttpResponseMessage> send) => _send = send;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(_send(request));
    }

    private sealed class FakeUpdateProcessRunner(int exitCode) : IUpdateProcessRunner
    {
        public int CallCount { get; private set; }

        public ProcessStartInfo? LastStartInfo { get; private set; }

        public Task<int> RunAsync(
            ProcessStartInfo startInfo,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastStartInfo = startInfo;
            progress?.Report(new UpdateProgress("install", "Installing package"));
            return Task.FromResult(exitCode);
        }
    }

    private sealed class ImmediateProgress<T>(ICollection<T> items) : IProgress<T>
    {
        public ICollection<T> Items { get; } = items;

        public void Report(T value) => Items.Add(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"updater-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
