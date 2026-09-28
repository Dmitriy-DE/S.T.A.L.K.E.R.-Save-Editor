using System.Xml;
using System.Xml.Linq;

namespace StalkerSaveEditor.Core.Content;

/// <summary>
/// Reads X-Ray string tables (<c>text/&lt;lang&gt;/*.xml</c>: <c>&lt;string id="…"&gt;&lt;text&gt;…&lt;/text&gt;</c>).
/// Only the most preferred language folder that is present is used, so a multi-language
/// install does not mix Russian and English names.
/// </summary>
internal static class XRayStringTables
{
    private static readonly IReadOnlyDictionary<string, string[]> FolderCodes = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ru"] = ["rus", "ru"],
        ["uk"] = ["ukr", "uk", "ua"],
        ["en"] = ["eng", "en"],
        ["de"] = ["ger", "de", "deu"],
        ["fr"] = ["fra", "fr", "fre"],
        ["it"] = ["ita", "it"],
        ["es"] = ["spa", "es", "esp"],
        ["pl"] = ["pol", "pl"],
        ["cs"] = ["cze", "cs", "ces"],
    };

    /// <summary>Folder codes in preference order for an UI language (port of <c>xray_text_codes</c>).</summary>
    public static IReadOnlyList<string[]> PreferenceOrder(string uiLanguage)
    {
        var order = new List<string[]>();
        if (FolderCodes.TryGetValue(uiLanguage, out var own)) order.Add(own);
        if (uiLanguage == "ru")
        {
            order.Add(FolderCodes["en"]);
        }
        else
        {
            order.Add(FolderCodes["en"]);
            order.Add(FolderCodes["ru"]);
        }

        return order.Distinct().ToArray();
    }

    public static Dictionary<string, string> Read(IReadOnlyList<GameFile> files, string uiLanguage)
    {
        var candidates = files
            .Where(file => file.RelativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                           ($"/{file.RelativePath}/".Contains("/text/", StringComparison.OrdinalIgnoreCase) ||
                            $"/{file.RelativePath}/".Contains("/localization/", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var chosen = candidates;
        foreach (var codes in PreferenceOrder(uiLanguage))
        {
            var match = candidates.Where(file => codes.Any(code =>
                    $"/{file.RelativePath}/".Contains($"/text/{code}/", StringComparison.OrdinalIgnoreCase) ||
                    $"/{file.RelativePath}/".Contains($"/localization/{code}/", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (match.Length > 0)
            {
                chosen = match;
                break;
            }
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in chosen)
        {
            XDocument document;
            try
            {
                // Game XML declares windows-1251; decode ourselves and drop the declaration.
                var text = LtxDocument.Decode(file.Read());
                var start = text.IndexOf("?>", StringComparison.Ordinal);
                if (text.TrimStart().StartsWith("<?xml", StringComparison.Ordinal) && start >= 0)
                {
                    text = text[(start + 2)..];
                }

                document = XDocument.Parse(text, LoadOptions.None);
            }
            catch (Exception exception) when (exception is XmlException or IOException or InvalidDataException)
            {
                continue;
            }

            foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "string"))
            {
                var key = element.Attribute("id")?.Value;
                var text = element.Elements().FirstOrDefault(child => child.Name.LocalName == "text")?.Value.Trim();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(text))
                {
                    values[key] = text;
                }
            }
        }

        return values;
    }
}
