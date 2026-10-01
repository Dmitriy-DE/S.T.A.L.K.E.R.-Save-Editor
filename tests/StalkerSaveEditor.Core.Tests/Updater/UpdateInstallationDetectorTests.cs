using StalkerSaveEditor.Updater;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Updater;

public sealed class UpdateInstallationDetectorTests
{
    [Fact]
    public void Detects_a_windows_portable_install_from_its_build_manifest()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "SaveEditor.exe");
        File.WriteAllText(executable, "synthetic executable");
        File.WriteAllText(
            Path.Combine(directory.Path, "BUILD_MANIFEST.json"),
            "{\"target\":\"windows\",\"architecture\":\"x86_64\",\"version\":\"1.0.0\"}");

        var installation = UpdateInstallationDetector.Detect(executable, "windows");

        Assert.Equal("windows", installation.Target);
        Assert.Equal("x86_64", installation.Architecture);
        Assert.Equal("portable", installation.Kind);
        Assert.Equal(directory.Path, installation.Root);
    }

    [Fact]
    public void Detects_a_linux_package_install_root()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "SaveEditor");
        File.WriteAllText(executable, "synthetic executable");

        var installation = UpdateInstallationDetector.Detect(executable, "linux", directory.Path);

        Assert.Equal("package", installation.Kind);
        Assert.Equal(directory.Path, installation.Root);
    }

    [Fact]
    public void Detects_a_macos_app_bundle_from_the_executable_path()
    {
        using var directory = new TemporaryDirectory();
        var bundle = Path.Combine(directory.Path, "SaveEditor.app");
        var executable = Path.Combine(bundle, "Contents", "MacOS", "SaveEditor");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
        File.WriteAllText(executable, "synthetic executable");
        File.WriteAllText(
            Path.Combine(bundle, "Contents", "Resources", "BUILD_MANIFEST.json"),
            "{\"target\":\"macos\",\"architecture\":\"arm64\"}");

        var installation = UpdateInstallationDetector.Detect(executable, "macos");

        Assert.Equal(bundle, installation.Root);
        Assert.Equal("app-bundle", installation.Kind);
        Assert.Equal("arm64", installation.Architecture);
    }

    [Fact]
    public void Rejects_a_build_manifest_for_a_different_platform()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "SaveEditor.exe");
        File.WriteAllText(executable, "synthetic executable");
        File.WriteAllText(Path.Combine(directory.Path, "BUILD_MANIFEST.json"), "{\"target\":\"linux\"}");

        Assert.Throws<UpdateManifestException>(() => UpdateInstallationDetector.Detect(executable, "windows"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"update-install-{Guid.NewGuid():N}");
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
