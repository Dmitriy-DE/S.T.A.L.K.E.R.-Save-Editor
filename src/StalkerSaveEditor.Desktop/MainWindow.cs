using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;

namespace StalkerSaveEditor.Desktop;

public sealed class MainWindow : Window
{
    private readonly SaveLibraryViewModel _viewModel;

    public MainWindow(SaveLibraryViewModel? viewModel = null)
    {
        _viewModel = viewModel ?? new SaveLibraryViewModel();
        DataContext = _viewModel;

        Title = "S.T.A.L.K.E.R. Save Editor";
        FontFamily = StalkerTheme.BodyFont;
        Width = ScreenshotDimension("STALKER_EDITOR_SCREENSHOT_WIDTH", 1260);
        Height = ScreenshotDimension("STALKER_EDITOR_SCREENSHOT_HEIGHT", 820);
        MinWidth = 940;
        MinHeight = 600;
        Background = StalkerTheme.BrushBgBase;

        Content = BuildRoot(_viewModel);
    }

    /// <summary>Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) undo and redo draft edits, Ctrl+S writes the save.</summary>
    private static void AddShortcuts(Control root, SaveLibraryViewModel vm)
    {
        foreach (var (gesture, command) in new (string, System.Windows.Input.ICommand)[]
                 {
                     ("Ctrl+Z", vm.UndoCommand),
                     ("Ctrl+Y", vm.RedoCommand),
                     ("Ctrl+Shift+Z", vm.RedoCommand),
                     ("Ctrl+S", vm.SaveCommand),
                 })
        {
            root.KeyBindings.Add(new Avalonia.Input.KeyBinding { Gesture = Avalonia.Input.KeyGesture.Parse(gesture), Command = command });
        }
    }

    /// <summary>The whole interface as one control (the web host shows it without a window).</summary>
    public static Control BuildRoot(SaveLibraryViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ApplyScreenshotOverrides(vm);
        var root = BuildContent(vm);
        root.DataContext = vm;
        var scaledRoot = new LayoutTransformControl
        {
            Child = root,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        scaledRoot.Bind(LayoutTransformControl.LayoutTransformProperty,
            new Binding(nameof(SettingsViewModel.UiScaleTransform)) { Source = vm.Settings });
        scaledRoot.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, StalkerTheme.BodyFont);
        AddShortcuts(scaledRoot, vm);
        return scaledRoot;
    }

    private static void ApplyScreenshotOverrides(SaveLibraryViewModel vm)
    {
        if (SaveLibraryViewModel.InteractiveApp) return;
        if (Environment.GetEnvironmentVariable("STALKER_EDITOR_SCREENSHOT_THEME") is { Length: > 0 } theme)
            vm.Settings.ThemeId = theme;
        if (Environment.GetEnvironmentVariable("STALKER_EDITOR_SCREENSHOT_ACCENT") is { Length: > 0 } accent)
            vm.Settings.AccentId = accent;
        if (int.TryParse(Environment.GetEnvironmentVariable("STALKER_EDITOR_SCREENSHOT_UI_SCALE"), out var scale))
            vm.Settings.UiScalePercent = scale;
    }

    private static double ScreenshotDimension(string variable, double fallback)
    {
        if (SaveLibraryViewModel.InteractiveApp) return fallback;
        return double.TryParse(Environment.GetEnvironmentVariable(variable), System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out var value)
               && value is >= 640 and <= 4096
            ? value
            : fallback;
    }

    private static Control BuildContent(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Background = StalkerTheme.BrushBgBase,
        };

        // 1. Top Header Bar
        var topBar = BuildTopBar(vm);
        root.Children.Add(topBar);

        // 2. Update Notification Banner
        var updateBanner = new StackPanel();
        if (!HostPlatform.IsBrowser) updateBanner.Children.Add(BuildReportsNotice(vm));
        updateBanner.Children.Add(BuildCrashBanner(vm));
        updateBanner.Children.Add(BuildUpdateBanner(vm));
        Grid.SetRow(updateBanner, 1);
        root.Children.Add(updateBanner);

        // 3. Middle area: collapsible navigation, contextual save list and active screen.
        var navigationColumn = new ColumnDefinition(new GridLength(236));
        var savesColumn = new ColumnDefinition(new GridLength(280));
        var workspaceColumn = new ColumnDefinition(new GridLength(1, GridUnitType.Star));
        var middle = new Grid
        {
        };
        middle.ColumnDefinitions.Add(navigationColumn);
        middle.ColumnDefinitions.Add(savesColumn);
        middle.ColumnDefinitions.Add(workspaceColumn);

        var sidebar = BuildSidebar(vm, navigationColumn, middle);
        middle.Children.Add(sidebar);

        var savesPane = BuildSavesPane(vm);
        Grid.SetColumn(savesPane, 1);
        middle.Children.Add(savesPane);

        var workspacePane = BuildWorkspacePane(vm);
        Grid.SetColumn(workspacePane, 2);
        middle.Children.Add(workspacePane);

        void UpdateSavesColumn()
        {
            var width = middle.Bounds.Width;
            var savePaneWidth = width < 1100 ? 220 : width >= 1900 ? 300 : 280;
            savesColumn.Width = vm.IsSaveWorkspace ? new GridLength(savePaneWidth) : new GridLength(0);
            savesPane.IsVisible = vm.IsSaveWorkspace;
        }

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SaveLibraryViewModel.SelectedTab) or nameof(SaveLibraryViewModel.IsSaveWorkspace))
                UpdateSavesColumn();
        };
        middle.SizeChanged += (_, args) =>
        {
            if (sidebar is SidebarNavigation nav) nav.AdaptToWidth(args.NewSize.Width);
            UpdateSavesColumn();
        };
        UpdateSavesColumn();

        Grid.SetRow(middle, 2);
        root.Children.Add(middle);

        // 4. Bottom Status Bar
        var statusBar = BuildStatusBar();
        Grid.SetRow(statusBar, 3);
        root.Children.Add(statusBar);

        return root;
    }

    /// <summary>First-run notice: what the daily report contains and how to switch it off.</summary>
    private static Control BuildReportsNotice(SaveLibraryViewModel vm)
    {
        var banner = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushAccentAmber,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 8, 18, 8),
        };
        banner.Bind(Visual.IsVisibleProperty, new Binding(nameof(SettingsViewModel.ReportsNoticeVisible)) { Source = vm.Settings });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = L.T("Редактор раз в сутки и после сбоя отправляет разработчику журнал работы, чтобы находить ошибки. Пути, имена и Steam ID из него вырезаются, сейвы не отправляются. Отключить можно здесь или в Настройках → Диагностика."),
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        var ok = StalkerTheme.StalkerButton(L.T("Понятно"), isPrimary: true, minWidth: 100);
        ok.Command = vm.AcknowledgeReportsCommand;
        Grid.SetColumn(ok, 1);
        grid.Children.Add(ok);
        var off = StalkerTheme.StalkerButton(L.T("Не отправлять"), isPrimary: false, minWidth: 120);
        off.Command = vm.DisableReportsCommand;
        off.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(off, 2);
        grid.Children.Add(off);
        banner.Child = grid;
        return banner;
    }

    private static Control BuildCrashBanner(SaveLibraryViewModel vm)
    {
        var banner = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushDanger,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 6, 18, 6),
            DataContext = vm.Diagnostics,
        };
        banner.Bind(Visual.IsVisibleProperty, new Binding(nameof(DiagnosticsViewModel.HasPendingCrash)));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = L.T("Прошлый запуск завершился ошибкой — отчёт в Настройках → Диагностика."),
            Foreground = StalkerTheme.BrushDanger,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var open = StalkerTheme.StalkerButton(L.T("Открыть"), isPrimary: true, minWidth: 100);
        open.Click += (_, _) => vm.SelectedTab = "settings";
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);
        banner.Child = grid;
        return banner;
    }

    private static Control BuildUpdateBanner(SaveLibraryViewModel vm)
    {
        var banner = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushSuccess,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 6, 18, 6),
            DataContext = vm.Updates,
        };
        banner.Bind(Visual.IsVisibleProperty, new Binding(nameof(UpdatesViewModel.ShowNotificationBanner)));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
        };

        var text = new TextBlock
        {
            Foreground = StalkerTheme.BrushSuccess,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(UpdatesViewModel.NotificationBannerText)));
        grid.Children.Add(text);

        var openBtn = StalkerTheme.StalkerButton(L.T("Посмотреть"), isPrimary: true, minWidth: 100);
        openBtn.Click += (_, _) => vm.SelectedTab = "updates";
        Grid.SetColumn(openBtn, 1);
        grid.Children.Add(openBtn);

        var dismissBtn = StalkerTheme.StalkerButton("×", isPrimary: false, minWidth: 32);
        dismissBtn.Bind(Button.CommandProperty, new Binding(nameof(UpdatesViewModel.DismissBannerCommand)));
        Grid.SetColumn(dismissBtn, 2);
        dismissBtn.Margin = new Thickness(8, 0, 0, 0);
        grid.Children.Add(dismissBtn);

        banner.Child = grid;
        return banner;
    }

    private static SidebarNavigation BuildSidebar(SaveLibraryViewModel vm, ColumnDefinition column, Grid host) =>
        new(vm, column, host);

    private sealed class SidebarNavigation : Border
    {
        private readonly ColumnDefinition _column;
        private readonly List<Control> _expandedOnly = [];
        private readonly SaveLibraryViewModel _vm;
        private readonly Button _toggleButton;
        private readonly TextBlock _toggleGlyph;
        private double _availableWidth;
        private bool _expanded = true;
        private bool _manualChoice;

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
                Height = 38,
                Margin = new Thickness(8, 6),
                Padding = new Thickness(8, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = StalkerTheme.BrushBgElevated,
                Foreground = StalkerTheme.BrushTextSecondary,
                BorderBrush = StalkerTheme.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
            };
            _toggleButton = toggle;
            _toggleGlyph = new TextBlock
            {
                Text = "‹",
                FontSize = 20,
                Foreground = StalkerTheme.BrushAccentAmber,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            toggle.Content = _toggleGlyph;
            toggle.Click += (_, _) =>
            {
                _manualChoice = true;
                SetExpanded(!_expanded && _availableWidth >= 1100);
                if (_availableWidth < 1100) SetExpanded(false);
            };
            ToolTip.SetTip(toggle, L.T("Свернуть меню"));
            DockPanel.SetDock(toggle, Dock.Bottom);
            layout.Children.Add(toggle);

            var navigation = new StackPanel { Spacing = 2, Margin = new Thickness(5, 8) };
            AddGroup(navigation, L.T("СОХРАНЕНИЯ"),
            [
                ("◉", L.T("ОБЗОР"), "overview"),
                ("▤", L.T("ИНВЕНТАРЬ"), "inventory"),
                ("⚑", L.T("ФРАКЦИИ"), "factions"),
                ("◇", L.T("ТАЙНИКИ"), "stashes"),
                ("⇄", L.T("ПЕРЕХОДЫ"), "transitions"),
                ("▣", L.T("БЭКАПЫ"), "backups"),
                ("◷", L.T("ИСТОРИЯ СОХРАНЕНИЙ"), "timeline"),
            ]);
            if (!HostPlatform.IsBrowser)
            {
                AddItem(navigation, "✚", L.T("ДОКТОР СОХРАНЕНИЯ"), "save-doctor");
                AddGroup(navigation, L.T("ИГРЫ"),
                [
                    ("⚒", L.T("ИСПРАВЛЕНИЯ ИГРЫ"), "game-fixes"),
                    ("⌖", L.T("ДОКТОР ИГРЫ"), "game-doctor"),
                    ("⚙", L.T("СРЕДА ИГРЫ"), "toolkit-environment"),
                    ("●", L.T("КОМПАНЬОН"), "companion"),
                    ("★", L.T("ДОСТИЖЕНИЯ"), "achievements"),
                ]);
                AddGroup(navigation, L.T("ИНСТРУМЕНТЫ"),
                [
                    ("☁", L.T("ОБЛАКО"), "cloud"),
                    ("≡", L.T("ЭНЦИКЛОПЕДИЯ"), "encyclopedia"),
                    ("✓", L.T("ВОЗМОЖНОСТИ"), "capabilities"),
                    ("↻", L.T("ОБНОВЛЕНИЯ"), "updates"),
                    ("⚙", L.T("НАСТРОЙКИ"), "settings"),
                ]);
            }
            else
            {
                AddGroup(navigation, L.T("ИНСТРУМЕНТЫ"),
                [
                    ("✓", L.T("ВОЗМОЖНОСТИ"), "capabilities"),
                    ("⚙", L.T("НАСТРОЙКИ"), "settings"),
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
            if (!_manualChoice) SetExpanded(width >= 1600);
            else if (width < 1100) SetExpanded(false);
        }

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            _column.Width = new GridLength(expanded ? 236 : 64);
            foreach (var control in _expandedOnly) control.IsVisible = expanded;
            _toggleGlyph.Text = expanded ? "‹" : "›";
            ToolTip.SetTip(_toggleButton, L.T(expanded ? "Свернуть меню" : "Развернуть меню"));
        }

        private Control BuildBrand()
        {
            var brand = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("42,*"),
                Margin = new Thickness(10, 14, 8, 12),
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
                Margin = new Thickness(8, 10, 4, 4),
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
                Text = icon,
                FontSize = 17,
                Foreground = StalkerTheme.BrushAccentAmber,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(iconText);
            var labelText = new TextBlock
            {
                Text = label,
                FontFamily = StalkerTheme.HeadingFont,
                FontSize = 11,
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
                Content = content,
                Height = 36,
                Margin = new Thickness(1, 1),
                Padding = new Thickness(4, 3),
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
            parent.Children.Add(button);
        }

    }

    private static Control BuildTopBar(SaveLibraryViewModel vm)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(18, 12, 18, 12),
        };

        // App title & badge
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = "S.T.A.L.K.E.R. SAVE EDITOR",
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var releaseBadge = StalkerTheme.Badge("X-Ray / S2", StalkerTheme.BrushBgElevated, StalkerTheme.BrushTextSecondary, 10);
        ((TextBlock)releaseBadge.Child!).Bind(TextBlock.TextProperty, new Binding("SelectedSave.ReleaseName") { FallbackValue = "X-Ray / S2", TargetNullValue = "X-Ray / S2" });
        titleStack.Children.Add(releaseBadge);
        grid.Children.Add(titleStack);

        // Right side: Undo, Redo, Discard, Save buttons
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var undoBtn = StalkerTheme.StalkerButton(L.T("Отменить"), isPrimary: false, minWidth: 90);
        undoBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.UndoCommand)));
        actions.Children.Add(undoBtn);

        var redoBtn = StalkerTheme.StalkerButton(L.T("Вернуть"), isPrimary: false, minWidth: 90);
        redoBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RedoCommand)));
        actions.Children.Add(redoBtn);

        var discardBtn = StalkerTheme.StalkerButton(L.T("Сбросить"), isPrimary: false, minWidth: 90);
        discardBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.DiscardDraftCommand)));
        actions.Children.Add(discardBtn);

        var openBtn = StalkerTheme.StalkerButton(L.T("Открыть…"), isPrimary: false, minWidth: 90);
        openBtn.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(openBtn)?.StorageProvider is not { } storage) return;
            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = L.T("Открыть сохранение"),
                AllowMultiple = true,
            });
            foreach (var file in files)
            {
                try
                {
                    var path = file.TryGetLocalPath();
                    if (path is null || HostPlatform.IsBrowser)
                    {
                        await using var stream = await file.OpenReadAsync();
                        path = await HostPlatform.ImportAsync(stream, file.Name);
                    }

                    if (!await vm.AddPreviewSaveAsync(path)) vm.StatusMessage = L.T("«{0}» — не сохранение S.T.A.L.K.E.R. или файл повреждён.", file.Name);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    vm.StatusMessage = L.T("Не удалось открыть «{0}»: {1}", file.Name, exception.Message);
                }
            }
        };
        actions.Children.Add(openBtn);

        var refreshBtn = StalkerTheme.StalkerButton(L.T("Обновить"), isPrimary: false, minWidth: 90);
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RefreshCommand)));
        actions.Children.Add(refreshBtn);

        var saveBtn = StalkerTheme.StalkerButton(L.T("СОХРАНИТЬ"), isPrimary: true, minWidth: 120);
        saveBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.SaveCommand)));
        saveBtn.Bind(ToolTip.TipProperty, new Binding(nameof(SaveLibraryViewModel.SaveDisabledReason)));
        actions.Children.Add(saveBtn);

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var border = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
        return border;
    }

    private static Control BuildSavesPane(SaveLibraryViewModel vm)
    {
        var dock = new DockPanel();

        var header = new TextBlock
        {
            Text = L.T("БИБЛИОТЕКА СОХРАНЕНИЙ"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.1,
            Margin = new Thickness(14, 12, 14, 8),
        };
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var saveList = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            ItemTemplate = StalkerTheme.Template<SaveFileSummary>(item =>
            {
                var stack = new StackPanel { Spacing = 3, Margin = new Thickness(6, 4) };
                stack.Children.Add(new TextBlock
                {
                    Text = item.DisplayName,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 13,
                });

                var subRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                subRow.Children.Add(new TextBlock
                {
                    Text = item.SlotTitle,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushTextSecondary,
                });
                var sizeText = new TextBlock
                {
                    Text = item.FileSizeDisplay,
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushTextMuted,
                };
                Grid.SetColumn(sizeText, 1);
                subRow.Children.Add(sizeText);
                stack.Children.Add(subRow);

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                if (item.Preview is { } preview)
                {
                    row.Children.Add(new Image { Source = preview, Width = 72, Height = 54, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 8, 0) });
                }

                Grid.SetColumn(stack, 1);
                row.Children.Add(stack);
                return row;
            }),
        };
        saveList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedSave))
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
        });
        saveList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.Saves)) { Source = vm });
        dock.Children.Add(saveList);

        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = dock,
        };
    }

    private static Control BuildWorkspacePane(SaveLibraryViewModel vm)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };

        var breadcrumb = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 9),
        };
        var breadcrumbParts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var group = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextMuted,
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        group.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.CurrentGroupTitle)) { Source = vm });
        breadcrumbParts.Children.Add(group);
        breadcrumbParts.Children.Add(new TextBlock
        {
            Text = "/",
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var page = new TextBlock
        {
            Foreground = StalkerTheme.BrushAccentAmber,
            FontFamily = StalkerTheme.HeadingFont,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        page.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.CurrentPageTitle)) { Source = vm });
        breadcrumbParts.Children.Add(page);
        breadcrumb.Child = breadcrumbParts;
        root.Children.Add(breadcrumb);

        // Content Area
        var contentGrid = new Grid();

        // Empty state
        var emptyState = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = L.T("ВЫБЕРИТЕ СОХРАНЕНИЕ"),
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = StalkerTheme.BrushAccentAmber,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = L.T("Выберите сейв из списка слева для просмотра и редактирования."),
                    Foreground = StalkerTheme.BrushTextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
        emptyState.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShouldShowEmptyState)));
        contentGrid.Children.Add(emptyState);

        // First Run Wizard
        var wizard = BuildFirstRunWizard(vm);
        wizard.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.IsFirstRunWizardVisible)));
        contentGrid.Children.Add(wizard);

        // Screens Container
        var screens = new Grid();

        // 1. Overview
        var overview = OverviewView.Build();
        overview.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowOverviewScreen)));
        screens.Children.Add(overview);

        // 2. Inventory
        var inventory = InventoryView.Build(vm);
        inventory.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowInventoryScreen)));
        screens.Children.Add(inventory);

        // 3. Factions
        var factions = FactionsView.Build(vm);
        factions.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowFactionsScreen)));
        screens.Children.Add(factions);

        // 4. Stashes
        var stashes = StashesView.Build(vm);
        stashes.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowStashesScreen)));
        screens.Children.Add(stashes);

        // 5. Transitions
        var transitions = TransitionsView.Build();
        transitions.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowTransitionsScreen)));
        screens.Children.Add(transitions);

        // 6. Backups
        var backups = BackupsView.Build(vm);
        backups.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowBackupsScreen)));
        screens.Children.Add(backups);

        var timeline = TimelineView.Build(vm.Timeline);
        timeline.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowTimelineScreen)) { Source = vm });
        screens.Children.Add(timeline);
        if (!HostPlatform.IsBrowser)
        {
            var encyclopedia = EncyclopediaView.Build(vm.Encyclopedia);
            encyclopedia.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowEncyclopediaScreen)) { Source = vm });
            screens.Children.Add(encyclopedia);
        }

        // 7. Settings
        var settings = SettingsView.Build(vm.Settings, vm.Diagnostics);
        settings.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowSettingsScreen)));
        screens.Children.Add(settings);

        // 8. Capabilities
        var capabilities = CapabilitiesView.Build(vm.Capabilities);
        capabilities.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCapabilitiesScreen)));
        screens.Children.Add(capabilities);

        // 9. Companion
        var companion = new CompanionView { DataContext = vm.Companion };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        companion.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCompanionScreen)) { Source = vm });
        screens.Children.Add(companion);

        // 10. Cloud
        var cloud = new CloudView { DataContext = vm.Cloud };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        cloud.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowCloudScreen)) { Source = vm });
        screens.Children.Add(cloud);
        // 10. Achievements
        var achievements = new AchievementsView { DataContext = vm.Achievements };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        achievements.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowAchievementsScreen)) { Source = vm });
        screens.Children.Add(achievements);
        // Game Doctor operates on an explicitly selected local installation.
        if (!HostPlatform.IsBrowser)
        {
            var gameFixes = new GameFixesView(vm.GameFixes);
            gameFixes.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowGameFixesScreen)) { Source = vm });
            screens.Children.Add(gameFixes);

            var saveDoctor = new SaveDoctorView(vm.SaveDoctor);
            saveDoctor.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowSaveDoctorScreen)) { Source = vm });
            screens.Children.Add(saveDoctor);

            var gameDoctor = new GameDoctorView(vm.GameDoctor);
            gameDoctor.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowGameDoctorScreen)) { Source = vm });
            screens.Children.Add(gameDoctor);

            var toolkitEnvironment = new ToolkitEnvironmentView(vm.ToolkitEnvironment);
            toolkitEnvironment.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowToolkitEnvironmentScreen)) { Source = vm });
            screens.Children.Add(toolkitEnvironment);
        }
        // 10. Updates
        var updates = new UpdatesView { DataContext = vm.Updates };
        // DataContext is the screen's own view model; visibility belongs to the window view model.
        updates.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveLibraryViewModel.ShowUpdatesScreen)) { Source = vm });
        screens.Children.Add(updates);

        contentGrid.Children.Add(screens);

        Grid.SetRow(contentGrid, 1);
        root.Children.Add(contentGrid);

        return root;
    }

    private static Control BuildStatusBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(14, 6, 14, 6),
        };

        var status = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.StatusMessage)));
        grid.Children.Add(status);

        var draftBadge = StalkerTheme.Badge(L.T("Сохранено"), StalkerTheme.BrushBgElevated, StalkerTheme.BrushAccentDim, 10);
        ((TextBlock)draftBadge.Child!).Bind(TextBlock.TextProperty, new Binding(nameof(SaveLibraryViewModel.DraftStatusText)));
        Grid.SetColumn(draftBadge, 1);
        grid.Children.Add(draftBadge);

        var border = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
        return border;
    }

    private static Control BuildFirstRunWizard(SaveLibraryViewModel vm)
    {
        if (HostPlatform.IsBrowser)
        {
            return new Border
            {
                Background = StalkerTheme.BrushBgPanel,
                BorderBrush = StalkerTheme.BrushAccentAmber,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(24),
                MaxWidth = 680,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = L.T("Откройте файл сохранения кнопкой «Открыть…» вверху.\n") +
                           L.T("Файл не покидает браузер: он разбирается и изменяется здесь, а после «Сохранить» скачивается обратно."),
                    Foreground = StalkerTheme.BrushTextSecondary,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        var card = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushAccentAmber,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(24),
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var stack = new StackPanel { Spacing = 14 };

        stack.Children.Add(new TextBlock
        {
            Text = L.T("МАСТЕР ПЕРВОГО ЗАПУСКА"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
        });

        stack.Children.Add(new TextBlock
        {
            Text = L.T("Сохранения S.T.A.L.K.E.R. не были найдены в стандартных каталогах.\nУкажите папку с файлами сохранений (savedgames или SaveGames) или запустите автоматический поиск на диске."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        // Auto-detect Button
        var autoDetectBtn = StalkerTheme.StalkerButton(L.T("АВТОПОИСК ПАПОК НА ДИСКЕ"), isPrimary: true, minWidth: 220);
        autoDetectBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.WizardAutoDetectCommand)));
        stack.Children.Add(autoDetectBtn);

        var orDivider = new TextBlock
        {
            Text = L.T("— ИЛИ УКАЖИТЕ ПУТЬ ВРУЧНУЮ —"),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
        };
        stack.Children.Add(orDivider);

        // Manual Input Row
        var inputRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
        };

        var input = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = L.T("Путь к папке с сейвами…"),
        };
        input.Bind(TextBox.TextProperty, new Binding(nameof(SaveLibraryViewModel.WizardDirectoryInput))
        {
            Mode = BindingMode.TwoWay,
        });
        inputRow.Children.Add(input);

        var browseBtn = StalkerTheme.StalkerButton(L.T("Обзор…"), isPrimary: false, minWidth: 80);
        browseBtn.Margin = new Thickness(8, 0, 0, 0);
        browseBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(browseBtn);
            if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanPickFolder)
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = L.T("Выберите папку с сохранениями"),
                    AllowMultiple = false,
                });
                if (folders.Count > 0 && folders[0].Path.LocalPath is { } path)
                {
                    vm.WizardDirectoryInput = path;
                }
            }
        };
        Grid.SetColumn(browseBtn, 1);
        inputRow.Children.Add(browseBtn);

        var addBtn = StalkerTheme.StalkerButton(L.T("Добавить"), isPrimary: false, minWidth: 90);
        addBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.WizardAddDirectoryCommand)));
        addBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(addBtn, 2);
        inputRow.Children.Add(addBtn);

        stack.Children.Add(inputRow);

        // Bottom Actions
        var bottomActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var settingsBtn = StalkerTheme.StalkerButton(L.T("Перейти в настройки"), isPrimary: false, minWidth: 140);
        settingsBtn.Click += (_, _) => vm.SelectedTab = "settings";
        bottomActions.Children.Add(settingsBtn);

        var dismissBtn = StalkerTheme.StalkerButton(L.T("Пропустить"), isPrimary: false, minWidth: 100);
        dismissBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.DismissWizardCommand)));
        bottomActions.Children.Add(dismissBtn);

        stack.Children.Add(bottomActions);

        card.Child = stack;
        return card;
    }

    private sealed class ActiveNavigationBrushConverter(IBrush active, IBrush inactive) : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is string selected && parameter is string tab && selected == tab ? active : inactive;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
