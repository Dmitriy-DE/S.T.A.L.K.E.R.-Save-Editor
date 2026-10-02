using StalkerSaveEditor.Core.Catalogs;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Catalogue and official-name lookups shared by everything that turns a parsed save into rows: the library loader
/// and the editing view models. It used to be static state on the root view model, which made the loader depend on
/// the view model it feeds.
/// </summary>
internal static class SaveNaming
{
    private static readonly IReadOnlyDictionary<string, CatalogBundle> Catalogs = CatalogBundleReader.LoadEmbedded();

    /// <summary>The installed game's own catalogue when it was read, otherwise the one shipped with the editor.</summary>
    internal static bool TryCatalog(string releaseId, out CatalogBundle bundle) =>
        GameContentRegistry.TryGetCatalog(releaseId, out bundle) || Catalogs.TryGetValue(releaseId, out bundle!);

    internal static readonly OfficialNamesCatalog OfficialNames = OfficialNamesCatalog.LoadEmbedded();

    /// <summary>
    /// An item's name in the interface language: the games' own string tables first, then the installed game's
    /// catalogue (items a mod adds, in the language of that installation), then the section name.
    /// </summary>
    internal static string ItemName(string releaseId, string key, string? installedName = null) =>
        OfficialNames.Resolve(releaseId, "items", key, NamesLanguage)
        ?? installedName
        ?? (TryCatalog(releaseId, out var bundle) ? bundle.Items.Resolve(key)?.DisplayName : null)
        ?? key;

    /// <summary>An upgrade's name in the interface language, else <paramref name="fallback"/>.</summary>
    internal static string UpgradeName(string? releaseId, string key, string? fallback) =>
        OfficialNames.Resolve(releaseId, "upgrades", key, NamesLanguage) ?? fallback ?? key;

    /// <summary>The editor's language as the name catalogs spell it (zh_CN, pt_BR).</summary>
    internal static string NamesLanguage => I18nService.Instance.CurrentLanguage.Replace('-', '_');
}
