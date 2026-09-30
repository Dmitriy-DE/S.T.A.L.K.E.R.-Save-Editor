using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StalkerSaveEditor.Desktop.Styles;

public static class StalkerTheme
{
    private static Color _bgBase = Color.Parse("#0C0D0A");
    private static Color _bgPanel = Color.Parse("#101311");
    private static Color _bgElevated = Color.Parse("#151814");
    private static Color _bgHover = Color.Parse("#23261F");
    private static Color _bgInput = Color.Parse("#1A1D17");
    private static Color _plateBg = Color.Parse("#161A14");
    private static Color _borderSubtle = Color.Parse("#242922");
    private static Color _border = Color.Parse("#33382F");
    private static Color _borderMetal = Color.Parse("#3D4837");
    private static Color _accent = Color.Parse("#D6A62D");
    private static Color _accentDim = Color.Parse("#8F6F22");
    private static Color _accentHover = Color.Parse("#E5B53C");
    private static Color _accentForeground = Color.Parse("#0C0D0A");
    private static Color _rust = Color.Parse("#A9532F");
    private static Color _success = Color.Parse("#7BCB62");
    private static Color _warning = Color.Parse("#D6A62D");
    private static Color _danger = Color.Parse("#D85A45");
    private static Color _textPrimary = Color.Parse("#D8D2BE");
    private static Color _textSecondary = Color.Parse("#A29D90");
    private static Color _textMuted = Color.Parse("#716F67");
    private static Color _textKhaki = Color.Parse("#D8BA8C");

    public static Color BgBase => _bgBase;
    public static Color BgPanel => _bgPanel;
    public static Color BgElevated => _bgElevated;
    public static Color BgHover => _bgHover;
    public static Color BgInput => _bgInput;
    public static Color PlateBg => _plateBg;
    public static Color BorderSubtle => _borderSubtle;
    public static Color Border => _border;
    public static Color BorderMetal => _borderMetal;
    public static Color BorderFocus => _accent;
    public static Color AccentAmber => _accent;
    public static Color AccentDim => _accentDim;
    public static Color Rust => _rust;
    public static Color Success => _success;
    public static Color Warning => _warning;
    public static Color Danger => _danger;
    public static Color TextPrimary => _textPrimary;
    public static Color TextSecondary => _textSecondary;
    public static Color TextMuted => _textMuted;
    public static Color TextKhaki => _textKhaki;

    public static string CurrentThemeId { get; private set; } = "zone";
    public static string CurrentAccentId { get; private set; } = "amber";
    public static int CurrentUiScalePercent { get; private set; } = 100;

    // Existing views keep references to these brushes. Mutating them makes a theme change live.
    public static readonly IBrush BrushBgBase = new SolidColorBrush(_bgBase);
    public static readonly IBrush BrushBgPanel = new SolidColorBrush(_bgPanel);
    public static readonly IBrush BrushBgElevated = new SolidColorBrush(_bgElevated);
    public static readonly IBrush BrushBgHover = new SolidColorBrush(_bgHover);
    public static readonly IBrush BrushBgInput = new SolidColorBrush(_bgInput);
    public static readonly IBrush BrushPlateBg = new SolidColorBrush(_plateBg);
    public static readonly IBrush BrushBorderSubtle = new SolidColorBrush(_borderSubtle);
    public static readonly IBrush BrushBorder = new SolidColorBrush(_border);
    public static readonly IBrush BrushBorderMetal = new SolidColorBrush(_borderMetal);
    public static readonly IBrush BrushBorderFocus = new SolidColorBrush(_accent);
    public static readonly IBrush BrushAccentAmber = new SolidColorBrush(_accent);
    public static readonly IBrush BrushAccentDim = new SolidColorBrush(_accentDim);
    public static readonly IBrush BrushAccentHover = new SolidColorBrush(_accentHover);
    public static readonly IBrush BrushAccentForeground = new SolidColorBrush(_accentForeground);
    public static readonly IBrush BrushSuccess = new SolidColorBrush(_success);
    public static readonly IBrush BrushWarning = new SolidColorBrush(_warning);
    public static readonly IBrush BrushDanger = new SolidColorBrush(_danger);
    public static readonly IBrush BrushTextPrimary = new SolidColorBrush(_textPrimary);
    public static readonly IBrush BrushTextSecondary = new SolidColorBrush(_textSecondary);
    public static readonly IBrush BrushTextMuted = new SolidColorBrush(_textMuted);
    public static readonly IBrush BrushTextKhaki = new SolidColorBrush(_textKhaki);

    /// <summary>Applies user-selected appearance values without replacing brushes already used by views.</summary>
    public static void ApplyAppearance(string? themeId, string? accentId, int uiScalePercent)
    {
        CurrentThemeId = themeId is "clear-sky" or "day" ? themeId : "zone";
        CurrentAccentId = accentId is "teal" or "blue" or "rust" ? accentId : "amber";
        CurrentUiScalePercent = uiScalePercent is 125 or 150 ? uiScalePercent : 100;

        var palette = CurrentThemeId switch
        {
            "clear-sky" => new Palette(
                "#0C1517", "#111D1F", "#17272A", "#203437", "#172326", "#182629",
                "#263739", "#35484A", "#456064", "#DCE5DC", "#AABBB7", "#7F928D", "#BED1C4",
                "#82CA8B", "#D5AA52", "#E06C59"),
            "day" => new Palette(
                "#D8D8D0", "#F0F0E9", "#FAFAF4", "#E4E6DD", "#FFFFFF", "#E8E9E1",
                "#C5C9BE", "#AEB5A8", "#8F998D", "#202720", "#4B564E", "#68736B", "#46584D",
                "#347345", "#8A610C", "#A43F32"),
            _ => new Palette(
                "#0C0D0A", "#101311", "#151814", "#23261F", "#1A1D17", "#161A14",
                "#242922", "#33382F", "#3D4837", "#D8D2BE", "#A29D90", "#716F67", "#D8BA8C",
                "#7BCB62", "#D6A62D", "#D85A45"),
        };

        _bgBase = Color.Parse(palette.BgBase);
        _bgPanel = Color.Parse(palette.BgPanel);
        _bgElevated = Color.Parse(palette.BgElevated);
        _bgHover = Color.Parse(palette.BgHover);
        _bgInput = Color.Parse(palette.BgInput);
        _plateBg = Color.Parse(palette.PlateBg);
        _borderSubtle = Color.Parse(palette.BorderSubtle);
        _border = Color.Parse(palette.Border);
        _borderMetal = Color.Parse(palette.BorderMetal);
        _textPrimary = Color.Parse(palette.TextPrimary);
        _textSecondary = Color.Parse(palette.TextSecondary);
        _textMuted = Color.Parse(palette.TextMuted);
        _textKhaki = Color.Parse(palette.TextKhaki);
        _success = Color.Parse(palette.Success);
        _warning = Color.Parse(palette.Warning);
        _danger = Color.Parse(palette.Danger);

        var accentColors = CurrentThemeId == "day"
            ? new Dictionary<string, string>
            {
                ["amber"] = "#8A610C", ["teal"] = "#176B5E", ["blue"] = "#285F8D", ["rust"] = "#9B452B",
            }
            : new Dictionary<string, string>
            {
                ["amber"] = "#D6A62D", ["teal"] = "#70BCA6", ["blue"] = "#78AFE0", ["rust"] = "#E07A57",
            };
        _accent = Color.Parse(accentColors[CurrentAccentId]);
        _accentForeground = RelativeLuminance(_accent) > 0.42 ? Color.Parse("#10130F") : Colors.White;
        _accentDim = Blend(_accent, _bgBase, 0.36);
        _accentHover = Blend(_accent, Colors.White, CurrentThemeId == "day" ? 0.12 : 0.08);

        SetBrush(BrushBgBase, _bgBase);
        SetBrush(BrushBgPanel, _bgPanel);
        SetBrush(BrushBgElevated, _bgElevated);
        SetBrush(BrushBgHover, _bgHover);
        SetBrush(BrushBgInput, _bgInput);
        SetBrush(BrushPlateBg, _plateBg);
        SetBrush(BrushBorderSubtle, _borderSubtle);
        SetBrush(BrushBorder, _border);
        SetBrush(BrushBorderMetal, _borderMetal);
        SetBrush(BrushBorderFocus, _accent);
        SetBrush(BrushAccentAmber, _accent);
        SetBrush(BrushAccentDim, _accentDim);
        SetBrush(BrushAccentHover, _accentHover);
        SetBrush(BrushAccentForeground, _accentForeground);
        SetBrush(BrushSuccess, _success);
        SetBrush(BrushWarning, _warning);
        SetBrush(BrushDanger, _danger);
        SetBrush(BrushTextPrimary, _textPrimary);
        SetBrush(BrushTextSecondary, _textSecondary);
        SetBrush(BrushTextMuted, _textMuted);
        SetBrush(BrushTextKhaki, _textKhaki);
        RefreshApplicationResources();
    }

    private static void SetBrush(IBrush brush, Color color) => ((SolidColorBrush)brush).Color = color;

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var linear = value / 255.0;
            return linear <= 0.04045 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static Color Blend(Color foreground, Color background, double backgroundWeight) => Color.FromArgb(
        255,
        (byte)Math.Round(foreground.R * (1 - backgroundWeight) + background.R * backgroundWeight),
        (byte)Math.Round(foreground.G * (1 - backgroundWeight) + background.G * backgroundWeight),
        (byte)Math.Round(foreground.B * (1 - backgroundWeight) + background.B * backgroundWeight));

    private static void RefreshApplicationResources()
    {
        if (Application.Current is not { } app) return;
        app.Resources["SystemControlBackgroundAltHighBrush"] = BrushBgBase;
        app.Resources["SystemControlBackgroundAccentBrush"] = BrushAccentAmber;
        app.Resources["SystemControlForegroundAccentBrush"] = BrushAccentForeground;
        app.Resources["SystemControlForegroundListLowBrush"] = BrushTextPrimary;
        app.Resources["SystemControlForegroundListMediumBrush"] = BrushTextPrimary;
        app.Resources["SystemControlHighlightAccentBrush"] = BrushAccentAmber;
        app.Resources["SystemControlHighlightAltAccentBrush"] = BrushAccentAmber;
        app.Resources["SystemControlHighlightListAccentLowBrush"] = BrushAccentDim;
        app.Resources["SystemControlHighlightListAccentMediumBrush"] = BrushAccentAmber;
        app.Resources["SystemControlHighlightListAccentHighBrush"] = BrushAccentAmber;
        app.Resources["SystemControlHighlightAltListAccentLowBrush"] = BrushAccentDim;
        app.Resources["SystemControlHighlightAltListAccentMediumBrush"] = BrushAccentAmber;
        app.Resources["SystemControlHighlightAltListAccentHighBrush"] = BrushAccentAmber;
        app.Resources["ScrollBarThumbFill"] = BrushBorderMetal;
        app.Resources["ScrollBarThumbFillPointerOver"] = BrushAccentAmber;
        app.Resources["ScrollBarTrackFill"] = BrushBgPanel;
        app.Resources["TextControlBackground"] = BrushBgInput;
        app.Resources["TextControlForeground"] = BrushTextPrimary;
        app.Resources["TextControlBorderBrush"] = BrushBorder;
        app.Resources["TextControlBorderBrushFocused"] = BrushAccentAmber;
        app.Resources["ProgressBarForeground"] = BrushAccentAmber;
        app.Resources["ProgressBarBackground"] = BrushBgElevated;
        app.Resources["ButtonBackgroundDisabled"] = BrushBgPanel;
        app.Resources["ButtonForegroundDisabled"] = BrushTextMuted;
        app.Resources["ButtonBorderBrushDisabled"] = BrushBorderSubtle;
    }

    private sealed record Palette(
        string BgBase, string BgPanel, string BgElevated, string BgHover, string BgInput, string PlateBg,
        string BorderSubtle, string Border, string BorderMetal, string TextPrimary, string TextSecondary,
        string TextMuted, string TextKhaki, string Success, string Warning, string Danger);

    /// <summary>Body text: Liberation Sans Narrow (SIL OFL-compatible licence, shipped in Assets/Fonts).</summary>
    public static readonly FontFamily BodyFont = new("avares://StalkerSaveEditor.Desktop/Assets/Fonts#Liberation Sans Narrow");

    /// <summary>Headings: Oswald (SIL OFL), the condensed face of the game menus' look.</summary>
    public static readonly FontFamily HeadingFont = new("avares://StalkerSaveEditor.Desktop/Assets/Fonts#Oswald");

    /// <summary>
    /// Item template that tolerates null: Avalonia rebuilds a recycled row with null content when an
    /// item is removed, and a template that dereferences it throws out of the collection change.
    /// </summary>
    public static Avalonia.Controls.Templates.FuncDataTemplate<T> Template<T>(Func<T, Control> build)
        where T : class =>
        new((item, _) => item is null ? new Panel() : build(item), supportsRecycling: false);

    /// <summary>
    /// A row built once and filled only through bindings to its DataContext, so Avalonia may recycle it (issue #90:
    /// templates that capture the item must not recycle, or a reused row shows another item's data).
    /// </summary>
    public static Avalonia.Controls.Templates.FuncDataTemplate<T> RecyclingTemplate<T>(Func<Control> build)
        where T : class =>
        new((_, _) => build(), supportsRecycling: true);

    public static Border Card(Control content, string? title = null, Thickness? margin = null, Thickness? padding = null)
    {
        var container = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(title))
        {
            var headerPlate = new Border
            {
                Background = BrushPlateBg,
                BorderBrush = BrushBorderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 6),
                Margin = new Thickness(-14, -14, -14, 6),
                Child = new TextBlock
                {
                    Text = $"[ {title.ToUpperInvariant()} ]",
                    FontFamily = HeadingFont,
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Foreground = BrushAccentAmber,
                    LetterSpacing = 1.1,
                },
            };
            container.Children.Add(headerPlate);
        }
        container.Children.Add(content);

        return new Border
        {
            Background = BrushBgPanel,
            BorderBrush = BrushBorderMetal,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Padding = padding ?? new Thickness(14),
            Margin = margin ?? new Thickness(0),
            Child = container,
        };
    }

    /// <summary>"#RRGGBB" from a view model → a brush (colours that follow a value, e.g. an item's condition).</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter ColorToBrush =
        new Avalonia.Data.Converters.FuncValueConverter<string?, IBrush?>(color => color is null ? null : new SolidColorBrush(Color.Parse(color)));

    public static Border Badge(string text, IBrush background, IBrush foreground, double fontSize = 11)
    {
        return new Border
        {
            Background = background,
            BorderBrush = BrushBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = foreground,
            },
        };
    }

    public static Button StalkerButton(string text, bool isPrimary = false, double minWidth = 100)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = minWidth,
            Background = isPrimary ? BrushAccentAmber : BrushBgElevated,
            Foreground = isPrimary ? BrushAccentForeground : BrushTextKhaki,
            BorderBrush = isPrimary ? BrushBorderFocus : BrushBorderMetal,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Padding = new Thickness(14, 7),
            FontWeight = isPrimary ? FontWeight.Bold : FontWeight.SemiBold,
            FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        if (!isPrimary)
        {
            btn.PointerEntered += (_, _) =>
            {
                if (btn.IsEnabled)
                {
                    btn.Background = BrushBgHover;
                    btn.BorderBrush = BrushBorderFocus;
                    btn.Foreground = BrushTextPrimary;
                }
            };
            btn.PointerExited += (_, _) =>
            {
                if (btn.IsEnabled)
                {
                    btn.Background = BrushBgElevated;
                    btn.BorderBrush = BrushBorderMetal;
                    btn.Foreground = BrushTextKhaki;
                }
            };
        }
        else
        {
            btn.PointerEntered += (_, _) =>
            {
                if (btn.IsEnabled)
                {
                    btn.Background = BrushAccentHover;
                    btn.BorderBrush = BrushAccentForeground;
                }
            };
            btn.PointerExited += (_, _) =>
            {
                if (btn.IsEnabled)
                {
                    btn.Background = BrushAccentAmber;
                    btn.BorderBrush = BrushBorderFocus;
                }
            };
        }

        return btn;
    }
}
