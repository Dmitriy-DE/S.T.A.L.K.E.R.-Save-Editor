using System.Text.Json;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Verifies completeness and integrity of translations across all 15 supported locales:
/// - Coverage: every key in _messages.json is translated in en and uk (and other locales)
/// - Placeholders: {0}, {1}, etc. matching between source key and translated string
/// - Formatting: safe evaluation of placeholders without format exceptions
/// - Plural forms and fallback chain
/// </summary>
public static class I18nCompletenessChecker
{
    private static readonly Regex PlaceholderRegex = new(@"\{(\d+)(?:![rsa])?(?::[^{}]*)?\}", RegexOptions.Compiled);

    public sealed record ValidationResult(
        bool Success,
        int TotalMessages,
        int CheckedLocales,
        IReadOnlyList<string> Errors,
        IReadOnlyDictionary<string, int> TranslatedCounts);

    public static ValidationResult Validate(I18nService? service = null)
    {
        service ??= I18nService.Instance;
        var errors = new List<string>();
        var translatedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // 1. Load master messages list
        var masterCatalog = service.GetCatalog("_messages");
        List<string> masterKeys = [];

        if (masterCatalog.TryGetValue("messages", out var msgElement) && msgElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in msgElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s))
                        masterKeys.Add(s);
                }
            }
        }

        // If _messages.json is empty or not found, collect all unique keys from en.json
        if (masterKeys.Count == 0)
        {
            var enCatalog = service.GetCatalog("en");
            masterKeys = enCatalog.Keys.ToList();
        }

        if (masterKeys.Count == 0)
        {
            return new ValidationResult(false, 0, 0, ["No master messages found in _messages.json or en.json"], translatedCounts);
        }

        var requiredLanguages = new[] { "en", "uk" };
        var allLanguages = I18nService.SupportedLanguages.Keys.Where(k => !string.Equals(k, "ru", StringComparison.OrdinalIgnoreCase)).ToList();

        // 2. Check coverage for required languages (en, uk)
        foreach (var lang in requiredLanguages)
        {
            var catalog = service.GetCatalog(lang);
            var missing = masterKeys.Where(key => !catalog.ContainsKey(key) || catalog[key].ValueKind == JsonValueKind.Null).ToList();

            if (missing.Count > 0)
            {
                errors.Add($"Language '{lang}' is missing {missing.Count} required translations. First missing: '{missing[0]}'");
            }
        }

        // 3. Check all locales for count and placeholder matching
        foreach (var lang in allLanguages)
        {
            var catalog = service.GetCatalog(lang);
            var validCount = 0;

            foreach (var key in masterKeys)
            {
                if (!catalog.TryGetValue(key, out var element))
                    continue;

                validCount++;

                // Skip list plurals for placeholder matching
                if (element.ValueKind != JsonValueKind.String)
                    continue;

                var translated = element.GetString() ?? string.Empty;

                var srcPlaceholders = PlaceholderRegex.Matches(key)
                    .Select(m => m.Groups[1].Value)
                    .OrderBy(x => x)
                    .ToList();

                var dstPlaceholders = PlaceholderRegex.Matches(translated)
                    .Select(m => m.Groups[1].Value)
                    .OrderBy(x => x)
                    .ToList();

                if (!srcPlaceholders.SequenceEqual(dstPlaceholders))
                {
                    errors.Add($"Placeholder mismatch in '{lang}' for key '{key}': source=[{string.Join(",", srcPlaceholders)}], target=[{string.Join(",", dstPlaceholders)}]");
                }
                else if (srcPlaceholders.Count > 0)
                {
                    // Check that formatting does not throw
                    try
                    {
                        var dummyArgs = srcPlaceholders.Select(p => (object)$"ARG_{p}").ToArray();
                        var formatted = I18nService.FormatString(translated, dummyArgs);
                        if (string.IsNullOrEmpty(formatted))
                        {
                            errors.Add($"Formatted string is empty in '{lang}' for key '{key}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Formatting error in '{lang}' for key '{key}': {ex.Message}");
                    }
                }
            }

            translatedCounts[lang] = validCount;
        }

        // 4. Test plural rules
        ValidatePluralRules(service, errors);

        // 5. Test fallback chain
        ValidateFallbackChain(service, errors);

        return new ValidationResult(
            Success: errors.Count == 0,
            TotalMessages: masterKeys.Count,
            CheckedLocales: allLanguages.Count,
            Errors: errors,
            TranslatedCounts: translatedCounts);
    }

    private static void ValidatePluralRules(I18nService service, List<string> errors)
    {
        var testCases = new (string Lang, int Count, string Expected)[]
        {
            ("ru", 1, "сохранение"),
            ("ru", 3, "сохранения"),
            ("ru", 11, "сохранений"),
            ("uk", 22, "збереження"),
            ("uk", 25, "збережень"),
            ("en", 1, "save"),
            ("en", 2, "saves"),
            ("pl", 1, "zapis"),
            ("pl", 3, "zapisy"),
            ("pl", 5, "zapisów"),
            ("fr", 0, "sauvegarde"),
            ("ja", 7, "件のセーブ"),
        };

        foreach (var (lang, count, expected) in testCases)
        {
            var actual = service.TrnIn(lang, count, "сохранение", "сохранения", "сохранений");
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add($"Plural rule failure for '{lang}' with count {count}: expected '{expected}', actual '{actual}'");
            }
        }
    }

    private static void ValidateFallbackChain(I18nService service, List<string> errors)
    {
        // 1. Russian source returns as-is
        var ruText = service.TrIn("ru", "Сохранить");
        if (ruText != "Сохранить")
            errors.Add($"Ru source failed: expected 'Сохранить', got '{ruText}'");

        // 2. English translation
        var enText = service.TrIn("en", "Сохранить");
        if (enText != "Save")
            errors.Add($"En translation failed: expected 'Save', got '{enText}'");

        // 3. Fallback when key does not exist in any locale
        var nonExistent = service.TrIn("de", "НесуществующийКлюч123");
        if (nonExistent != "НесуществующийКлюч123")
            errors.Add($"Fallback for non-existent key failed: expected original text, got '{nonExistent}'");

        // 4. SourceText recovery
        var deText = service.TrIn("de", "Запись в облако недоступна: {0}; запись не начиналась", "offline");
        service.SetLanguage("de");
        var recovered = service.SourceText(deText);
        if (recovered != "Запись в облако недоступна: offline; запись не начиналась")
        {
            errors.Add($"SourceText recovery failed: expected 'Запись в облако недоступна: offline; запись не начиналась', got '{recovered}'");
        }
        service.SetLanguage("ru");
    }
}
