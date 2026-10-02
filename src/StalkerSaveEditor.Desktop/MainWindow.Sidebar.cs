using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop;

public sealed partial class MainWindow : Window
{
    private static SidebarNavigation BuildSidebar(SaveLibraryViewModel vm, ColumnDefinition column, Grid host) =>
        new(vm, column, host);

    private sealed class SidebarNavigation : Border
    {
        private readonly ColumnDefinition _column;
        private readonly List<Control> _expandedOnly = [];
        private readonly SaveLibraryViewModel _vm;
        private readonly Button _toggleButton;
        private readonly TextBlock _toggleGlyph;
        /// <summary>Below this the workspace has no room for a labelled menu; from <see cref="RoomyWidth"/> it opens by itself.</summary>
        private const double NarrowWidth = 900;
        private const double RoomyWidth = 1150;
        private double _availableWidth;
        private bool _expanded = true;

        public SidebarNavigation(SaveLibraryViewModel vm, ColumnDefinition column, Grid host)
        {
            _vm = vm;
            _column = column;
            Background = StalkerTheme.BrushBgPanel;
            BorderBrush = StalkerTheme.BrushBorderSubtle;
            BorderThickness = new Thickness(0, 0, 1, 0);

            var layout = new DockPanel();
            var brand = BuildBrand();
            DockPanel.SetDock(brand, Dock.Top);
            layout.Children.Add(brand);

            var toggle = new Button
            {
                Height = 34,
                Margin = new Thickness(6, 0, 6, 4),
                Padding = new Thickness(4, 3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = StalkerTheme.BrushBgElevated,
                Foreground = StalkerTheme.BrushTextSecondary,
                BorderBrush = StalkerTheme.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
            };
            _toggleButton = toggle;
            _toggleGlyph = new TextBlock
            {
                Text = "☰",
                FontSize = 17,
                Foreground = StalkerTheme.BrushAccentAmber,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var toggleLabel = new TextBlock
            {
                Text = L.T("Свернуть меню"),
                FontFamily = StalkerTheme.HeadingFont,
                FontSize = 12,
                Foreground = StalkerTheme.BrushTextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(toggleLabel, 1);
            _expandedOnly.Add(toggleLabel);
            toggle.Content = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), Children = { _toggleGlyph, toggleLabel } };
            toggle.Click += (_, _) =>
            {
                _vm.Settings.NavigationCollapsed = _expanded;
                Apply();
            };
            AutomationProperties.SetName(toggle, L.T("Свернуть меню"));
            DockPanel.SetDock(toggle, Dock.Top);
            layout.Children.Add(toggle);

            var navigation = new StackPanel { Spacing = 1, Margin = new Thickness(5, 2, 5, 8) };
            AddGroup(navigation, L.T("СОХРАНЕНИЯ"),
            [
                ("◉", L.T("ОБЗОР"), AppTabs.Overview),
                ("▤", L.T("ИНВЕНТАРЬ"), AppTabs.Inventory),
                ("⚑", L.T("ФРАКЦИИ"), AppTabs.Factions),
                ("◇", L.T("ТАЙНИКИ"), AppTabs.Stashes),
                ("⇄", L.T("ПЕРЕХОДЫ"), AppTabs.Transitions),
                ("▣", L.T("БЭКАПЫ"), AppTabs.Backups),
                ("⇆", L.T("СРАВНЕНИЕ"), AppTabs.Compare),
                ("◷", L.T("ИСТОРИЯ СОХРАНЕНИЙ"), AppTabs.Timeline),
            ]);
            if (!HostPlatform.IsBrowser)
            {
                AddItem(navigation, "✚", L.T("ДОКТОР СОХРАНЕНИЯ"), AppTabs.SaveDoctor);
                AddGroup(navigation, L.T("ИГРЫ"),
                [
                    ("◉", L.T("ОБЗОР ИГР"), AppTabs.Games),
                    ("⚒", L.T("ИСПРАВЛЕНИЯ ИГРЫ"), AppTabs.GameFixes),
                    ("⌖", L.T("ДОКТОР ИГРЫ"), AppTabs.GameDoctor),
                    ("⚙", L.T("СРЕДА ИГРЫ"), AppTabs.ToolkitEnvironment),
                    ("●", L.T("КОМПАНЬОН"), AppTabs.Companion),
                    ("★", L.T("ДОСТИЖЕНИЯ"), AppTabs.Achievements),
                ]);
                AddGroup(navigation, L.T("ИНСТРУМЕНТЫ"),
                [
                    ("☁", L.T("ОБЛАКО"), AppTabs.Cloud),
                    ("≡", L.T("ЭНЦИКЛОПЕДИЯ"), AppTabs.Encyclopedia),
                    ("✓", L.T("ВОЗМОЖНОСТИ"), AppTabs.Capabilities),
                    ("↻", L.T("ОБНОВЛЕНИЯ"), AppTabs.Updates),
                    ("⚙", L.T("НАСТРОЙКИ"), AppTabs.Settings),
                ]);
            }
            else
            {
                AddGroup(navigation, L.T("ИНСТРУМЕНТЫ"),
                [
                    ("✓", L.T("ВОЗМОЖНОСТИ"), AppTabs.Capabilities),
                    ("⚙", L.T("НАСТРОЙКИ"), AppTabs.Settings),
                ]);
            }

            layout.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = navigation,
            });
            Child = layout;
            host.SizeChanged += (_, args) => AdaptToWidth(args.NewSize.Width);
            AdaptToWidth(host.Bounds.Width);
        }

        public void AdaptToWidth(double width)
        {
            _availableWidth = width;
            Apply();
        }

        /// <summary>The user's choice wins while the window has room for it; a narrow window always folds the menu.</summary>
        private void Apply()
        {
            var expanded = _availableWidth >= NarrowWidth && !(_vm.Settings.NavigationCollapsed ?? _availableWidth < RoomyWidth);
            _expanded = expanded;
            _column.Width = new GridLength(expanded ? 236 : 58);
            foreach (var control in _expandedOnly) control.IsVisible = expanded;
            var tip = L.T(expanded ? "Свернуть меню" : "Развернуть меню");
            ToolTip.SetTip(_toggleButton, tip);
            AutomationProperties.SetName(_toggleButton, tip);
        }

        private Control BuildBrand()
        {
            var brand = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("42,*"),
                Margin = new Thickness(10, 12, 8, 10),
            };
            var mark = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                BorderBrush = StalkerTheme.BrushAccentAmber,
                BorderThickness = new Thickness(1),
                Background = StalkerTheme.BrushBgElevated,
                Child = new TextBlock
                {
                    Text = "☢",
                    FontSize = 21,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            brand.Children.Add(mark);
            var name = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock
            {
                Text = "S.T.A.L.K.E.R.",
                FontFamily = StalkerTheme.HeadingFont,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = StalkerTheme.BrushTextPrimary,
                LetterSpacing = 0.7,
            });
            name.Children.Add(new TextBlock
            {
                Text = L.T("РЕДАКТОР СОХРАНЕНИЙ"),
                FontSize = 9,
                Foreground = StalkerTheme.BrushAccentAmber,
                LetterSpacing = 0.5,
            });
            Grid.SetColumn(name, 1);
            brand.Children.Add(name);
            _expandedOnly.Add(name);
            return brand;
        }

        private void AddGroup(StackPanel parent, string heading, (string Icon, string Label, string Tab)[] items)
        {
            var header = new TextBlock
            {
                Text = heading,
                FontFamily = StalkerTheme.HeadingFont,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = StalkerTheme.BrushTextMuted,
                LetterSpacing = 0.8,
                Margin = new Thickness(8, 8, 4, 3),
            };
            _expandedOnly.Add(header);
            parent.Children.Add(header);
            foreach (var item in items) AddItem(parent, item.Icon, item.Label, item.Tab);
        }

        private void AddItem(StackPanel parent, string icon, string label, string tab)
        {
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*") };
            var iconText = new TextBlock
            {
                Name = "nav-icon-" + tab,
                Text = icon,
                FontSize = 17,
                Foreground = StalkerTheme.BrushAccentAmber,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(iconText);
            var labelText = new TextBlock
            {
                Name = "nav-label-" + tab,
                Text = label,
                FontFamily = StalkerTheme.HeadingFont,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = StalkerTheme.BrushTextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(labelText, 1);
            content.Children.Add(labelText);
            _expandedOnly.Add(labelText);

            var button = new Button
            {
                Name = "nav-" + tab,
                Content = content,
                Height = 33,
                Margin = new Thickness(1, 1),
                Padding = new Thickness(4, 3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = StalkerTheme.BrushBgPanel,
                Foreground = StalkerTheme.BrushTextSecondary,
                BorderBrush = StalkerTheme.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
            };
            button.Bind(Button.BackgroundProperty, new Binding(nameof(SaveLibraryViewModel.SelectedTab))
            {
                Source = _vm,
                Converter = new ActiveNavigationBrushConverter(active: StalkerTheme.BrushBgHover, inactive: StalkerTheme.BrushBgPanel),
                ConverterParameter = tab,
            });
            button.Bind(Button.BorderBrushProperty, new Binding(nameof(SaveLibraryViewModel.SelectedTab))
            {
                Source = _vm,
                Converter = new ActiveNavigationBrushConverter(active: StalkerTheme.BrushBorderFocus, inactive: StalkerTheme.BrushBorderSubtle),
                ConverterParameter = tab,
            });
            button.Click += (_, _) => _vm.SelectedTab = tab;
            ToolTip.SetTip(button, label);
            ToolTip.SetTip(content, label);
            ToolTip.SetTip(iconText, label);
            AutomationProperties.SetName(button, label);
            parent.Children.Add(button);
        }

    }
}
