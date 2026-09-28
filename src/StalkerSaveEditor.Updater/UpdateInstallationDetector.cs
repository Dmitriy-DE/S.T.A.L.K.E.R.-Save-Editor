using System.Text.Json;
using System.Runtime.InteropServices;

namespace StalkerSaveEditor.Updater;

/// <summary>Identifies the installation around the running executable without changing files.</summary>
public static class UpdateInstallationDetector
{
    public const string LinuxPackageInstallRoot = "/usr/lib/stalker-save-editor";

    public static UpdateInstallation Detect(string? executablePath = null, string? platformName = null) =>
        Detect(executablePath, platformName, LinuxPackageInstallRoot);

    internal static UpdateInstallation Detect(
        string? executablePath,
        string? platformName,
        string packageInstallRoot)
    {
        var platform = (platformName ?? (OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "macos"
                : "linux")).ToLowerInvariant();
        var target = platform switch
        {
            "win32nt" or "windows" => "windows",
            "darwin" or "macos" => "macos",
            "unix" or "linux" => "linux",
            _ => throw new UpdateManifestException($"Unsupported update platform: {platformName ?? platform}.")
        };

        var path = Path.GetFullPath(executablePath ?? Environment.ProcessPath
            ?? throw new UpdateManifestException("Running application executable path is unavailable."));
        try
        {
            path = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            // Missing or inaccessible link metadata does not make a development install unsafe to inspect.
        }

        var appBundle = target == "macos"
            ? GetAncestorsAndSelf(new DirectoryInfo(Path.GetDirectoryName(path)!))
                .FirstOrDefault(parent => parent.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase))
            : null;
        var root = appBundle?.FullName ?? Path.GetDirectoryName(path)!;
        var manifestPath = appBundle is null
            ? Path.Combine(root, "BUILD_MANIFEST.json")
            : Path.Combine(root, "Contents", "Resources", "BUILD_MANIFEST.json");
        var architecture = target == "macos" && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "arm64"
            : "x86_64";

        if (target == "linux" && PathsEqual(root, packageInstallRoot))
        {
            return new UpdateInstallation(target, architecture, "package", root, path);
        }

        if (!File.Exists(manifestPath))
        {
            return new UpdateInstallation(target, architecture, "development", root, path);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var manifest = document.RootElement;
            if (!manifest.TryGetProperty("target", out var targetElement)
                || targetElement.GetString() != target)
            {
                throw new UpdateManifestException("Packaged build target does not match the current platform.");
            }

            if (manifest.TryGetProperty("architecture", out var architectureElement)
                && architectureElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(architectureElement.GetString()))
            {
                architecture = architectureElement.GetString()!;
            }

            var kind = target == "windows" && File.Exists(Path.Combine(root, "INSTALLER_MARKER"))
                ? "installer"
                : "portable";
            return new UpdateInstallation(target, architecture, kind, root, path);
        }
        catch (UpdateManifestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw new UpdateManifestException($"Packaged build manifest is invalid: {exception.Message}", exception);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    private static IEnumerable<DirectoryInfo> GetAncestorsAndSelf(DirectoryInfo directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }
}
