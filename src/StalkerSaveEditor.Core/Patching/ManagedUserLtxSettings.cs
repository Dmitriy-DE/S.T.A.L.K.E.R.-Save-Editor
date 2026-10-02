using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Storage;

namespace StalkerSaveEditor.Core.Patching;

public sealed record UserLtxSettingInfo(
    string Key,
    string Meaning,
    string? CurrentValue,
    string? OriginalValue,
    bool ChangedByToolkit,
    bool HasConflict)
{
    public bool CanRestoreDefault => ChangedByToolkit && !HasConflict;
}

public sealed record UserLtxSettingsInspection(
    string FilePath,
    bool HasConflict,
    string? Conflict,
    IReadOnlyList<UserLtxSettingInfo> Settings);

/// <summary>Edits only a small allow-list of X-Ray user.ltx console settings, preserving every other byte of the file.</summary>
public static partial class ManagedUserLtxSettings
{
    private const int ManifestSchema = 1;
    private static readonly Encoding Cp1251 = CreateCp1251();
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private static readonly IReadOnlyDictionary<string, SettingDefinition> Definitions =
        new Dictionary<string, SettingDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["g_fov"] = new("g_fov", "Camera field of view", 30, 150),
            ["hud_fov"] = new("hud_fov", "HUD field of view", 0.2, 1.0),
            ["mouse_sens"] = new("mouse_sens", "Mouse sensitivity", 0.01, 1.0),
            ["hud_crosshair"] = new("hud_crosshair", "HUD crosshair", Toggle: true),
            ["hud_crosshair_dist"] = new("hud_crosshair_dist", "Crosshair distance", Toggle: true),
            ["hud_info"] = new("hud_info", "HUD information", Toggle: true),
            ["hud_weapon"] = new("hud_weapon", "Weapon HUD", Toggle: true),
            ["cl_dynamiccrosshair"] = new("cl_dynamiccrosshair", "Dynamic crosshair", Toggle: true),
        };

    public static string DefaultStateDirectory => AppPaths.ToolkitConfig;

    /// <summary>Returns the current and first-seen values. An absent original key means the game engine default was in effect.</summary>
    public static UserLtxSettingsInspection Inspect(string userLtxPath, string? stateDirectory = null)
    {
        var path = CanonicalConfigPath(userLtxPath);
        var text = ReadText(path, out _);
        var lines = ParseLines(text);
        var current = ReadKnownValues(lines);
        var manifest = ReadManifest(path, stateDirectory);
        var conflict = false;
        string? conflictMessage = null;
        var settings = new List<UserLtxSettingInfo>();
        foreach (var definition in Definitions.Values.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            current.TryGetValue(definition.Key, out var line);
            ManagedSetting? owned = null;
            if (manifest is not null) manifest.Settings.TryGetValue(definition.Key, out owned);
            var changedByToolkit = owned is not null && line?.RawLine == owned.OwnedLine;
            var hasConflict = owned is not null && !changedByToolkit;
            if (hasConflict)
            {
                conflict = true;
                conflictMessage ??= $"The managed setting {definition.Key} changed outside the toolkit.";
            }

            settings.Add(new UserLtxSettingInfo(
                definition.Key,
                definition.Meaning,
                line?.Value,
                owned?.OriginalLine is { } originalLine ? ParseValue(originalLine)?.Value : null,
                changedByToolkit,
                hasConflict));
        }

        return new UserLtxSettingsInspection(path, conflict, conflictMessage, settings.AsReadOnly());
    }

    public static UserLtxSettingsInspection SetOverrides(
        string userLtxPath,
        string? stateDirectory,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return Inspect(userLtxPath, stateDirectory);
        var path = CanonicalConfigPath(userLtxPath);
        var originalBytes = File.ReadAllBytes(path);
        var text = Decode(originalBytes, out var encoding);
        var lines = ParseLines(text);
        var current = ReadKnownValues(lines);
        var manifestPath = GetManifestPath(path, stateDirectory);
        var oldManifestBytes = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;
        var manifest = ReadManifest(path, stateDirectory) ?? new SettingsManifest(ManifestSchema, path, new Dictionary<string, ManagedSetting>(StringComparer.OrdinalIgnoreCase));
        EnsureNoDrift(manifest, current);

        var normalizedValues = NormalizeOverrides(values);

        foreach (var (key, value) in normalizedValues)
        {
            var definition = Definitions[key];
            current.TryGetValue(key, out var oldLine);
            manifest.Settings.TryGetValue(key, out var existing);
            // A null OriginalLine in the manifest is a fact ("the setting was absent"), not missing data: a second
            // override must keep it, or "restore default" would bring back our own first value.
            var originalLine = existing is not null ? existing.OriginalLine : oldLine?.RawLine;
            var nextText = SetLine(text, oldLine, definition.Key, value);
            text = nextText;
            lines = ParseLines(text);
            current = ReadKnownValues(lines);
            var ownedLine = current[key].RawLine;
            manifest.Settings[key] = new ManagedSetting(definition.Key, originalLine, ownedLine);
        }

        var nextBytes = Encode(text, encoding);
        try
        {
            AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), path, nextBytes, overwrite: true);
            WriteManifest(manifestPath, manifest);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            try
            {
                if (File.Exists(path) && Sha256(File.ReadAllBytes(path)) == Sha256(nextBytes))
                    AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), path, originalBytes, overwrite: true);
                if (oldManifestBytes is null) File.Delete(manifestPath);
                else AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), manifestPath, oldManifestBytes, overwrite: true);
            }
            catch (Exception rollbackFailure) when (rollbackFailure is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Settings change failed: {failure.Message}; rollback failed: {rollbackFailure.Message}", failure);
            }

            throw;
        }

        return Inspect(path, stateDirectory);
    }

    public static UserLtxSettingsInspection RestoreDefault(string userLtxPath, string? stateDirectory, string key)
    {
        if (!Definitions.TryGetValue(key, out var definition))
            throw new ArgumentException($"'{key}' is not an editable, known user.ltx setting.", nameof(key));
        var path = CanonicalConfigPath(userLtxPath);
        var bytes = File.ReadAllBytes(path);
        var text = Decode(bytes, out var encoding);
        var current = ReadKnownValues(ParseLines(text));
        var manifestPath = GetManifestPath(path, stateDirectory);
        var oldManifestBytes = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;
        var manifest = ReadManifest(path, stateDirectory)
            ?? throw new InvalidOperationException($"The setting {definition.Key} has no toolkit-owned default to restore.");
        EnsureNoDrift(manifest, current);
        if (!manifest.Settings.TryGetValue(definition.Key, out var setting))
            throw new InvalidOperationException($"The setting {definition.Key} is not changed by the toolkit.");
        current.TryGetValue(definition.Key, out var currentLine);
        text = setting.OriginalLine is { } originalLine
            ? SetRawLine(text, currentLine, originalLine)
            : RemoveLine(text, currentLine);
        manifest.Settings.Remove(definition.Key);
        var nextBytes = Encode(text, encoding);
        try
        {
            AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), path, nextBytes, overwrite: true);
            if (manifest.Settings.Count == 0) File.Delete(manifestPath);
            else WriteManifest(manifestPath, manifest);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            try
            {
                if (File.Exists(path) && Sha256(File.ReadAllBytes(path)) == Sha256(nextBytes))
                    AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), path, bytes, overwrite: true);
                if (oldManifestBytes is not null)
                    AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), manifestPath, oldManifestBytes, overwrite: true);
            }
            catch (Exception rollbackFailure) when (rollbackFailure is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Default restore failed: {failure.Message}; rollback failed: {rollbackFailure.Message}", failure);
            }

            throw;
        }

        return Inspect(path, stateDirectory);
    }

    public static string? ManagedManifestPath(string userLtxPath, string? stateDirectory = null)
    {
        var path = CanonicalConfigPath(userLtxPath);
        var manifestPath = GetManifestPath(path, stateDirectory);
        return File.Exists(manifestPath) ? manifestPath : null;
    }

    public static IReadOnlyDictionary<string, string> NormalizeOverrides(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var normalizedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (!Definitions.TryGetValue(pair.Key, out var definition))
                throw new ArgumentException($"'{pair.Key}' is not an editable, known user.ltx setting.", nameof(values));
            var normalized = ValidateValue(definition, pair.Value);
            if (!normalizedValues.TryAdd(definition.Key, normalized))
                throw new ArgumentException($"Setting '{definition.Key}' was supplied more than once.", nameof(values));
        }

        return normalizedValues;
    }

    /// <summary>Restores the provider's ownership record from a verified toolkit snapshot after its values are present.</summary>
    public static void RestoreOwnershipManifest(string userLtxPath, string? stateDirectory, byte[]? manifestBytes)
    {
        var path = CanonicalConfigPath(userLtxPath);
        var manifestPath = GetManifestPath(path, stateDirectory);
        if (manifestBytes is null)
        {
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            return;
        }

        var manifest = JsonSerializer.Deserialize(manifestBytes, UserLtxJsonContext.Default.SettingsManifest)
            ?? throw new InvalidDataException("The saved user.ltx ownership record is empty.");
        if (manifest.SchemaVersion != ManifestSchema || !PathEquals(manifest.FilePath, path) || manifest.Settings is null)
            throw new InvalidDataException("The saved user.ltx ownership record does not match this file.");
        var current = ReadKnownValues(ParseLines(Decode(File.ReadAllBytes(path), out _)));
        foreach (var (key, setting) in manifest.Settings)
        {
            if (!Definitions.ContainsKey(key) || setting is null || !string.Equals(key, setting.Key, StringComparison.OrdinalIgnoreCase) ||
                !current.TryGetValue(key, out var line) || !string.Equals(line.RawLine, setting.OwnedLine, StringComparison.Ordinal))
                throw new InvalidOperationException($"The saved user.ltx setting {key} does not match the current file.");
            if (setting.OriginalLine is { } original &&
                !string.Equals(ParseValue(original)?.Key, key, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The saved original value for {key} is malformed.");
        }
        WriteManifest(manifestPath, manifest);
    }

    private static void EnsureNoDrift(SettingsManifest manifest, IReadOnlyDictionary<string, ParsedLine> current)
    {
        foreach (var setting in manifest.Settings.Values)
        {
            if (!current.TryGetValue(setting.Key, out var line) || line.RawLine != setting.OwnedLine)
                throw new InvalidOperationException($"The managed setting {setting.Key} changed outside the toolkit; review the conflict before editing.");
        }
    }

    private static SettingsManifest? ReadManifest(string path, string? stateDirectory)
    {
        var manifestPath = GetManifestPath(path, stateDirectory);
        if (!File.Exists(manifestPath)) return null;
        var manifest = JsonSerializer.Deserialize(File.ReadAllBytes(manifestPath), UserLtxJsonContext.Default.SettingsManifest)
            ?? throw new InvalidDataException("The toolkit user.ltx manifest is empty.");
        if (manifest.SchemaVersion != ManifestSchema || !PathEquals(manifest.FilePath, path) || manifest.Settings is null)
            throw new InvalidDataException("The toolkit user.ltx manifest does not match this file.");
        foreach (var (key, value) in manifest.Settings)
        {
            if (!Definitions.ContainsKey(key) || value is null || !string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The toolkit user.ltx manifest contains an unknown setting.");
        }

        return manifest;
    }

    private static void WriteManifest(string path, SettingsManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicGameFileWriter.Write(new PhysicalGameFileSystem(), path,
            JsonSerializer.SerializeToUtf8Bytes(manifest, UserLtxJsonContext.Default.SettingsManifest), overwrite: true);
    }

    private static string GetManifestPath(string path, string? stateDirectory)
    {
        var root = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(stateDirectory ?? DefaultStateDirectory));
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
        return Path.Combine(root, identity + ".json");
    }

    private static string CanonicalConfigPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(path));
        if (!string.Equals(Path.GetFileName(fullPath), "user.ltx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose the game's existing user.ltx file.", nameof(path));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The selected user.ltx file does not exist.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing to edit a linked user.ltx file.");
        return fullPath;
    }

    private static Dictionary<string, ParsedLine> ReadKnownValues(IReadOnlyList<LinePart> lines)
    {
        var result = new Dictionary<string, ParsedLine>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Count; index++)
        {
            var parsed = ParseValue(lines[index].Content);
            if (parsed is null || !Definitions.ContainsKey(parsed.Key)) continue;
            if (!result.TryAdd(parsed.Key, new ParsedLine(parsed.Key, parsed.Value, lines[index].Content, index)))
                throw new InvalidDataException($"The user.ltx contains more than one {parsed.Key} command; no change was made.");
        }

        return result;
    }

    private static ParsedValue? ParseValue(string line)
    {
        var match = SettingLineRegex().Match(line);
        return match.Success
            ? new ParsedValue(match.Groups["key"].Value, match.Groups["value"].Value)
            : null;
    }

    private static string SetLine(string text, ParsedLine? current, string key, string value)
    {
        if (current is null) return AppendLine(text, $"{key} {value}");
        var content = ParseLines(text)[current.Index];
        var match = SettingLineRegex().Match(content.Content);
        var replacement = content.Content[..match.Groups["value"].Index] + value + content.Content[(match.Groups["value"].Index + match.Groups["value"].Length)..];
        return ReplaceLine(text, content, replacement);
    }

    private static string SetRawLine(string text, ParsedLine? current, string originalLine) =>
        current is null ? AppendLine(text, originalLine) : ReplaceLine(text, ParseLines(text)[current.Index], originalLine);

    private static string RemoveLine(string text, ParsedLine? current)
    {
        if (current is null) return text;
        var line = ParseLines(text)[current.Index];
        var length = line.Content.Length + line.Ending.Length;
        return text.Remove(line.Start, length);
    }

    private static string ReplaceLine(string text, LinePart line, string replacement) =>
        text[..line.Start] + replacement + text[(line.Start + line.Content.Length)..];

    private static string AppendLine(string text, string newLine)
    {
        var ending = DetectNewline(text);
        if (text.Length == 0) return newLine;
        return EndsInNewline(text) ? text + newLine + ending : text + ending + newLine;
    }

    private static List<LinePart> ParseLines(string text)
    {
        var result = new List<LinePart>();
        var start = 0;
        while (start < text.Length)
        {
            var end = start;
            while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
            var content = text[start..end];
            var endingLength = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : end < text.Length ? 1 : 0;
            var ending = endingLength switch { 2 => "\r\n", 1 when text[end] == '\r' => "\r", 1 => "\n", _ => string.Empty };
            result.Add(new LinePart(start, content, ending));
            start = end + endingLength;
        }

        if (text.Length == 0) result.Add(new LinePart(0, string.Empty, string.Empty));
        return result;
    }

    private static string ValidateValue(SettingDefinition definition, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Any(character => char.IsWhiteSpace(character) || character is ';' or '#' or '\r' or '\n'))
            throw new ArgumentException($"The value for {definition.Key} must be one token.", nameof(value));
        if (definition.Toggle)
        {
            if (normalized is not ("on" or "off"))
                throw new ArgumentException($"The value for {definition.Key} must be 'on' or 'off'.", nameof(value));
            return normalized;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) ||
            number < definition.Minimum || number > definition.Maximum)
            throw new ArgumentException($"The value for {definition.Key} must be between {definition.Minimum} and {definition.Maximum}.", nameof(value));
        return normalized;
    }

    private static string ReadText(string path, out Encoding encoding) => Decode(File.ReadAllBytes(path), out encoding);

    private static string Decode(byte[] bytes, out Encoding encoding)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            encoding = Utf8;
            return encoding.GetString(bytes, Encoding.UTF8.Preamble.Length, bytes.Length - Encoding.UTF8.Preamble.Length);
        }

        encoding = Cp1251;
        return encoding.GetString(bytes);
    }

    /// <summary>
    /// The inverse of <see cref="Decode"/>: a file read as UTF-8 was recognised by its BOM, so the BOM is written back
    /// (GetBytes never emits it). Without it the next read would take the same bytes for CP1251.
    /// </summary>
    private static byte[] Encode(string text, Encoding encoding) =>
        ReferenceEquals(encoding, Utf8) ? [.. Encoding.UTF8.Preamble, .. encoding.GetBytes(text)] : encoding.GetBytes(text);

    private static Encoding CreateCp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static bool EndsInNewline(string value) => value.EndsWith('\n') || value.EndsWith('\r');

    private static string DetectNewline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
        : text.Contains('\r') ? "\r" : "\n";

    private static bool PathEquals(string left, string right) =>
        string.Equals(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(left)), SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [GeneratedRegex("^(?<indent>\\s*)(?<key>[A-Za-z_][A-Za-z0-9_]*)(?<separator>\\s*=\\s*|\\s+)(?<value>[^\\s;#]+)(?<suffix>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SettingLineRegex();

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(SettingsManifest), TypeInfoPropertyName = "SettingsManifest")]
    private partial class UserLtxJsonContext : JsonSerializerContext { }

    private sealed record SettingDefinition(string Key, string Meaning, double Minimum = 0, double Maximum = 0, bool Toggle = false);
    private sealed record ParsedValue(string Key, string Value);
    private sealed record ParsedLine(string Key, string Value, string RawLine, int Index);
    private sealed record LinePart(int Start, string Content, string Ending);
    private sealed record SettingsManifest(int SchemaVersion, string FilePath, Dictionary<string, ManagedSetting> Settings);
    private sealed record ManagedSetting(string Key, string? OriginalLine, string OwnedLine);
}
