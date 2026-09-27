using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Core.Storage;

public sealed record SaveSlot(
    string Path,
    string CandidateGameId,
    string CandidateReleaseId,
    long Size,
    DateTime LastWriteTimeUtc,
    string? FormatId,
    string? GameId,
    string? DetectionError = null);

public sealed record SaveDiscoveryResult(
    IReadOnlyList<SaveSlot> Slots,
    IReadOnlyList<string> SearchedPaths);

public static class SaveSlotDiscovery
{
    private static readonly HashSet<string> SaveSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sav",
        ".scop",
        ".scs",
    };

    private static readonly HashSet<string> NonSlotFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "campaignssave.sav",
        "analyticsdata.sav",
    };

    public static SaveDiscoveryResult Discover(IEnumerable<SaveDirectoryCandidate> directoryCandidates)
    {
        ArgumentNullException.ThrowIfNull(directoryCandidates);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var searchedPaths = new List<string>();
        var searchedSet = new HashSet<string>(pathComparer);
        var slots = new List<SaveSlot>();
        var seenSlots = new HashSet<string>(pathComparer);

        foreach (var candidate in directoryCandidates)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.DirectoryPath))
            {
                continue;
            }

            string directory;
            try
            {
                directory = Path.GetFullPath(candidate.DirectoryPath);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                continue;
            }

            if (searchedSet.Add(directory))
            {
                searchedPaths.Add(directory);
            }

            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in paths)
            {
                if (!SaveSuffixes.Contains(Path.GetExtension(path)) || NonSlotFileNames.Contains(Path.GetFileName(path)))
                {
                    continue;
                }

                string fullPath;
                FileInfo file;
                try
                {
                    fullPath = Path.GetFullPath(path);
                    if (!seenSlots.Add(fullPath))
                    {
                        continue;
                    }

                    file = new FileInfo(fullPath);
                    if (!file.Exists)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    continue;
                }

                try
                {
                    var formatId = DetectFormat(File.ReadAllBytes(fullPath));
                    slots.Add(new SaveSlot(
                        fullPath,
                        candidate.GameId,
                        candidate.ReleaseId,
                        file.Length,
                        file.LastWriteTimeUtc,
                        formatId,
                        FamilyForFormat(formatId)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    slots.Add(new SaveSlot(
                        fullPath,
                        candidate.GameId,
                        candidate.ReleaseId,
                        file.Length,
                        file.LastWriteTimeUtc,
                        null,
                        null,
                        $"{exception.GetType().Name}: {exception.Message}"));
                }
            }
        }

        slots.Sort((left, right) =>
        {
            var modified = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
            return modified != 0 ? modified : pathComparer.Compare(left.Path, right.Path);
        });
        return new SaveDiscoveryResult(slots.AsReadOnly(), searchedPaths.AsReadOnly());
    }

    public static SaveDiscoveryResult Discover(SaveDirectoryDiscoveryOptions? options = null) =>
        Discover(SaveDirectoryLocator.FindCandidateDirectories(options));

    private static string? DetectFormat(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data).FormatId;
        }
        catch (XRayFormatException)
        {
            // Try the other registered formats by content.
        }

        try
        {
            return XRayEnhancedReader.FromBytes(data).FormatId;
        }
        catch (XRayFormatException)
        {
            // Try S.T.A.L.K.E.R. 2 after both X-Ray variants.
        }

        return Stalker2SaveReader.Detect(data) ? "stalker2" : null;
    }

    private static string? FamilyForFormat(string? formatId) => formatId switch
    {
        "stalker-soc" or "stalker-soc-ee" => "soc",
        "stalker-cs" or "stalker-cs-ee" => "clear_sky",
        "stalker-cop" or "stalker-cop-ee" => "cop",
        "stalker2" => "stalker2",
        _ => null,
    };
}
