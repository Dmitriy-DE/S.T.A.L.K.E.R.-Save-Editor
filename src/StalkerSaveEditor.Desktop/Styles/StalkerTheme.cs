using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StalkerSaveEditor.Desktop.Styles;

public static class StalkerTheme
{
    // Industrial S.T.A.L.K.E.R. color palette
    public static readonly Color BgBase = Color.Parse("#0C0D0A");
    public static readonly Color BgPanel = Color.Parse("#101311");
    public static readonly Color BgElevated = Color.Parse("#151814");
    public static readonly Color BgHover = Color.Parse("#23261F");
    public static readonly Color BgInput = Color.Parse("#1A1D17");
    public static readonly Color PlateBg = Color.Parse("#161A14");

    public static readonly Color BorderSubtle = Color.Parse("#242922");
    public static readonly Color Border = Color.Parse("#33382F");
    public static readonly Color BorderMetal = Color.Parse("#3D4837");
    public static readonly Color BorderFocus = Color.Parse("#D6A62D");

    public static readonly Color AccentAmber = Color.Parse("#D6A62D");
    public static readonly Color AccentDim = Color.Parse("#8F6F22");
    public static readonly Color Rust = Color.Parse("#A9532F");

    public static readonly Color Success = Color.Parse("#7BCB62");
    public static readonly Color Warning = Color.Parse("#D6A62D");
    public static readonly Color Danger = Color.Parse("#D85A45");

    public static readonly Color TextPrimary = Color.Parse("#D8D2BE");
    public static readonly Color TextSecondary = Color.Parse("#A29D90");
    public static readonly Color TextMuted = Color.Parse("#716F67");
    public static readonly Color TextKhaki = Color.Parse("#D8BA8C");

    // Brushes
    public static readonly IBrush BrushBgBase = new SolidColorBrush(BgBase);
    public static readonly IBrush BrushBgPanel = new SolidColorBrush(BgPanel);
    public static readonly IBrush BrushBgElevated = new SolidColorBrush(BgElevated);
    public static readonly IBrush BrushBgHover = new SolidColorBrush(BgHover);
    public static readonly IBrush BrushBgInput = new SolidColorBrush(BgInput);
    public static readonly IBrush BrushPlateBg = new SolidColorBrush(PlateBg);

    public static readonly IBrush BrushBorderSubtle = new SolidColorBrush(BorderSubtle);
    public static readonly IBrush BrushBorder = new SolidColorBrush(Border);
    public static readonly IBrush BrushBorderMetal = new SolidColorBrush(BorderMetal);
    public static readonly IBrush BrushBorderFocus = new SolidColorBrush(BorderFocus);

    public static readonly IBrush BrushAccentAmber = new SolidColorBrush(AccentAmber);
    public static readonly IBrush BrushAccentDim = new SolidColorBrush(AccentDim);

    public static readonly IBrush BrushSuccess = new SolidColorBrush(Success);
    public static readonly IBrush BrushWarning = new SolidColorBrush(Warning);
    public static readonly IBrush BrushDanger = new SolidColorBrush(Danger);

    public static readonly IBrush BrushTextPrimary = new SolidColorBrush(TextPrimary);
    public static readonly IBrush BrushTextSecondary = new SolidColorBrush(TextSecondary);
    public static readonly IBrush BrushTextMuted = new SolidColorBrush(TextMuted);
    public static readonly IBrush BrushTextKhaki = new SolidColorBrush(TextKhaki);

    public static Border Card(Control content, string? title = null, Thickness? margin = null, Thickness? padding = null)
    {
        var container = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(title))
        {
            var headerPlate = new Border
            {
                Background = BrushPlateBg,
                BorderBrush = new SolidColorBrush(Color.Parse("#283023")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 6),
                Margin = new Thickness(-14, -14, -14, 6),
                Child = new TextBlock
                {
                    Text = $"[ ▪ {title.ToUpperInvariant()} ]",
                    FontSize = 11,
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

    public static Border Badge(string text, IBrush background, IBrush foreground, double fontSize = 11)
    {
        return new Border
        {
            Background = background,
            BorderBrush = new SolidColorBrush(Color.Parse("#384232")),
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
            Background = isPrimary ? BrushAccentAmber : new SolidColorBrush(Color.Parse("#181D15")),
            Foreground = isPrimary ? new SolidColorBrush(Color.Parse("#0C0D0A")) : BrushTextKhaki,
            BorderBrush = isPrimary ? new SolidColorBrush(Color.Parse("#F0C040")) : BrushBorderMetal,
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
                    btn.Background = new SolidColorBrush(Color.Parse("#262E22"));
                    btn.BorderBrush = BrushAccentAmber;
                    btn.Foreground = Brushes.White;
                }
            };
            btn.PointerExited += (_, _) =>
            {
                if (btn.IsEnabled)
                {
                    btn.Background = new SolidColorBrush(Color.Parse("#181D15"));
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
                    btn.Background = new SolidColorBrush(Color.Parse("#E5B53C"));
                    btn.BorderBrush = Brushes.White;
                }
            };
            btn.PointerExited += (_, _) =>
            {
                if (btn.IsEnabled)
                {
                    btn.Background = BrushAccentAmber;
                    btn.BorderBrush = new SolidColorBrush(Color.Parse("#F0C040"));
                }
            };
        }

        return btn;
    }
}
