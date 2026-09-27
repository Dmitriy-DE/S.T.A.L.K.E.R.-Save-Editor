namespace StalkerSaveEditor.Core.Storage;

public static class SteamLibraryFolderLocator
{
    public static IReadOnlyList<string> GetLibraries(IEnumerable<string> steamRoots)
    {
        ArgumentNullException.ThrowIfNull(steamRoots);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var libraries = new List<string>();
        var seen = new HashSet<string>(comparer);
        foreach (var root in steamRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
                if (!Directory.Exists(fullRoot) || !seen.Add(fullRoot))
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                continue;
            }

            libraries.Add(fullRoot);
            var libraryFile = Path.Combine(fullRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            try
            {
                var document = ValveKeyValuesParser.Parse(File.ReadAllText(libraryFile));
                if (!TryGetObject(document, "libraryfolders", out var folders))
                {
                    continue;
                }

                foreach (var entry in folders.Values)
                {
                    var path = entry switch
                    {
                        string legacyPath => legacyPath,
                        Dictionary<string, object> properties when TryGetString(properties, "path", out var modernPath) => modernPath,
                        _ => null,
                    };
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    try
                    {
                        var fullPath = Path.GetFullPath(UnescapePath(path));
                        if (Directory.Exists(fullPath) && seen.Add(fullPath))
                        {
                            libraries.Add(fullPath);
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
                    {
                        // One unusable library entry must not hide the remaining Steam libraries.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                // A malformed library file is isolated to its Steam root.
            }
        }

        return libraries.AsReadOnly();
    }

    public static string? GetManifestInstallDirectory(string libraryRoot, int appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        if (appId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appId));
        }

        var library = Path.GetFullPath(libraryRoot);
        var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
        try
        {
            var document = ValveKeyValuesParser.Parse(File.ReadAllText(manifest));
            if (!TryGetObject(document, "AppState", out var appState) ||
                !TryGetString(appState, "appid", out var manifestAppId) ||
                !int.TryParse(manifestAppId, out var actualAppId) || actualAppId != appId ||
                !TryGetString(appState, "installdir", out var installDirectory) ||
                string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(library, "steamapps", "common", UnescapePath(installDirectory)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static bool TryGetObject(
        IReadOnlyDictionary<string, object> parent,
        string key,
        out Dictionary<string, object> value)
    {
        foreach (var pair in parent)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                pair.Value is Dictionary<string, object> child)
            {
                value = child;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, object> parent,
        string key,
        out string value)
    {
        foreach (var pair in parent)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && pair.Value is string text)
            {
                value = text;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string UnescapePath(string value) => value;

    private static class ValveKeyValuesParser
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var tokens = Tokenize(text);
            var index = 0;
            var result = ReadObject(tokens, ref index, expectClose: false);
            if (index != tokens.Count)
            {
                throw new FormatException("Unexpected trailing Valve KeyValues token.");
            }

            return result;
        }

        private static Dictionary<string, object> ReadObject(
            IReadOnlyList<Token> tokens,
            ref int index,
            bool expectClose)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (index < tokens.Count)
            {
                var key = tokens[index++];
                if (key.Kind == TokenKind.Close)
                {
                    if (!expectClose)
                    {
                        throw new FormatException("Unexpected closing brace in Valve KeyValues.");
                    }

                    return result;
                }

                if (key.Kind != TokenKind.Value || index >= tokens.Count)
                {
                    throw new FormatException("Expected a Valve KeyValues key/value pair.");
                }

                var next = tokens[index++];
                object value = next.Kind switch
                {
                    TokenKind.Value => next.Value,
                    TokenKind.Open => ReadObject(tokens, ref index, expectClose: true),
                    _ => throw new FormatException("Expected a Valve KeyValues value or object."),
                };
                result[key.Value] = value;
            }

            if (expectClose)
            {
                throw new FormatException("Unclosed Valve KeyValues object.");
            }

            return result;
        }

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            for (var index = 0; index < text.Length;)
            {
                var character = text[index];
                if (char.IsWhiteSpace(character))
                {
                    index++;
                    continue;
                }

                if (character == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    index += 2;
                    while (index < text.Length && text[index] is not ('\r' or '\n')) index++;
                    continue;
                }

                if (character == '#')
                {
                    while (index < text.Length && text[index] is not ('\r' or '\n')) index++;
                    continue;
                }

                if (character == '{')
                {
                    tokens.Add(new Token(TokenKind.Open, string.Empty));
                    index++;
                    continue;
                }

                if (character == '}')
                {
                    tokens.Add(new Token(TokenKind.Close, string.Empty));
                    index++;
                    continue;
                }

                if (character != '"')
                {
                    throw new FormatException($"Unexpected Valve KeyValues character at offset {index}.");
                }

                index++;
                var value = new System.Text.StringBuilder();
                var closed = false;
                while (index < text.Length)
                {
                    character = text[index++];
                    if (character == '"')
                    {
                        closed = true;
                        break;
                    }

                    if (character == '\\' && index < text.Length)
                    {
                        var escaped = text[index++];
                        if (escaped is '\\' or '"')
                        {
                            value.Append(escaped);
                        }
                        else
                        {
                            value.Append('\\');
                            value.Append(escaped);
                        }
                    }
                    else
                    {
                        value.Append(character);
                    }
                }

                if (!closed)
                {
                    throw new FormatException("Unterminated quoted Valve KeyValues value.");
                }

                tokens.Add(new Token(TokenKind.Value, value.ToString()));
            }

            return tokens;
        }

        private enum TokenKind
        {
            Value,
            Open,
            Close,
        }

        private readonly record struct Token(TokenKind Kind, string Value);
    }
}
