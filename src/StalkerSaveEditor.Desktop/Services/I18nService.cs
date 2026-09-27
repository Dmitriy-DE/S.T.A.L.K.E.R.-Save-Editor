using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Interface translation service keyed by Russian source text (gettext-style).
/// Supports 15 languages, on-the-fly switching, and fallback chain (locale -> en -> ru).
/// </summary>
public sealed class I18nService
{
    public const string SourceLanguage = "ru";

    public static readonly ReadOnlyDictionary<string, string> SupportedLanguages = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "Русский",
        ["uk"] = "Українська",
        ["en"] = "English",
        ["de"] = "Deutsch",
        ["fr"] = "Français",
        ["it"] = "Italiano",
        ["es"] = "Español",
        ["pl"] = "Polski",
        ["cs"] = "Čeština",
        ["pt-BR"] = "Português (Brasil)",
        ["tr"] = "Türkçe",
        ["ja"] = "日本語",
        ["ko"] = "한국어",
        ["zh-CN"] = "简体中文",
        ["zh-TW"] = "繁體中文",
    });

    private static readonly Lazy<I18nService> _instance = new(() => new I18nService());
    public static I18nService Instance => _instance.Value;

    private static readonly Regex PlaceholderRegex = new(@"\{(\d+)(?:![rsa])?(?::[^{}]*)?\}", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, Dictionary<string, JsonElement>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (Dictionary<string, string> Exact, List<(Regex Pattern, string Source)> Patterns)> _reverseCatalogs = new(StringComparer.OrdinalIgnoreCase);

    private string _currentLanguage = SourceLanguage;

    public event Action? LanguageChanged;

    public I18nService()
    {
        _currentLanguage = ResolveInitialLanguage();
    }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set => SetLanguage(value);
    }

    public void SetLanguage(string? code)
    {
        var normalized = NormalizeLanguageCode(code) ?? SourceLanguage;
        if (string.Equals(_currentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _currentLanguage = normalized;
        LanguageChanged?.Invoke();
    }

    public static string? NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var clean = code.Trim().Replace('_', '-').Split('.')[0].Split('@')[0];
        foreach (var key in SupportedLanguages.Keys)
        {
            if (string.Equals(key, clean, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        var lower = clean.ToLowerInvariant();
        if (lower.StartsWith("zh", StringComparison.Ordinal))
        {
            return lower.EndsWith("tw", StringComparison.Ordinal) ||
                   lower.EndsWith("hk", StringComparison.Ordinal) ||
                   lower.EndsWith("hant", StringComparison.Ordinal)
                ? "zh-TW"
                : "zh-CN";
        }

        if (lower.StartsWith("pt", StringComparison.Ordinal))
            return "pt-BR";

        var baseLang = lower.Split('-')[0];
        foreach (var key in SupportedLanguages.Keys)
        {
            if (string.Equals(key, baseLang, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        return null;
    }

    private static string ResolveInitialLanguage()
    {
        var env = Environment.GetEnvironmentVariable("STALKER_EDITOR_LANG");
        var fromEnv = NormalizeLanguageCode(env);
        if (fromEnv is not null)
            return fromEnv;

        try
        {
            var system = CultureInfo.CurrentUICulture.Name;
            var fromSystem = NormalizeLanguageCode(system);
            if (fromSystem is not null)
                return fromSystem;
        }
        catch
        {
            // fallback
        }

        return "en";
    }

    /// <summary>
    /// Translates Russian source text into the active language, formatting placeholders like {0}.
    /// Falls back to English if missing in active language, and Russian source if missing in English.
    /// </summary>
    public string Tr(string text, params object?[] args)
    {
        return TrIn(_currentLanguage, text, args);
    }

    /// <summary>
    /// Translates Russian source text into a specific language code.
    /// </summary>
    public string TrIn(string? code, string text, params object?[] args)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var targetLang = NormalizeLanguageCode(code) ?? _currentLanguage;
        var pattern = text;

        if (!string.Equals(targetLang, SourceLanguage, StringComparison.OrdinalIgnoreCase))
        {
            var catalog = GetCatalog(targetLang);
            if (catalog.TryGetValue(text, out var element) && element.ValueKind == JsonValueKind.String)
            {
                var val = element.GetString();
                if (!string.IsNullOrEmpty(val))
                    pattern = val;
            }
            else if (!string.Equals(targetLang, "en", StringComparison.OrdinalIgnoreCase))
            {
                // Fallback to English
                var enCatalog = GetCatalog("en");
                if (enCatalog.TryGetValue(text, out var enElement) && enElement.ValueKind == JsonValueKind.String)
                {
                    var val = enElement.GetString();
                    if (!string.IsNullOrEmpty(val))
                        pattern = val;
                }
            }
        }

        return FormatString(pattern, args);
    }

    /// <summary>
    /// Translates plural forms according to CLDR plural rules for the active language.
    /// </summary>
    public string Trn(int count, string one, string few, string many, params object?[] args)
    {
        return TrnIn(_currentLanguage, count, one, few, many, args);
    }

    public string TrnIn(string? code, int count, string one, string few, string many, params object?[] args)
    {
        var targetLang = NormalizeLanguageCode(code) ?? _currentLanguage;
        var key = $"{one}|{few}|{many}";
        List<string> forms = [one, few, many];

        if (!string.Equals(targetLang, SourceLanguage, StringComparison.OrdinalIgnoreCase))
        {
            var catalog = GetCatalog(targetLang);
            if (catalog.TryGetValue(key, out var element))
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    var loaded = new List<string>();
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            loaded.Add(item.GetString() ?? string.Empty);
                    }
                    if (loaded.Count > 0)
                        forms = loaded;
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    forms = [element.GetString() ?? one];
                }
            }
        }

        var index = GetPluralIndex(targetLang, count);
        var chosen = forms[Math.Min(index, forms.Count - 1)];

        if (args is { Length: > 0 })
        {
            return FormatString(chosen, args);
        }

        return chosen;
    }

    /// <summary>
    /// Maps a translated text back to its Russian source text for backend classifiers.
    /// </summary>
    public string SourceText(string text)
    {
        if (string.IsNullOrEmpty(text) || string.Equals(_currentLanguage, SourceLanguage, StringComparison.OrdinalIgnoreCase))
            return text;

        var (exact, patterns) = GetReverseCatalog(_currentLanguage);
        if (exact.TryGetValue(text, out var source))
            return source;

        foreach (var (pattern, src) in patterns)
        {
            var match = pattern.Match(text);
            if (!match.Success) continue;

            return PlaceholderRegex.Replace(src, m =>
            {
                var idx = m.Groups[1].Value;
                foreach (Group g in match.Groups)
                {
                    if (g.Name.StartsWith($"p{idx}_", StringComparison.Ordinal))
                        return g.Value;
                }
                return m.Value;
            });
        }

        return text;
    }

    private static int GetPluralIndex(string lang, int count)
    {
        var n = Math.Abs(count);
        switch (lang)
        {
            case "ru":
            case "uk":
                if (n % 10 == 1 && n % 100 != 11) return 0;
                if (n % 10 >= 2 && n % 10 <= 4 && !(n % 100 >= 12 && n % 100 <= 14)) return 1;
                return 2;

            case "pl":
                if (n == 1) return 0;
                if (n % 10 >= 2 && n % 10 <= 4 && !(n % 100 >= 12 && n % 100 <= 14)) return 1;
                return 2;

            case "cs":
                if (n == 1) return 0;
                if (n is >= 2 and <= 4) return 1;
                return 2;

            case "ja":
            case "ko":
            case "zh-CN":
            case "zh-TW":
            case "tr":
                return 0;

            case "fr":
            case "pt-BR":
                return n is 0 or 1 ? 0 : 1;

            default:
                return n == 1 ? 0 : 1;
        }
    }

    public static string FormatString(string pattern, object?[]? args)
    {
        if (args is null || args.Length == 0)
            return pattern;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, pattern, args);
        }
        catch (FormatException)
        {
            // Fallback manual replace if custom format fails
            var result = pattern;
            for (var i = 0; i < args.Length; i++)
            {
                result = Regex.Replace(result, $@"\{{{i}(?::[^{{}}]+)?\}}", args[i]?.ToString() ?? string.Empty);
            }
            return result;
        }
    }

    public Dictionary<string, JsonElement> GetCatalog(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode) ?? SourceLanguage;
        return _catalogs.GetOrAdd(normalized, LoadCatalog);
    }

    private (Dictionary<string, string> Exact, List<(Regex Pattern, string Source)> Patterns) GetReverseCatalog(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode) ?? SourceLanguage;
        return _reverseCatalogs.GetOrAdd(normalized, code =>
        {
            var catalog = GetCatalog(code);
            var exact = new Dictionary<string, string>(StringComparer.Ordinal);
            var patterns = new List<(Regex Pattern, string Source)>();

            foreach (var (source, element) in catalog)
            {
                if (element.ValueKind != JsonValueKind.String) continue;
                var val = element.GetString();
                if (string.IsNullOrEmpty(val)) continue;

                if (!PlaceholderRegex.IsMatch(val))
                {
                    exact.TryAdd(val, source);
                    continue;
                }

                var parts = PlaceholderRegex.Split(val);
                var regexBuilder = new System.Text.StringBuilder("^");
                for (var pos = 0; pos < parts.Length; pos++)
                {
                    if (pos % 2 == 0)
                    {
                        regexBuilder.Append(Regex.Escape(parts[pos]));
                    }
                    else
                    {
                        regexBuilder.Append($"(?<p{parts[pos]}_{pos}>.*?)");
                    }
                }
                regexBuilder.Append('$');
                try
                {
                    patterns.Add((new Regex(regexBuilder.ToString(), RegexOptions.Singleline | RegexOptions.Compiled), source));
                }
                catch
                {
                    // Ignore malformed regex
                }
            }

            return (exact, patterns);
        });
    }

    private static Dictionary<string, JsonElement> LoadCatalog(string code)
    {
        // 1. Try file on disk
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "i18n", $"{code}.json"),
            Path.Combine(AppContext.BaseDirectory, "i18n", $"{code.Replace('-', '_')}.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "StalkerSaveEditor.Desktop", "i18n", $"{code}.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "StalkerSaveEditor.Desktop", "i18n", $"{code.Replace('-', '_')}.json"),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream) ?? [];
                }
                catch
                {
                    // Continue to next option
                }
            }
        }

        // 2. Try Embedded Resource
        var asm = typeof(I18nService).Assembly;
        var resourceNames = new[]
        {
            $"StalkerSaveEditor.Desktop.i18n.{code}.json",
            $"StalkerSaveEditor.Desktop.i18n.{code.Replace('-', '_')}.json",
        };

        foreach (var resName in resourceNames)
        {
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream is not null)
            {
                try
                {
                    return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream) ?? [];
                }
                catch
                {
                    // Continue
                }
            }
        }

        return [];
    }
}
