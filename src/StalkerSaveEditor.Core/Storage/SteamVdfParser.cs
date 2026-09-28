using System.Collections.ObjectModel;
using System.Text;

namespace StalkerSaveEditor.Core.Storage;

public static class SteamVdfParser
{
    public static IReadOnlyDictionary<string, object> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = Tokenize(text);
        var index = 0;
        var document = ReadObject(tokens, ref index, expectClose: false);
        if (index != tokens.Count)
        {
            throw new FormatException("Unexpected trailing Valve KeyValues token.");
        }

        return document;
    }

    public static IReadOnlyList<string> GetLibraryPaths(string text)
    {
        var document = Parse(text);
        if (!TryGetObject(document, "libraryfolders", out var folders))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (var entry in folders.Values)
        {
            if (entry is string legacyPath)
            {
                paths.Add(legacyPath);
            }
            else if (entry is IReadOnlyDictionary<string, object> properties &&
                     TryGetString(properties, "path", out var modernPath) &&
                     !string.IsNullOrWhiteSpace(modernPath))
            {
                paths.Add(modernPath);
            }
        }

        return Array.AsReadOnly(paths.ToArray());
    }

    internal static bool TryGetObject(
        IReadOnlyDictionary<string, object> parent,
        string key,
        out IReadOnlyDictionary<string, object> value)
    {
        foreach (var pair in parent)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                pair.Value is IReadOnlyDictionary<string, object> child)
            {
                value = child;
                return true;
            }
        }

        value = null!;
        return false;
    }

    internal static bool TryGetString(
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

    private static ReadOnlyDictionary<string, object> ReadObject(
        IReadOnlyList<Token> tokens,
        ref int index,
        bool expectClose)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        while (index < tokens.Count)
        {
            var key = tokens[index++];
            if (key.Kind is TokenKind.Close)
            {
                if (!expectClose)
                {
                    throw new FormatException("Unexpected closing brace in Valve KeyValues.");
                }

                return new ReadOnlyDictionary<string, object>(result);
            }

            if (key.Kind is not TokenKind.Value || index >= tokens.Count)
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

        return new ReadOnlyDictionary<string, object>(result);
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

            if (character is '{' or '}')
            {
                tokens.Add(new Token(character is '{' ? TokenKind.Open : TokenKind.Close, string.Empty));
                index++;
                continue;
            }

            if (character == '"')
            {
                tokens.Add(ReadQuotedValue(text, ref index));
                continue;
            }

            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not ('{' or '}'))
            {
                index++;
            }

            if (start == index)
            {
                throw new FormatException("Empty Valve KeyValues token.");
            }

            tokens.Add(new Token(TokenKind.Value, text[start..index]));
        }

        return tokens;
    }

    private static Token ReadQuotedValue(string text, ref int index)
    {
        index++;
        var value = new StringBuilder();
        while (index < text.Length)
        {
            var character = text[index++];
            if (character == '"')
            {
                return new Token(TokenKind.Value, value.ToString());
            }

            if (character == '\\' && index < text.Length)
            {
                var escaped = text[index];
                if (escaped is '\\' or '"')
                {
                    value.Append(escaped);
                    index++;
                }
                else
                {
                    value.Append('\\');
                }
            }
            else
            {
                value.Append(character);
            }
        }

        throw new FormatException("Unterminated quoted Valve KeyValues value.");
    }

    private enum TokenKind
    {
        Value,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Value);
}
