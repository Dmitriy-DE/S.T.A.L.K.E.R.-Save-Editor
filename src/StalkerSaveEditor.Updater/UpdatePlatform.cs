namespace StalkerSaveEditor.Updater;

/// <summary>
/// The one description of what the updater can run on and what a release publishes: platform, CPU and artifact kind
/// names, and the fixed table of manifest entries. Detection, manifest parsing and artifact selection all read it,
/// so an impossible combination (a Linux installer, an arm64 Windows build) is rejected where it enters instead of
/// being compared as free text further down.
/// </summary>
public static class UpdatePlatform
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string MacOS = "macos";

    public const string X64 = "x86_64";
    public const string Arm64 = "arm64";

    /// <summary>Artifact and installation kinds.</summary>
    public const string Portable = "portable";
    public const string Package = "package";
    public const string Installer = "installer";

    /// <summary>What a release publishes for macOS; an installed copy is an <see cref="AppBundle"/>.</summary>
    public const string DiskImage = "disk-image";

    /// <summary>Installation kinds only.</summary>
    public const string AppBundle = "app-bundle";
    public const string Development = "development";

    /// <summary>Every manifest entry a release may contain, and what it must describe.</summary>
    private static readonly Dictionary<string, (string Platform, string Architecture, string Kind)> Artifacts = new(StringComparer.Ordinal)
    {
        ["windows-x86_64"] = (Windows, X64, Portable),
        ["windows-installer-x86_64"] = (Windows, X64, Installer),
        ["linux-x86_64"] = (Linux, X64, Portable),
        ["linux-deb-amd64"] = (Linux, X64, Package),
        ["macos-arm64"] = (MacOS, Arm64, DiskImage),
        ["macos-x86_64"] = (MacOS, X64, DiskImage),
    };

    /// <summary>The manifest entry for a platform, CPU and artifact kind; null when no release has such an artifact.</summary>
    public static string? ArtifactKey(string platform, string architecture, string kind)
    {
        foreach (var (key, description) in Artifacts)
        {
            if (description.Platform == platform && description.Kind == kind &&
                (description.Architecture == architecture || platform != MacOS))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>True when the entry <paramref name="key"/> exists and may carry this CPU and kind.</summary>
    public static bool Describes(string key, string architecture, string kind) =>
        Artifacts.TryGetValue(key, out var description) && description.Architecture == architecture && description.Kind == kind;

    /// <summary>The artifact kind that updates an installation of this kind.</summary>
    public static string ArtifactKindFor(string platform, string installationKind) => (platform, installationKind) switch
    {
        (MacOS, _) => DiskImage,
        (Linux, Package) => Package,
        (Windows, Installer) => Installer,
        _ => Portable,
    };
}
