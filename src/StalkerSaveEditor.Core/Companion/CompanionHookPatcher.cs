using System.Text;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Companion;

internal static partial class CompanionHookPatcher
{
    private const string UpdateAnchor = "object_binder.update(self, delta)";
    private const string UpdateHook = "if save_editor_companion then save_editor_companion.update() end";
    private const string UseItemAnchor = "function actor_binder:use_inventory_item(obj)";
    private const string UseItemHook = "if save_editor_companion then save_editor_companion.on_use(obj) end";
    private const string UseObjectAnchor = "self.object:set_callback(callback.on_item_drop, self.on_item_drop, self)";
    private const string UseObjectHook = "if save_editor_companion then self.object:set_callback(callback.use_object, function(_, obj) save_editor_companion.on_use(obj) end) end";
    private const string MenuFunctionAnchor = "function main_menu:OnKeyboard";
    private const string MenuHook = "if save_editor_companion_ui then save_editor_companion_ui.on_menu_key(dik, self) end";
    private const string QuestInclude = "#include \"save_editor_companion.ltx\"";

    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static byte[] PatchBindStalker(byte[] original, CompanionGame game)
    {
        var text = Latin1.GetString(original);
        text = InsertAfterAnchorLine(text, UpdateAnchor, UpdateHook, "scripts/bind_stalker.script");
        text = game == CompanionGame.CallOfPripyat
            ? InsertAfterAnchorLine(
                text,
                UseItemAnchor,
                UseItemHook,
                "scripts/bind_stalker.script",
                indentFromFollowingLine: true)
            : InsertAfterAnchorLine(text, UseObjectAnchor, UseObjectHook, "scripts/bind_stalker.script");
        return Latin1.GetBytes(text);
    }

    public static byte[] PatchMainMenu(byte[] original)
    {
        var text = Latin1.GetString(original);
        var functionStart = FindUnique(text, MenuFunctionAnchor, "scripts/ui_main_menu.script");
        var functionEnd = FindMatchingEnd(text, functionStart, "scripts/ui_main_menu.script");
        var functionRegion = text.AsSpan(functionStart, functionEnd - functionStart);
        var windowStart = FindRegexUnique(
            functionRegion,
            WindowKeyRegex(),
            functionStart,
            "scripts/ui_main_menu.script");
        var windowEnd = FindMatchingEnd(text, windowStart, "scripts/ui_main_menu.script");
        if (!QuitKeyRegex().IsMatch(text.AsSpan(windowStart, windowEnd - windowStart)))
        {
            // Shadow of Chornobyl EE handles Q in an elseif branch: hook the first line of the key-press block.
            text = InsertAfterLine(text, windowStart, MenuHook, "scripts/ui_main_menu.script");
            return Latin1.GetBytes(text);
        }

        var quitStart = FindRegexUnique(
            text.AsSpan(windowStart, windowEnd - windowStart),
            QuitKeyRegex(),
            windowStart,
            "scripts/ui_main_menu.script");
        var quitEnd = FindMatchingEnd(text, quitStart, "scripts/ui_main_menu.script");
        if (quitEnd >= windowEnd)
        {
            throw AnchorError("scripts/ui_main_menu.script", "DIK_Q block is outside WINDOW_KEY_PRESSED");
        }

        text = InsertAfterBlockLine(text, quitEnd, MenuHook, "scripts/ui_main_menu.script");
        return Latin1.GetBytes(text);
    }

    public static byte[] PatchQuestItems(byte[] original, string filePath)
    {
        var text = Latin1.GetString(original);
        var includeCount = Count(text, QuestInclude);
        if (includeCount > 0)
        {
            if (includeCount == 1 && string.Equals(LastNonEmptyLine(text), QuestInclude, StringComparison.Ordinal))
            {
                return original;
            }

            throw AnchorError(filePath, "companion include already exists outside the end of file");
        }

        var newline = FindNewline(text);
        if (text.Length > 0 && !EndsInNewline(text))
        {
            text += newline;
        }

        text += QuestInclude + newline;
        return Latin1.GetBytes(text);
    }

    public static byte[] RemoveBindStalkerHooks(byte[] installed, CompanionGame game)
    {
        var text = Latin1.GetString(installed);
        text = RemoveAfterAnchorLine(text, UpdateAnchor, UpdateHook, "scripts/bind_stalker.script");
        text = game == CompanionGame.CallOfPripyat
            ? RemoveAfterAnchorLine(text, UseItemAnchor, UseItemHook, "scripts/bind_stalker.script")
            : RemoveAfterAnchorLine(text, UseObjectAnchor, UseObjectHook, "scripts/bind_stalker.script");
        return Latin1.GetBytes(text);
    }

    public static byte[] RemoveMainMenuHook(byte[] installed)
    {
        var text = Latin1.GetString(installed);
        var functionStart = FindUnique(text, MenuFunctionAnchor, "scripts/ui_main_menu.script");
        var functionEnd = FindMatchingEnd(text, functionStart, "scripts/ui_main_menu.script");
        var functionRegion = text.AsSpan(functionStart, functionEnd - functionStart);
        var windowStart = FindRegexUnique(
            functionRegion,
            WindowKeyRegex(),
            functionStart,
            "scripts/ui_main_menu.script");
        var windowEnd = FindMatchingEnd(text, windowStart, "scripts/ui_main_menu.script");
        if (!QuitKeyRegex().IsMatch(text.AsSpan(windowStart, windowEnd - windowStart)))
        {
            text = RemoveAfterLine(text, windowStart, MenuHook, "scripts/ui_main_menu.script");
            return Latin1.GetBytes(text);
        }

        var quitStart = FindRegexUnique(
            text.AsSpan(windowStart, windowEnd - windowStart),
            QuitKeyRegex(),
            windowStart,
            "scripts/ui_main_menu.script");
        var quitEnd = FindMatchingEnd(text, quitStart, "scripts/ui_main_menu.script");
        text = RemoveAfterBlockLine(text, quitEnd, MenuHook, "scripts/ui_main_menu.script");
        return Latin1.GetBytes(text);
    }

    public static byte[] RemoveQuestInclude(byte[] installed, string filePath)
    {
        var text = Latin1.GetString(installed);
        if (Count(text, QuestInclude) != 1 || !string.Equals(LastNonEmptyLine(text), QuestInclude, StringComparison.Ordinal))
        {
            throw AnchorError(filePath, "installed companion include is missing or ambiguous");
        }

        var lineStart = text.LastIndexOf(QuestInclude, StringComparison.Ordinal);
        var lineEnd = lineStart + QuestInclude.Length;
        if (lineEnd < text.Length && text.AsSpan(lineEnd).StartsWith("\r\n", StringComparison.Ordinal))
        {
            lineEnd += 2;
        }
        else if (lineEnd < text.Length && text[lineEnd] is '\r' or '\n')
        {
            lineEnd++;
        }

        text = text.Remove(lineStart, lineEnd - lineStart);
        return Latin1.GetBytes(text);
    }

    private static string InsertAfterAnchorLine(
        string text,
        string anchor,
        string hook,
        string file,
        bool indentFromFollowingLine = false)
    {
        var anchorIndex = FindUnique(text, anchor, file);
        if (Count(text, hook) > 0)
        {
            if (Count(text, hook) == 1 && string.Equals(NextLineAfterAnchor(text, anchorIndex), hook, StringComparison.Ordinal))
            {
                return text;
            }

            throw AnchorError(file, "companion hook already exists at an unexpected location");
        }

        var lineStart = text.LastIndexOf('\n', Math.Max(anchorIndex - 1, 0));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = FindLineContentEnd(text, anchorIndex);
        var prefix = text.AsSpan(lineStart, anchorIndex - lineStart);
        var suffix = text.AsSpan(anchorIndex + anchor.Length, lineEnd - anchorIndex - anchor.Length);
        if (!IsIndent(prefix) || !IsWhitespace(suffix))
        {
            throw AnchorError(file, $"anchor '{anchor}' is not a standalone line");
        }

        var newline = FindNewline(text);
        var indentation = indentFromFollowingLine
            ? FindFollowingLineIndentation(text, lineEnd, file)
            : prefix.ToString();
        return text.Insert(lineEnd, newline + indentation + hook);
    }

    private static string FindFollowingLineIndentation(string text, int anchorLineEnd, string file)
    {
        var lineStart = anchorLineEnd + NewlineLengthAt(text, anchorLineEnd);
        while (lineStart < text.Length)
        {
            var lineEnd = FindLineContentEnd(text, lineStart);
            var line = text.AsSpan(lineStart, lineEnd - lineStart);
            var contentStart = 0;
            while (contentStart < line.Length && line[contentStart] is ' ' or '\t')
            {
                contentStart++;
            }

            if (contentStart < line.Length)
            {
                return line[..contentStart].ToString();
            }

            var newlineLength = NewlineLengthAt(text, lineEnd);
            if (newlineLength == 0)
            {
                break;
            }

            lineStart = lineEnd + newlineLength;
        }

        throw AnchorError(file, "could not determine indentation for the first function body line");
    }

    /// <summary>Inserts the hook after the line that starts at <paramref name="lineMatchIndex"/> (a regex match of that line).</summary>
    private static string InsertAfterLine(string text, int lineMatchIndex, string hook, string file) =>
        InsertAfterAnchorLine(text, LineAt(text, lineMatchIndex), hook, file, indentFromFollowingLine: true);

    private static string RemoveAfterLine(string text, int lineMatchIndex, string hook, string file) =>
        RemoveAfterAnchorLine(text, LineAt(text, lineMatchIndex), hook, file);

    private static string LineAt(string text, int index) =>
        text[index..FindLineContentEnd(text, index)].TrimEnd();

    private static string RemoveAfterAnchorLine(string text, string anchor, string hook, string file)
    {
        var anchorIndex = FindUnique(text, anchor, file);
        if (Count(text, hook) != 1 || !string.Equals(NextLineAfterAnchor(text, anchorIndex), hook, StringComparison.Ordinal))
        {
            throw AnchorError(file, "installed companion hook is missing or ambiguous");
        }

        var lineEnd = FindLineContentEnd(text, anchorIndex);
        var newlineLength = NewlineLengthAt(text, lineEnd);
        var hookStart = lineEnd + newlineLength;
        var hookLineEnd = FindLineContentEnd(text, hookStart);
        return text.Remove(lineEnd, hookLineEnd - lineEnd);
    }

    private static string InsertAfterBlockLine(string text, int endKeywordStart, string hook, string file)
    {
        if (Count(text, hook) > 0)
        {
            var lineEnd = FindLineContentEnd(text, endKeywordStart);
            var afterLine = lineEnd + NewlineLengthAt(text, lineEnd);
            if (Count(text, hook) == 1 && string.Equals(NextLine(text, afterLine), hook, StringComparison.Ordinal))
            {
                return text;
            }

            throw AnchorError(file, "companion keyboard hook already exists at an unexpected location");
        }

        var start = text.LastIndexOf('\n', Math.Max(endKeywordStart - 1, 0));
        start = start < 0 ? 0 : start + 1;
        var end = FindLineContentEnd(text, endKeywordStart);
        if (!IsIndent(text.AsSpan(start, endKeywordStart - start)) ||
            !IsWhitespace(text.AsSpan(endKeywordStart + "end".Length, end - endKeywordStart - "end".Length)))
        {
            throw AnchorError(file, "DIK_Q block end is not a standalone line");
        }

        var indentation = text.AsSpan(start, endKeywordStart - start).ToString();
        var newlineLength = NewlineLengthAt(text, end);
        var newline = NewlineAt(text, end);
        if (newlineLength == 0)
        {
            return text + newline + indentation + hook + newline;
        }

        var insertionPoint = end + newlineLength;
        return text.Insert(insertionPoint, indentation + hook + newline);
    }

    private static string RemoveAfterBlockLine(string text, int endKeywordStart, string hook, string file)
    {
        var end = FindLineContentEnd(text, endKeywordStart);
        var lineBreakLength = NewlineLengthAt(text, end);
        var hookStart = end + lineBreakLength;
        if (Count(text, hook) != 1 || !string.Equals(NextLine(text, hookStart), hook, StringComparison.Ordinal))
        {
            throw AnchorError(file, "installed keyboard hook is missing or ambiguous");
        }

        var hookLineEnd = FindLineContentEnd(text, hookStart);
        var hookLineBreak = NewlineLengthAt(text, hookLineEnd);
        var removeStart = lineBreakLength == 0 ? end : hookStart;
        var removeEnd = hookLineEnd + hookLineBreak;
        return text.Remove(removeStart, removeEnd - removeStart);
    }

    private static int FindMatchingEnd(string text, int start, string file)
    {
        var depth = 0;
        var pendingDo = 0;
        foreach (var token in LuaBlocks(text, start))
        {
            switch (token.Value)
            {
                case "if":
                case "function":
                case "repeat":
                    depth++;
                    break;
                case "for":
                case "while":
                    depth++;
                    pendingDo++;
                    break;
                case "do":
                    if (pendingDo > 0)
                    {
                        pendingDo--;
                    }
                    else
                    {
                        depth++;
                    }

                    break;
                case "end":
                case "until":
                    depth--;
                    if (depth == 0)
                    {
                        return token.Start;
                    }

                    if (depth < 0)
                    {
                        throw AnchorError(file, "Lua block nesting is invalid near the requested hook");
                    }

                    break;
            }
        }

        throw AnchorError(file, "could not find the end of the required Lua block");
    }

    private static IEnumerable<(string Value, int Start)> LuaBlocks(string text, int start)
    {
        for (var index = start; index < text.Length;)
        {
            if (text[index] == '-' && index + 1 < text.Length && text[index + 1] == '-')
            {
                index = SkipLuaComment(text, index + 2);
                continue;
            }

            if (text[index] is '\'' or '"')
            {
                index = SkipLuaString(text, index);
                continue;
            }

            if (!char.IsLetter(text[index]) && text[index] != '_')
            {
                index++;
                continue;
            }

            var tokenStart = index++;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
            {
                index++;
            }

            var word = text[tokenStart..index];
            if (word is "if" or "function" or "for" or "while" or "do" or "end" or "repeat" or "until")
            {
                yield return (word, tokenStart);
            }
        }
    }

    private static int SkipLuaComment(string text, int index)
    {
        if (index + 1 < text.Length && text[index] == '[' && text[index + 1] == '[')
        {
            var end = text.IndexOf("]]", index + 2, StringComparison.Ordinal);
            return end < 0 ? text.Length : end + 2;
        }

        var lineEnd = text.IndexOfAny(['\r', '\n'], index);
        return lineEnd < 0 ? text.Length : lineEnd;
    }

    private static int SkipLuaString(string text, int index)
    {
        var quote = text[index++];
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index += Math.Min(2, text.Length - index);
            }
            else if (text[index++] == quote)
            {
                break;
            }
        }

        return index;
    }

    private static int FindUnique(string text, string anchor, string file)
    {
        var index = text.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0 || text.IndexOf(anchor, index + anchor.Length, StringComparison.Ordinal) >= 0)
        {
            throw AnchorError(file, $"anchor '{anchor}' was not found exactly once");
        }

        return index;
    }

    private static int FindRegexUnique(ReadOnlySpan<char> region, Regex regex, int absoluteStart, string file)
    {
        var matches = regex.Matches(region.ToString());
        if (matches.Count != 1)
        {
            throw AnchorError(file, $"anchor '{regex}' was not found exactly once");
        }

        return absoluteStart + matches[0].Index;
    }

    private static string? NextLineAfterAnchor(string text, int anchorStart)
    {
        var anchorLineEnd = FindLineContentEnd(text, anchorStart);
        var newlineLength = NewlineLengthAt(text, anchorLineEnd);
        return newlineLength == 0 ? null : NextLine(text, anchorLineEnd + newlineLength);
    }

    private static string? NextLine(string text, int lineStart)
    {
        if (lineStart >= text.Length)
        {
            return null;
        }

        var lineEnd = FindLineContentEnd(text, lineStart);
        return text.AsSpan(lineStart, lineEnd - lineStart).Trim().ToString();
    }

    private static int FindLineContentEnd(string text, int position)
    {
        var end = text.IndexOfAny(['\r', '\n'], position);
        return end < 0 ? text.Length : end;
    }

    private static int NewlineLengthAt(string text, int position) =>
        position >= text.Length ? 0 : text[position] == '\r' && position + 1 < text.Length && text[position + 1] == '\n'
            ? 2
            : text[position] is '\r' or '\n' ? 1 : 0;

    private static string NewlineAt(string text, int position) =>
        NewlineLengthAt(text, position) == 2 ? "\r\n" : NewlineLengthAt(text, position) == 1 ? text[position].ToString() : FindNewline(text);

    private static string FindNewline(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? "\n" : text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n'
            ? "\r\n" : text[index].ToString();
    }

    private static bool EndsInNewline(string text) => text.Length > 0 && text[^1] is '\r' or '\n';

    private static string? LastNonEmptyLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();

    private static bool IsIndent(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWhitespace(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var position = 0; (position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0; position += value.Length)
        {
            count++;
        }

        return count;
    }

    private static CompanionInstallerException AnchorError(string file, string detail) =>
        new($"Cannot install companion hook in {file}: {detail}.", file);

    [GeneratedRegex(@"\bif\s+keyboard_action\s*==\s*(?:ui_events\s*\.\s*)?WINDOW_KEY_PRESSED\s+then\b", RegexOptions.CultureInvariant)]
    private static partial Regex WindowKeyRegex();

    [GeneratedRegex(@"\bif\s+dik\s*==\s*(?:DIK_keys\s*\.\s*)?DIK_Q\s+then\b", RegexOptions.CultureInvariant)]
    private static partial Regex QuitKeyRegex();
}
