using System.Text;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Companion;

public static partial class CompanionAppDataRootResolver
{
    private static readonly Encoding FsgameEncoding = CreateFsgameEncoding();

    public static string Resolve(string gameDirectory, string fsgameFileName = "fsgame.ltx")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        if (fsgameFileName == "fsgame.ltx" && EnhancedEditionRoot(gameDirectory) is { } enhanced) return enhanced;
        ArgumentException.ThrowIfNullOrWhiteSpace(fsgameFileName);

        string root;
        try
        {
            root = Path.GetFullPath(gameDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            throw new CompanionProtocolException($"Invalid game directory: {exception.Message}", exception);
        }

        if (Path.GetFileName(fsgameFileName) != fsgameFileName)
        {
            throw new CompanionProtocolException("The fsgame file name must not contain a directory path.");
        }

        var fsgamePath = Path.Combine(root, fsgameFileName);
        if (!Directory.Exists(root) || !File.Exists(fsgamePath))
        {
            throw new CompanionProtocolException($"The game fsgame file was not found: {fsgameFileName}.");
        }

        string text;
        try
        {
            using var reader = new StreamReader(fsgamePath, FsgameEncoding, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CompanionProtocolException($"Could not read {fsgameFileName}: {exception.Message}", exception);
        }

        var definitions = ParseDefinitions(text, fsgameFileName);
        return ResolveAlias("$app_data_root$", definitions, root, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static readonly (string Fsgame, string AppId)[] EnhancedEditions =
    [
        ("fsgame_soc.ltx", "2427410"),
        ("fsgame_cs.ltx", "2427420"),
        ("fsgame_cop.ltx", "2427430"),
    ];

    /// <summary>
    /// Enhanced Edition has no fsgame.ltx and no $app_data_root$: the engine uses
    /// "Saved Games/&lt;install folder name&gt;/STEAM" — under Proton inside steamapps/compatdata/&lt;appid&gt;/pfx.
    /// </summary>
    internal static string? EnhancedEditionRoot(string gameDirectory, string? windowsSavedGames = null)
    {
        var root = Path.GetFullPath(gameDirectory);
        if (File.Exists(Path.Combine(root, "fsgame.ltx"))) return null;
        var edition = EnhancedEditions.FirstOrDefault(entry => File.Exists(Path.Combine(root, entry.Fsgame)));
        if (edition.Fsgame is null) return null;

        var title = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (OperatingSystem.IsWindows() || windowsSavedGames is not null)
        {
            var savedGames = windowsSavedGames ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
            return Path.Combine(savedGames, title, "STEAM");
        }

        // <library>/steamapps/common/<title> → <library>/steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/Saved Games/<title>/STEAM
        var steamapps = Path.GetDirectoryName(Path.GetDirectoryName(root));
        return steamapps is null
            ? null
            : Path.Combine(steamapps, "compatdata", edition.AppId, "pfx", "drive_c", "users", "steamuser", "Saved Games", title, "STEAM");
    }

    private static Dictionary<string, string[]> ParseDefinitions(string text, string fileName)
    {
        var definitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Split(';', 2)[0].Trim();
            var match = DefinitionLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups[1].Value;
            var values = match.Groups[2].Value.Split('|')
                .Select(value => value.Trim().Trim('"'))
                .ToArray();
            if (values.Length < 4)
            {
                if (string.Equals(key, "$app_data_root$", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CompanionProtocolException($"{fileName} has an incomplete $app_data_root$ definition.");
                }

                continue;
            }

            if (!definitions.TryAdd(key, values))
            {
                throw new CompanionProtocolException($"{fileName} defines {key} more than once.");
            }
        }

        if (!definitions.ContainsKey("$app_data_root$"))
        {
            throw new CompanionProtocolException($"{fileName} does not define $app_data_root$.");
        }

        return definitions;
    }

    private static string ResolveAlias(
        string alias,
        IReadOnlyDictionary<string, string[]> definitions,
        string gameDirectory,
        HashSet<string> resolving)
    {
        if (string.Equals(alias, "$fs_root$", StringComparison.OrdinalIgnoreCase))
        {
            return gameDirectory;
        }

        if (!resolving.Add(alias))
        {
            throw new CompanionProtocolException($"The fsgame path aliases contain a cycle at {alias}.");
        }

        if (!definitions.TryGetValue(alias, out var values) || values.Length < 4)
        {
            resolving.Remove(alias);
            throw new CompanionProtocolException($"The fsgame path alias {alias} is not defined.");
        }

        try
        {
            var parentExpression = ExpandEnvironment(values[2]);
            if (string.IsNullOrWhiteSpace(parentExpression))
            {
                throw new CompanionProtocolException($"The fsgame path alias {alias} has an empty parent path.");
            }

            var parent = IsAlias(parentExpression)
                ? ResolveAlias(parentExpression, definitions, gameDirectory, resolving)
                : Path.IsPathRooted(parentExpression)
                    ? Path.GetFullPath(NormalizePath(parentExpression))
                    : Path.GetFullPath(Path.Combine(gameDirectory, NormalizePath(parentExpression)));
            var childExpression = ExpandEnvironment(values[3]);
            var resolved = Path.GetFullPath(Path.Combine(parent, NormalizePath(childExpression)));
            return Path.TrimEndingDirectorySeparator(resolved);
        }
        catch (CompanionProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            throw new CompanionProtocolException($"Could not resolve fsgame path alias {alias}: {exception.Message}", exception);
        }
        finally
        {
            resolving.Remove(alias);
        }
    }

    private static bool IsAlias(string value) =>
        value.Length > 2 && value[0] == '$' && value[^1] == '$';

    private static string ExpandEnvironment(string value) => EnvironmentVariableRegex().Replace(value, match =>
    {
        var name = match.Groups[1].Value;
        var expansion = Environment.GetEnvironmentVariable(name);
        if (expansion is null)
        {
            throw new CompanionProtocolException($"The fsgame path refers to undefined environment variable %{name}%.");
        }

        return expansion;
    });

    private static string NormalizePath(string path) => path
        .Replace('\\', Path.DirectorySeparatorChar)
        .Replace('/', Path.DirectorySeparatorChar);

    private static Encoding CreateFsgameEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    [GeneratedRegex("^(\\$[^$]+\\$)\\s*=\\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DefinitionLineRegex();

    [GeneratedRegex("%([^%]+)%", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariableRegex();
}
