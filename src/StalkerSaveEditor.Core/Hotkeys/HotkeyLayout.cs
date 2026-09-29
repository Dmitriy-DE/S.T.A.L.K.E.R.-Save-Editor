using System.Collections.ObjectModel;
using System.Globalization;

namespace StalkerSaveEditor.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
}

public enum CompanionHotkeyAction
{
    Heal,
    RepairEquipped,
    Mark,
    JumpLast,
    QuickSave,
}

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, char Key)
{
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(char.ToUpperInvariant(Key).ToString(CultureInfo.InvariantCulture));
        return string.Join('+', parts);
    }

    public static HotkeyGesture Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new HotkeyLayoutException($"Hotkey '{text}' must include a modifier and one letter.");
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var modifier in parts[..^1])
        {
            var bit = modifier.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                _ => throw new HotkeyLayoutException($"Unknown hotkey modifier '{modifier}'."),
            };
            if (modifiers.HasFlag(bit))
            {
                throw new HotkeyLayoutException($"Hotkey '{text}' repeats modifier '{modifier}'.");
            }

            modifiers |= bit;
        }

        var keyText = parts[^1];
        if (keyText.Length != 1 || !char.IsAsciiLetter(keyText[0]))
        {
            throw new HotkeyLayoutException($"Hotkey '{text}' must end in one A-Z letter.");
        }

        return new HotkeyGesture(modifiers, char.ToUpperInvariant(keyText[0]));
    }
}

public sealed record CompanionHotkeyBinding(CompanionHotkeyAction Action, HotkeyGesture Gesture);

public sealed class HotkeyLayout
{
    private static readonly ReadOnlyDictionary<string, CompanionHotkeyAction> ActionNames =
        new ReadOnlyDictionary<string, CompanionHotkeyAction>(new Dictionary<string, CompanionHotkeyAction>(StringComparer.Ordinal)
        {
            ["heal"] = CompanionHotkeyAction.Heal,
            ["repair_equipped"] = CompanionHotkeyAction.RepairEquipped,
            ["mark"] = CompanionHotkeyAction.Mark,
            ["jump_last"] = CompanionHotkeyAction.JumpLast,
            ["quicksave"] = CompanionHotkeyAction.QuickSave,
        });

    private HotkeyLayout(IReadOnlyList<CompanionHotkeyBinding> bindings)
    {
        Bindings = bindings;
    }

    public IReadOnlyList<CompanionHotkeyBinding> Bindings { get; }

    public static string ActionName(CompanionHotkeyAction action) =>
        ActionNames.First(pair => pair.Value == action).Key;

    /// <summary>The same action=Ctrl+Key text that <see cref="Parse"/> reads.</summary>
    public string ToText() =>
        string.Concat(Bindings.Select(binding => ActionName(binding.Action) + "=" + binding.Gesture + "\n"));

    /// <summary>The user's layout from <paramref name="path"/>, or the default when the file is missing or invalid.</summary>
    public static HotkeyLayout Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HotkeyLayoutException)
        {
            return Default;
        }
    }

    /// <summary>Atomic write of <see cref="ToText"/>.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToText());
        File.Move(temp, path, overwrite: true);
    }

    public static string DefaultPath => Path.Combine(StalkerSaveEditor.Core.Diagnostics.AppPaths.DataDirectory, "hotkeys.txt");

    public static HotkeyLayout Default { get; } = new(Array.AsReadOnly<CompanionHotkeyBinding>(
    [
        new(CompanionHotkeyAction.Heal, HotkeyGesture.Parse("Ctrl+H")),
        new(CompanionHotkeyAction.RepairEquipped, HotkeyGesture.Parse("Ctrl+R")),
        new(CompanionHotkeyAction.Mark, HotkeyGesture.Parse("Ctrl+M")),
        new(CompanionHotkeyAction.JumpLast, HotkeyGesture.Parse("Ctrl+J")),
        new(CompanionHotkeyAction.QuickSave, HotkeyGesture.Parse("Ctrl+S")),
    ]));

    public static HotkeyLayout Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bindings = new List<CompanionHotkeyBinding>();
        var actions = new HashSet<CompanionHotkeyAction>();
        var gestures = new HashSet<HotkeyGesture>();
        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0 || separator == trimmed.Length - 1)
            {
                throw new HotkeyLayoutException($"Hotkey line {lineNumber} must use action=Ctrl+Key syntax.");
            }

            var actionName = trimmed[..separator].Trim();
            if (!ActionNames.TryGetValue(actionName, out var action))
            {
                throw new HotkeyLayoutException($"Unknown hotkey action '{actionName}' on line {lineNumber}.");
            }

            if (!actions.Add(action))
            {
                throw new HotkeyLayoutException($"Hotkey action '{actionName}' is configured more than once.");
            }

            HotkeyGesture gesture;
            try
            {
                gesture = HotkeyGesture.Parse(trimmed[(separator + 1)..].Trim());
            }
            catch (HotkeyLayoutException exception)
            {
                throw new HotkeyLayoutException($"Invalid hotkey on line {lineNumber}: {exception.Message}");
            }

            if (!gestures.Add(gesture))
            {
                throw new HotkeyLayoutException($"Hotkey '{gesture}' is assigned more than once.");
            }

            bindings.Add(new CompanionHotkeyBinding(action, gesture));
        }

        if (bindings.Count == 0)
        {
            throw new HotkeyLayoutException("At least one hotkey binding is required.");
        }

        return new HotkeyLayout(bindings.AsReadOnly());
    }
}

public sealed class HotkeyLayoutException(string message) : FormatException(message);
