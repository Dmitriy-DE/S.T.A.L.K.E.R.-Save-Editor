using StalkerSaveEditor.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop.Services;

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

    /// <summary>The editor's language as the name catalogs spell it (zh_CN, pt_BR).</summary>
    internal static string NamesLanguage => I18nService.Instance.CurrentLanguage.Replace('-', '_');
}
