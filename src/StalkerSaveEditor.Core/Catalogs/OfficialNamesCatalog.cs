using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;

namespace StalkerSaveEditor.Core.Catalogs;

public sealed class OfficialNamesCatalog
{
    private const string EmbeddedResourceName = "StalkerSaveEditor.Core.Catalogs.Data.catalog_names.json";
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> _releases;

    private OfficialNamesCatalog(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> releases)
    {
        _releases = releases;
    }

    public static OfficialNamesCatalog Load(ReadOnlySpan<byte> payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload.ToArray());
        }
        catch (JsonException exception)
        {
            throw new CatalogBundleException("Invalid official names JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("releases", out var rawReleases) ||
                rawReleases.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogBundleException("Official names document does not contain releases.");
            }

            var releases = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>>(StringComparer.Ordinal);
            foreach (var release in rawReleases.EnumerateObject())
            {
                if (release.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new CatalogBundleException($"Invalid official names family '{release.Name}'.");
                }

                var kinds = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
                foreach (var kind in release.Value.EnumerateObject())
                {
                    if (kind.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw new CatalogBundleException($"Invalid official names kind '{kind.Name}'.");
                    }

                    var entries = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
                    foreach (var entry in kind.Value.EnumerateObject())
                    {
                        if (entry.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new CatalogBundleException($"Invalid official names entry '{entry.Name}'.");
                        }

                        var languages = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var language in entry.Value.EnumerateObject())
                        {
                            if (language.Value.ValueKind != JsonValueKind.String)
                            {
                                throw new CatalogBundleException($"Invalid official name translation '{entry.Name}'.");
                            }

                            languages.Add(language.Name, language.Value.GetString()!);
                        }

                        entries.Add(entry.Name, new ReadOnlyDictionary<string, string>(languages));
                    }

                    kinds.Add(kind.Name, new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(entries));
                }

                releases.Add(release.Name, new ReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(kinds));
            }

            return new OfficialNamesCatalog(
                new ReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>>(releases));
        }
    }

    public static OfficialNamesCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new CatalogBundleException("Embedded official names catalog is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    public string? Resolve(string? releaseId, string kind, string? key, string? language = null)
    {
        var family = ReleaseFamily(releaseId);
        if (family is null || string.IsNullOrEmpty(key) ||
            !_releases.TryGetValue(family, out var kinds) ||
            !kinds.TryGetValue(kind, out var entries) ||
            !entries.TryGetValue(key, out var names))
        {
            return null;
        }

        var requested = string.IsNullOrEmpty(language) ? "en" : language;
        if (names.TryGetValue(requested, out var preferred) && preferred.Length > 0) return preferred;
        if (names.TryGetValue("en", out var english) && english.Length > 0) return english;
        return names.TryGetValue("ru", out var russian) && russian.Length > 0 ? russian : null;
    }

    public static string? ReleaseFamily(string? releaseId)
    {
        var value = (releaseId ?? string.Empty).ToLowerInvariant();
        if (value.StartsWith("stalker2", StringComparison.Ordinal)) return null;
        if (value.Contains("cop", StringComparison.Ordinal) ||
            value.Contains("pripyat", StringComparison.Ordinal) ||
            value.Contains("prypiat", StringComparison.Ordinal)) return "cop";
        if (value.Contains("-cs", StringComparison.Ordinal) ||
            value.Contains("clear", StringComparison.Ordinal) ||
            value == "cs") return "clear_sky";
        if (value.Contains("soc", StringComparison.Ordinal) ||
            value.Contains("shadow", StringComparison.Ordinal)) return "soc";
        return value is "soc" or "clear_sky" or "cop" ? value : null;
    }
}
