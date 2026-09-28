using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>Existing save folders of every supported game on this machine (the Core locator: Steam libraries, fsgame.ltx, Proton, EE, S2).</summary>
public static class SaveDirectoryDiscovery
{
    public static IReadOnlyList<string> GetExistingDirectories()
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return SaveDirectoryLocator.FindCandidateDirectories()
            .Select(candidate => candidate.DirectoryPath)
            .Where(Directory.Exists)
            .Where(path => seen.Add(SaveSlotDiscovery.ResolveLinks(path))) // ~/.steam/steam is a link
            .ToArray();
    }
}
