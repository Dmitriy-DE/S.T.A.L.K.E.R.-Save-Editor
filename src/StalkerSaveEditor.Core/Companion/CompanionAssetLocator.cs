namespace StalkerSaveEditor.Core.Companion;

/// <summary>Finds the Companion payload beside packaged builds or up the development source tree.</summary>
public static class CompanionAssetLocator
{
    public static string ResolveSourceRoot(string? executableDirectory = null)
    {
        var start = Path.GetFullPath(executableDirectory ?? AppContext.BaseDirectory);
        if (File.Exists(start)) start = Path.GetDirectoryName(start)!;
        var first = Path.TrimEndingDirectorySeparator(start);
        var current = first;
        for (var depth = 0; depth < 10; depth++)
        {
            var candidate = Path.Combine(current, "mods", "companion");
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal)) break;
            current = parent;
        }

        return Path.Combine(first, "mods", "companion");
    }
}
