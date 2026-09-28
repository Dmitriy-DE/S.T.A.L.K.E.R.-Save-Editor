using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Steam;

internal sealed record SteamCloudSaveProfile(
    int AppId,
    string ReleaseId,
    string CloudPrefix,
    IReadOnlySet<string> Extensions)
{
    public bool TryNormalizeSavePath(string remotePath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return false;
        }

        var candidate = remotePath.Replace('\\', '/');
        var segments = candidate.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Contains(':') ||
            segment.Any(char.IsControl)))
        {
            return false;
        }

        if (!candidate.StartsWith(CloudPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leaf = candidate[CloudPrefix.Length..];
        if (leaf.Length == 0 || leaf.Contains('/') || !Extensions.Contains(Path.GetExtension(leaf)))
        {
            return false;
        }

        normalizedPath = candidate;
        return true;
    }

    public bool HasExpectedFormat(ReadOnlySpan<byte> data)
    {
        string formatId;
        try
        {
            formatId = XRayTrilogyReader.FromBytes(data).FormatId;
        }
        catch (XRayFormatException)
        {
            try
            {
                formatId = XRayEnhancedReader.FromBytes(data).FormatId;
            }
            catch (XRayFormatException)
            {
                return false;
            }
        }

        return string.Equals(formatId, ReleaseId, StringComparison.Ordinal);
    }
}

internal static class SteamCloudSaveProfiles
{
    private static readonly Dictionary<int, SteamCloudSaveProfile> Profiles =
        new Dictionary<int, SteamCloudSaveProfile>
        {
            [4500] = Profile(4500, "stalker-soc", "_appdata_/savedgames/", ".sav"),
            [20510] = Profile(20510, "stalker-cs", "_appdata_/savedgames/", ".sav"),
            [41700] = Profile(41700, "stalker-cop", "_appdata_/savedgames/", ".sav", ".scop"),
            [2427410] = Profile(2427410, "stalker-soc-ee", "STALKER Shadow of Chornobyl - EE/STEAM/savedgames/", ".sav"),
            [2427420] = Profile(2427420, "stalker-cs-ee", "STALKER Clear Sky - EE/STEAM/savedgames/", ".sav", ".scop", ".scs"),
            [2427430] = Profile(2427430, "stalker-cop-ee", "STALKER Call of Prypiat - EE/STEAM/savedgames/", ".sav", ".scop", ".scs"),
        };

    public static bool TryGet(int appId, out SteamCloudSaveProfile profile) =>
        Profiles.TryGetValue(appId, out profile!);

    private static SteamCloudSaveProfile Profile(
        int appId,
        string releaseId,
        string cloudPrefix,
        params string[] extensions) =>
        new(appId, releaseId, cloudPrefix, new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase));
}
