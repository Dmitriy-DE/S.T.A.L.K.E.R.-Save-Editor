using System.Text;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Content;

/// <summary>One X-Ray LTX section: its own values, parents and bare entries.</summary>
internal sealed class LtxSection(string name, IReadOnlyList<string> bases, string source)
{
    public string Name { get; } = name;

    public IReadOnlyList<string> Bases { get; } = bases;

    public string Source { get; } = source;

    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public List<string> Entries { get; } = [];
}

/// <summary>
/// Parser for the INI-like LTX files of the X-Ray trilogy (port of the Python oracle's
/// <c>_parse_ltx</c>): <c>[name]:base1,base2</c> headers, <c>key = value</c> lines,
/// bare entries, <c>;</c> comments outside quotes. <c>#include</c> is skipped because the
/// caller parses every file of the tree anyway.
/// </summary>
internal static partial class LtxDocument
{
    public static Dictionary<string, LtxSection> Parse(string text, string source) =>
        Parse(text, source, include: null);

    /// <summary>
    /// Parses <paramref name="rootPath"/> and every file it pulls in through <c>#include</c>
    /// (relative to the including file, <c>*</c> wildcards allowed), in the order the engine reads
    /// them. Files outside the include graph (for example ini files a script opens on its own) are
    /// not part of the item database and are ignored.
    /// </summary>
    public static Dictionary<string, LtxSection>? ParseIncludeGraph(
        string rootPath,
        IReadOnlyDictionary<string, GameFile> files)
    {
        if (!files.ContainsKey(rootPath)) return null;
        var sections = new Dictionary<string, LtxSection>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = files.Keys.ToArray();

        void Visit(string path)
        {
            if (!visited.Add(path) || !files.TryGetValue(path, out var file)) return;
            var directory = path.Contains('/', StringComparison.Ordinal) ? path[..(path.LastIndexOf('/') + 1)] : string.Empty;
            Parse(Decode(file.Read()), path, include =>
            {
                var target = NormalizeRelative(directory + include.Replace('\\', '/'));
                if (target.Contains('*', StringComparison.Ordinal))
                {
                    var pattern = new Regex(
                        "^" + Regex.Escape(target).Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    foreach (var match in paths.Where(candidate => pattern.IsMatch(candidate)).Order(StringComparer.OrdinalIgnoreCase))
                    {
                        Visit(match);
                    }
                }
                else
                {
                    Visit(files.Keys.FirstOrDefault(candidate => string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase)) ?? target);
                }
            }, sections);
        }

        Visit(rootPath);
        return sections;
    }

    private static Dictionary<string, LtxSection> Parse(
        string text,
        string source,
        Action<string>? include,
        Dictionary<string, LtxSection>? sections = null)
    {
        sections ??= new Dictionary<string, LtxSection>(StringComparer.Ordinal);
        LtxSection? current = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var includeMatch = IncludePattern().Match(line);
            if (includeMatch.Success)
            {
                include?.Invoke(includeMatch.Groups[1].Value);
                continue;
            }

            var header = SectionPattern().Match(line);
            if (header.Success)
            {
                var bases = header.Groups[2].Success
                    ? header.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];
                current = new LtxSection(header.Groups[1].Value.Trim(), bases, source);
                sections[current.Name] = current;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                current.Entries.Add(line);
                continue;
            }

            var key = line[..equals].Trim().ToLowerInvariant();
            var value = line[(equals + 1)..].Trim().Trim('"', '\'').Trim();
            current.Values[key] = value;
        }

        return sections;
    }

    /// <summary>UTF-8 when valid, otherwise Windows-1251, as the game's text files use both.</summary>
    public static string Decode(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            data = data[3..];
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(data);
        }
    }

    /// <summary>Values of every section with its inheritance chain applied (own values win).</summary>
    public static IEnumerable<(LtxSection Section, IReadOnlyDictionary<string, string> Values)> Resolve(
        IReadOnlyDictionary<string, LtxSection> sections)
    {
        var cache = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        Dictionary<string, string> ResolveOne(string name, HashSet<string> stack)
        {
            if (cache.TryGetValue(name, out var cached)) return cached;
            if (!stack.Add(name)) return new Dictionary<string, string>(StringComparer.Ordinal);
            var section = sections[name];
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parent in section.Bases)
            {
                if (!sections.ContainsKey(parent)) continue;
                foreach (var (key, value) in ResolveOne(parent, stack))
                {
                    values[key] = value;
                }
            }

            foreach (var (key, value) in section.Values)
            {
                values[key] = value;
            }

            stack.Remove(name);
            cache[name] = values;
            return values;
        }

        foreach (var section in sections.Values)
        {
            yield return (section, ResolveOne(section.Name, new HashSet<string>(StringComparer.Ordinal)));
        }
    }

    private static string NormalizeRelative(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == ".." && parts.Count > 0) parts.RemoveAt(parts.Count - 1);
            else parts.Add(part);
        }

        return string.Join('/', parts);
    }

    private static string StripComment(string line)
    {
        var quote = '\0';
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character is '"' or '\'')
            {
                if (quote == character) quote = '\0';
                else if (quote == '\0') quote = character;
            }
            else if (quote == '\0' && character == ';')
            {
                return line[..index];
            }
        }

        return line;
    }

    [GeneratedRegex(@"^\[([^\]]+)\](?::(.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"^#include\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IncludePattern();
}
