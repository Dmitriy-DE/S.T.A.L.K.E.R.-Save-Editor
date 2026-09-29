using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Patching;

public sealed record ToolkitProfile(
    string Id,
    string Name,
    GameTarget Target,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<string> FixIds,
    bool CompanionInstalled,
    string? UserLtxPath,
    IReadOnlyDictionary<string, string> UserLtxOverrides);

/// <summary>Stores named, explicit managed states and switches between them through recovery snapshots.</summary>
public sealed class ToolkitProfileService
{
    private const int SchemaVersion = 1;
    private readonly string _directory;
    private readonly GameFixEngine _engine;
    private readonly IReadOnlyDictionary<string, GameFixDefinition> _definitions;
    private readonly CompanionInstaller? _companion;
    private readonly ToolkitSnapshotService _snapshots;
    private readonly string _configStateDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<GameTarget>() },
    };

    public ToolkitProfileService()
        : this(AppPaths.Profiles, new GameFixEngine(), GameFixCatalog.All.ToDictionary(fix => fix.Id, StringComparer.Ordinal),
            new CompanionInstaller(CompanionAssetLocator.ResolveSourceRoot()), new ToolkitSnapshotService(), AppPaths.ToolkitConfig)
    {
    }

    internal ToolkitProfileService(
        string directory,
        GameFixEngine engine,
        IReadOnlyDictionary<string, GameFixDefinition> definitions,
        CompanionInstaller? companion,
        ToolkitSnapshotService snapshots,
        string? configStateDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(directory));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _companion = companion;
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _configStateDirectory = Path.GetFullPath(configStateDirectory ?? AppPaths.ToolkitConfig);
    }

    public IReadOnlyList<ToolkitProfile> List()
    {
        if (!Directory.Exists(_directory)) return [];
        RejectLink(_directory);
        return Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(Read)
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public ToolkitProfile SaveCurrent(string name, GameTarget target, string gameDirectory, string? userLtxPath = null)
    {
        name = NormalizeName(name);
        var root = CanonicalDirectory(gameDirectory);
        if (!GameTargetCatalog.Get(target).IsXRay)
            throw new NotSupportedException("Toolkit profiles currently support X-Ray managed providers only.");

        var installed = _engine.ListInstalled(root);
        var drifted = installed.FirstOrDefault(fix => fix.State != GameFixState.Installed);
        if (drifted is not null) throw new InvalidOperationException($"Game Fix {drifted.Id} has drifted; resolve it before saving a profile.");
        foreach (var fix in installed)
            if (!_definitions.TryGetValue(fix.Id, out var definition) || definition.Game != target)
                throw new InvalidDataException($"Installed Game Fix {fix.Id} is not in the selected target's catalogue.");

        var companionInstalled = ReadCompanionState(target, root);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? canonicalUserLtxPath = null;
        if (userLtxPath is not null)
        {
            var inspection = ManagedUserLtxSettings.Inspect(userLtxPath, _configStateDirectory);
            if (inspection.HasConflict) throw new InvalidOperationException(inspection.Conflict ?? "Managed user.ltx settings need review.");
            canonicalUserLtxPath = inspection.FilePath;
            foreach (var setting in inspection.Settings.Where(setting => setting.ChangedByToolkit && setting.CurrentValue is not null))
                overrides.Add(setting.Key, setting.CurrentValue!);
        }

        var id = ProfileId(name);
        var profilePath = ProfilePath(id);
        var created = File.Exists(profilePath) ? Read(profilePath).CreatedUtc : DateTime.UtcNow;
        var profile = new ToolkitProfile(id, name, target, created, DateTime.UtcNow,
            installed.Select(fix => fix.Id).Order(StringComparer.Ordinal).ToArray(), companionInstalled, canonicalUserLtxPath, overrides);
        Persist(profile);
        return profile;
    }

    public ToolkitSnapshotRestoreResult Apply(string id, GameTarget target, string gameDirectory, string? userLtxPath = null)
    {
        var profile = Get(id);
        if (profile.Target != target) throw new InvalidOperationException("Choose the game this profile was created for.");
        var configPath = userLtxPath ?? profile.UserLtxPath;
        if (profile.UserLtxOverrides.Count > 0 && configPath is null)
            throw new ArgumentException("Select the game's existing user.ltx before applying this profile's config overrides.", nameof(userLtxPath));
        ValidateProfile(profile);
        return _snapshots.ApplyManagedState(profile.Target, gameDirectory, profile.FixIds,
            profile.CompanionInstalled, configPath, profile.UserLtxOverrides);
    }

    public ToolkitProfile Get(string id)
    {
        ValidateId(id);
        RejectLink(_directory);
        var profile = Read(ProfilePath(id));
        if (!string.Equals(profile.Id, id, StringComparison.Ordinal)) throw new InvalidDataException("Profile id does not match its metadata path.");
        ValidateProfile(profile);
        return profile;
    }

    public void Delete(string id)
    {
        ValidateId(id);
        var path = ProfilePath(id);
        if (!File.Exists(path)) return;
        _ = Get(id);
        File.Delete(path);
    }

    private bool ReadCompanionState(GameTarget target, string gameRoot)
    {
        var manifest = Path.Combine(gameRoot, ".save-editor-companion", "manifest.json");
        var marker = Path.Combine(gameRoot, "gamedata", "scripts", "save_editor_companion.script");
        if (!File.Exists(manifest))
        {
            if (File.Exists(marker)) throw new InvalidOperationException("Companion files exist without a verifiable manifest; they cannot be captured in a profile.");
            return false;
        }
        if (_companion is null) throw new NotSupportedException("Companion profile support is unavailable because its provider is not configured.");
        var game = ToCompanionGame(target);
        var status = _companion.GetStatus(game, gameRoot);
        if (!status.ModInstalled || status.Issues.Any(issue => issue.Contains("changed or missing", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Companion installation has drifted; resolve it before saving a profile.");
        foreach (var file in _companion.GetManagedFileStatus(game, gameRoot))
            if (!file.Exists || !file.MatchesExpectedHash) throw new InvalidOperationException($"Companion file changed or is missing: {file.RelativePath}");
        return true;
    }

    private void ValidateProfile(ToolkitProfile profile)
    {
        if (profile is null || profile.FixIds is null || profile.UserLtxOverrides is null || profile.FixIds.Count > 256)
            throw new InvalidDataException("Profile metadata is incomplete.");
        _ = NormalizeName(profile.Name);
        _ = ManagedUserLtxSettings.NormalizeOverrides(profile.UserLtxOverrides);
        if (profile.UserLtxPath is not null && !string.Equals(Path.GetFileName(profile.UserLtxPath), "user.ltx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Profile config path is not an existing user.ltx path.");
        var distinct = profile.FixIds.ToHashSet(StringComparer.Ordinal);
        if (distinct.Count != profile.FixIds.Count) throw new InvalidDataException("Profile contains duplicate fix ids.");
        foreach (var id in distinct)
            if (!_definitions.TryGetValue(id, out var definition) || definition.Game != profile.Target)
                throw new InvalidDataException($"Profile contains an unknown or mismatched Game Fix: {id}");
        foreach (var id in distinct)
        {
            var definition = _definitions[id];
            if (definition.DependsOn.Any(dependency => !distinct.Contains(dependency)))
                throw new InvalidDataException($"Profile is missing a dependency required by {id}.");
        }
        if (profile.CompanionInstalled) _ = ToCompanionGame(profile.Target);
    }

    private void Persist(ToolkitProfile profile)
    {
        Directory.CreateDirectory(_directory);
        RejectLink(_directory);
        AtomicWrite(ProfilePath(profile.Id), JsonSerializer.SerializeToUtf8Bytes(new ProfileDocument(SchemaVersion, profile), JsonOptions));
    }

    private ToolkitProfile Read(string path)
    {
        RejectLink(path);
        var document = JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException("Profile metadata is empty.");
        if (document.SchemaVersion != SchemaVersion || document.Profile is null) throw new InvalidDataException("Unsupported or incomplete profile metadata.");
        if (!PathEquals(path, ProfilePath(document.Profile.Id))) throw new InvalidDataException("Profile id does not match its metadata path.");
        ValidateProfile(document.Profile);
        return document.Profile;
    }

    private string ProfilePath(string id) => Path.Combine(_directory, id + ".json");

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 64 || normalized.Any(char.IsControl)) throw new ArgumentException("Profile names must be 1–64 printable characters.", nameof(name));
        return normalized;
    }

    private static string ProfileId(string name) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant()))).ToLowerInvariant();

    private static void ValidateId(string id)
    {
        if (id.Length != 64 || id.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException("Invalid toolkit profile id.", nameof(id));
    }

    private static string CanonicalDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(path));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        return full;
    }

    private static CompanionGame ToCompanionGame(GameTarget target) => target switch
    {
        GameTarget.ShadowOfChernobyl => CompanionGame.ShadowOfChernobyl,
        GameTarget.ClearSky => CompanionGame.ClearSky,
        GameTarget.CallOfPripyat => CompanionGame.CallOfPripyat,
        _ => throw new NotSupportedException("Companion profiles are available for the original trilogy only."),
    };

    private static bool PathEquals(string left, string right) => string.Equals(
        SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(left)), SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to follow linked profile state: {path}");
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private sealed record ProfileDocument(int SchemaVersion, ToolkitProfile Profile);
}
