namespace StalkerSaveEditor.Steam;

public static class SteamAutoCloudRootLocator
{
    public const int Stalker2AppId = 1643320;
    public const string Stalker2DirectoryName = "Stalker2";

    public static string? FindRoot(int appId)
    {
        var libraries = OperatingSystem.IsWindows()
            ? Array.Empty<string>()
            : SteamLibraryLocator.GetSteamLibraryRoots();
        return FindRoot(
            appId,
            OperatingSystem.IsWindows(),
            Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            libraries);
    }

    internal static string? FindRoot(
        int appId,
        bool isWindows,
        string? windowsLocalAppData,
        IEnumerable<string> steamLibraries)
    {
        ArgumentNullException.ThrowIfNull(steamLibraries);
        if (appId != Stalker2AppId)
        {
            return null;
        }

        if (isWindows)
        {
            return IsAutoCloudRoot(windowsLocalAppData) ? Path.GetFullPath(windowsLocalAppData!) : null;
        }

        foreach (var library in steamLibraries)
        {
            var usersRoot = Path.Combine(
                library,
                "steamapps",
                "compatdata",
                appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "pfx",
                "drive_c",
                "users");
            if (!Directory.Exists(usersRoot))
            {
                continue;
            }

            foreach (var userRoot in Directory.EnumerateDirectories(usersRoot))
            {
                var candidates = new[]
                {
                    Path.Combine(userRoot, "Local Settings", "Application Data"),
                    Path.Combine(userRoot, "AppData", "Local"),
                };
                var found = candidates.FirstOrDefault(IsAutoCloudRoot);
                if (found is not null)
                {
                    return Path.GetFullPath(found);
                }
            }
        }

        return null;
    }

    public static string ResolveLocalPath(string root, string remoteName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteName);
        var normalized = remoteName.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Length < 2 ||
            !string.Equals(segments[0], Stalker2DirectoryName, StringComparison.Ordinal) ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Contains(':') ||
                segment.Any(char.IsControl)))
        {
            throw new ArgumentException("Auto-Cloud path must be a safe Stalker2/ relative file name.", nameof(remoteName));
        }

        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine([fullRoot, .. segments]));
        var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            throw new ArgumentException("Auto-Cloud path escapes its local root.", nameof(remoteName));
        }

        return candidate;
    }

    private static bool IsAutoCloudRoot(string? root) =>
        !string.IsNullOrWhiteSpace(root) && Directory.Exists(Path.Combine(root, Stalker2DirectoryName));
}
