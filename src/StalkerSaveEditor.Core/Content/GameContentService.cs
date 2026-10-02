using System.Collections.Concurrent;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;

namespace StalkerSaveEditor.Core.Content;

/// <summary>What was read from an installed game and whether it came from the cache.</summary>
public sealed record GameContentStatus(
    string ReleaseId,
    string GameDirectory,
    string Fingerprint,
    bool FromCache,
    bool HasLooseOverlay,
    string? ModName,
    int ItemCount,
    int UpgradeCount,
    int FactionCount,
    IReadOnlyList<string> Issues);

/// <summary>Catalog built from the user's own game install plus lazily cropped item icons.</summary>
public sealed class GameContent
{
    private readonly Func<string, byte[]?> _icon;

    internal GameContent(CatalogBundle bundle, GameContentStatus status, Func<string, byte[]?> icon)
    {
        Bundle = bundle;
        Status = status;
        _icon = icon;
    }

    public CatalogBundle Bundle { get; }

    public GameContentStatus Status { get; }

    /// <summary>PNG of the item's inventory icon cropped from the game's atlas, or null when unavailable.</summary>
    public byte[]? IconPng(string itemKey) => _icon(itemKey);
}

/// <summary>
/// Content packs from the installed game (D20): item/upgrade/community catalog and icons read
/// from the game's archives and loose gamedata exactly as the engine sees them, cached by a
/// fingerprint of the install so a rebuild happens only after the game or a mod changes.
/// </summary>
public static class GameContentService
{
    private const int IconCellSize = 50;

    /// <summary>Bumped whenever the builder output changes so old caches are ignored.</summary>
    private const int BuilderVersion = 5;
    private static readonly string[] ModMarkers = ["ogsm", "srp", "anomaly", "misery", "complete", "gunslinger", "amk"];

    public static string ReleaseIdFor(CompanionGame game) => game switch
    {
        CompanionGame.ShadowOfChernobyl => "stalker-soc",
        CompanionGame.ClearSky => "stalker-cs",
        CompanionGame.CallOfPripyat => "stalker-cop",
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    /// <summary>Loads the cached content for this install or builds and caches it; null when the game has no readable catalog.</summary>
    public static GameContent? Load(CompanionGame game, string gameDirectory, string cacheDirectory, string uiLanguage = "ru")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        var releaseId = ReleaseIdFor(game);
        // First only the archive tables: enough for the fingerprint, and on a cache hit nothing else is needed
        // (unpacking every config and texture took a few hundred ms and tens of MiB per game on each start).
        var index = GameFileTree.Load(game, gameDirectory, IsWanted, deferArchiveContent: true);
        var tree = index;
        var modName = DetectMod(tree);
        var cacheRoot = Path.Combine(cacheDirectory, $"{releaseId}-v{BuilderVersion}-{tree.Fingerprint[..32]}");
        var catalogPath = Path.Combine(cacheRoot, $"catalog-{SafeLanguage(uiLanguage)}.json");

        CatalogBundle? bundle = null;
        var fromCache = false;
        if (File.Exists(catalogPath))
        {
            try
            {
                bundle = CatalogBundleReader.Load(File.ReadAllBytes(catalogPath)).GetValueOrDefault(releaseId);
                fromCache = bundle is not null;
            }
            catch (Exception exception) when (exception is CatalogBundleException or IOException)
            {
                bundle = null;
            }
        }

        if (bundle is null)
        {
            // The catalogue has to be built: now the contents are read, all at once per archive.
            tree = GameFileTree.Load(game, gameDirectory, IsWanted);
            index = tree.Fingerprint == index.Fingerprint
                ? index
                : GameFileTree.Load(game, gameDirectory, IsWanted, deferArchiveContent: true);
            cacheRoot = Path.Combine(cacheDirectory, $"{releaseId}-v{BuilderVersion}-{tree.Fingerprint[..32]}");
            catalogPath = Path.Combine(cacheRoot, $"catalog-{SafeLanguage(uiLanguage)}.json");
        }

        var issues = new List<string>(tree.Issues);
        if (bundle is null)
        {
            var sections = LtxDocument.ParseIncludeGraph(tree.ConfigPrefix + "system.ltx", tree.Files);
            if (sections is null)
            {
                // No system.ltx: fall back to every LTX file of the config tree.
                issues.Add("system.ltx was not found; the catalog was read from every LTX file.");
                sections = new Dictionary<string, LtxSection>(StringComparer.Ordinal);
                foreach (var file in tree.Files.Values
                             .Where(file => file.RelativePath.StartsWith(tree.ConfigPrefix, StringComparison.OrdinalIgnoreCase) &&
                                            file.RelativePath.EndsWith(".ltx", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var (name, section) in LtxDocument.Parse(LtxDocument.Decode(file.Read()), file.RelativePath))
                    {
                        sections[name] = section;
                    }
                }
            }

            var strings = XRayStringTables.Read(
                tree.Files.Values.Where(file => file.RelativePath.StartsWith(tree.ConfigPrefix, StringComparison.OrdinalIgnoreCase)).ToArray(),
                uiLanguage);
            try
            {
                bundle = InstalledGameCatalogBuilder.Build(releaseId, sections, strings);
            }
            catch (CatalogBundleException exception)
            {
                issues.Add($"Catalog from the game files is inconsistent: {exception.Message}");
                bundle = null;
            }

            if (bundle is null) return null;
            try
            {
                Directory.CreateDirectory(cacheRoot);
                Storage.AtomicFile.WriteAllBytes(catalogPath, CatalogBundleWriter.Write([bundle]));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add($"Could not write the content cache: {exception.Message}");
            }
        }

        var status = new GameContentStatus(
            releaseId,
            gameDirectory,
            tree.Fingerprint,
            fromCache,
            tree.HasLooseOverlay,
            modName,
            bundle.Items.Items.Count,
            bundle.Upgrades?.Upgrades.Count ?? 0,
            bundle.Factions?.Factions.Count ?? 0,
            issues.AsReadOnly());
        // Icons read their atlas on demand from the index, so the unpacked files of a build are not kept alive.
        var icons = new IconSource(index, bundle, Path.Combine(cacheRoot, "icons"));
        return new GameContent(WithFallbackNames(bundle, uiLanguage), status, icons.Png);
    }

    /// <summary>
    /// Keeps the game's own names, but when the install has no text in the UI language (the Steam
    /// SoC ships English only) or no name at all, uses the name from the embedded official catalog.
    /// </summary>
    private static CatalogBundle WithFallbackNames(CatalogBundle bundle, string uiLanguage)
    {
        if (!CatalogBundleReader.LoadEmbedded().TryGetValue(bundle.ReleaseId, out var embedded)) return bundle;
        var wantCyrillic = uiLanguage is "ru" or "uk";
        var changed = false;
        var items = bundle.Items.Items.Select(item =>
        {
            var fallback = embedded.Items.Resolve(item.Key)?.DisplayName;
            if (fallback is null) return item;
            var missing = string.IsNullOrWhiteSpace(item.DisplayName);
            var wrongScript = wantCyrillic && !HasCyrillic(item.DisplayName) && HasCyrillic(fallback);
            if (!missing && !wrongScript) return item;
            changed = true;
            return item.WithDisplayName(fallback);
        }).ToArray();
        return changed ? bundle.With(new ItemCatalog(bundle.ReleaseId, items)) : bundle;
    }

    private static bool HasCyrillic(string? value) =>
        value is not null && value.Any(character => character is >= '\u0400' and <= '\u04FF');

    private static bool IsWanted(string relativePath)
    {
        var lower = relativePath.ToLowerInvariant();
        return ((lower.StartsWith("config/", StringComparison.Ordinal) || lower.StartsWith("configs/", StringComparison.Ordinal)) &&
                (lower.EndsWith(".ltx", StringComparison.Ordinal) ||
                 (lower.EndsWith(".xml", StringComparison.Ordinal) && lower.Contains("/text/", StringComparison.Ordinal)))) ||
               (lower.StartsWith("textures/ui/ui_icon_", StringComparison.Ordinal) && lower.EndsWith(".dds", StringComparison.Ordinal));
    }

    /// <summary>Well-known community mods announce themselves by file names at the top of gamedata.</summary>
    private static string? DetectMod(GameFileTree tree)
    {
        if (tree.DataDirectory is not { } dataRoot || !Directory.Exists(dataRoot)) return null;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dataRoot))
            {
                var name = Path.GetFileName(entry).ToLowerInvariant();
                var marker = ModMarkers.FirstOrDefault(marker => name.Contains(marker, StringComparison.Ordinal));
                if (marker is not null) return marker.ToUpperInvariant();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string SafeLanguage(string language)
    {
        var value = new string(language.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
        return value.Length > 0 ? value : "ru";
    }

    /// <summary>
    /// A readable prefix plus a hash of the exact key: "weapon/a" and "weapon:a" both sanitise to "weapon_a", and
    /// without the hash the second would be shown the first one's cached icon.
    /// </summary>
    internal static string IconCacheFileName(string key)
    {
        var readable = new string(key.Take(48).Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return $"{readable}-{hash}";
    }

    /// <summary>Crops item icons from the game's icon atlas on demand and caches them as PNG.</summary>
    private sealed class IconSource(GameFileTree tree, CatalogBundle bundle, string cacheDirectory)
    {
        private readonly ConcurrentDictionary<string, RgbaImage?> _atlases = new(StringComparer.OrdinalIgnoreCase);

        public byte[]? Png(string itemKey)
        {
            var item = bundle.Items.Resolve(itemKey);
            if (item?.IconX is not { } iconX || item.IconY is not { } iconY) return null;
            var cachePath = Path.Combine(cacheDirectory, IconCacheFileName(itemKey) + ".png");
            try
            {
                if (File.Exists(cachePath)) return File.ReadAllBytes(cachePath);
            }
            catch (IOException)
            {
                // Fall through and rebuild the icon.
            }

            var atlas = _atlases.GetOrAdd(item.IconTexture ?? "ui_icon_equipment", LoadAtlas);
            var crop = atlas?.Crop(
                iconX * IconCellSize,
                iconY * IconCellSize,
                Math.Max(1, item.Width ?? 1) * IconCellSize,
                Math.Max(1, item.Height ?? 1) * IconCellSize);
            if (crop is null) return null;
            var png = crop.ToPng();
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                File.WriteAllBytes(cachePath, png);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The icon is still returned; only caching failed.
            }

            return png;
        }

        private RgbaImage? LoadAtlas(string texture)
        {
            var name = texture.Replace('\\', '/').Trim('/');
            var relative = name.Contains('/', StringComparison.Ordinal) ? $"textures/{name}.dds" : $"textures/ui/{name}.dds";
            if (!tree.Files.TryGetValue(relative, out var file)) return null;
            try
            {
                return DdsImage.Decode(file.Read());
            }
            catch (Exception exception) when (exception is InvalidDataException or OverflowException or IOException)
            {
                return null;
            }
        }

    }
}
