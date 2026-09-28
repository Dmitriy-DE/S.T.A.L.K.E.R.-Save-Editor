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
        Width = 1260;
        Height = 820;
        MinWidth = 940;
        MinHeight = 600;
        Background = StalkerTheme.BrushBgBase;

        var content = BuildContent(_viewModel);
        AddShortcuts(content, _viewModel);
        Content = content;
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
        var root = BuildContent(vm);
        root.DataContext = vm;
        AddShortcuts(root, vm);
        root.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, StalkerTheme.BodyFont);
        return root;
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

        // 3. Middle Area: Left Saves Pane (310) + Right Workspace
        var middle = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("310,*"),
        };

        var savesPane = BuildSavesPane();
        middle.Children.Add(savesPane);

        var workspacePane = BuildWorkspacePane(vm);
        Grid.SetColumn(workspacePane, 1);
        middle.Children.Add(workspacePane);

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
            Background = new SolidColorBrush(Color.Parse("#1F1C12")),
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
            Background = new SolidColorBrush(Color.Parse("#2B1A18")),
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
            Background = new SolidColorBrush(Color.Parse("#1A2B18")),
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

                    if (!vm.AddPreviewSave(path)) vm.StatusMessage = L.T("«{0}» — не сохранение S.T.A.L.K.E.R. или файл повреждён.", file.Name);
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

    private static Control BuildSavesPane()
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
        saveList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.Saves)));
        saveList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedSave))
        {
            Mode = BindingMode.TwoWay,
        });
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

        // Nav Tabs Rail
        var navBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(14, 8, 14, 8),
        };

        navBar.Children.Add(MakeNavTab(vm, L.T("ОБЗОР"), "overview", nameof(SaveLibraryViewModel.IsOverviewTab)));
        navBar.Children.Add(MakeNavTab(vm, L.T("ИНВЕНТАРЬ"), "inventory", nameof(SaveLibraryViewModel.IsInventoryTab)));
        navBar.Children.Add(MakeNavTab(vm, L.T("ФРАКЦИИ"), "factions", nameof(SaveLibraryViewModel.IsFactionsTab)));
        navBar.Children.Add(MakeNavTab(vm, L.T("ТАЙНИКИ"), "stashes", nameof(SaveLibraryViewModel.IsStashesTab)));
        navBar.Children.Add(MakeNavTab(vm, L.T("ПЕРЕХОДЫ"), "transitions", nameof(SaveLibraryViewModel.IsTransitionsTab)));
        navBar.Children.Add(MakeNavTab(vm, L.T("БЭКАПЫ"), "backups", nameof(SaveLibraryViewModel.IsBackupsTab)));
        navBar.Children.Add(MakeNavTab(vm, L.T("ВОЗМОЖНОСТИ"), "capabilities", nameof(SaveLibraryViewModel.IsCapabilitiesTab)));
        if (!HostPlatform.IsBrowser) navBar.Children.Add(MakeNavTab(vm, L.T("КОМПАНЬОН"), "companion", nameof(SaveLibraryViewModel.IsCompanionTab)));
        if (!HostPlatform.IsBrowser) navBar.Children.Add(MakeNavTab(vm, L.T("ОБЛАКО"), "cloud", nameof(SaveLibraryViewModel.IsCloudTab)));
        if (!HostPlatform.IsBrowser) navBar.Children.Add(MakeNavTab(vm, L.T("ДОСТИЖЕНИЯ"), "achievements", nameof(SaveLibraryViewModel.IsAchievementsTab)));
        if (!HostPlatform.IsBrowser) navBar.Children.Add(MakeNavTab(vm, L.T("ОБНОВЛЕНИЯ"), "updates", nameof(SaveLibraryViewModel.IsUpdatesTab)));
        navBar.Children.Add(MakeNavTab(vm, L.T("НАСТРОЙКИ"), "settings", nameof(SaveLibraryViewModel.IsSettingsTab)));

        var navBorder = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            // Tabs scroll instead of widening the whole workspace past the window edge.
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = navBar,
            },
        };
        root.Children.Add(navBorder);

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

    private static Button MakeNavTab(SaveLibraryViewModel vm, string label, string tabName, string isTabProperty)
    {
        var btn = new Button
        {
            Content = label,
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(14, 7),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
        };
        btn.Bind(Button.BackgroundProperty, new Binding(isTabProperty)
        {
            Converter = new BoolToBrushConverter(StalkerTheme.BrushAccentAmber, StalkerTheme.BrushBgElevated),
        });
        btn.Bind(Button.ForegroundProperty, new Binding(isTabProperty)
        {
            Converter = new BoolToBrushConverter(new SolidColorBrush(Color.Parse("#0C0D0A")), StalkerTheme.BrushTextSecondary),
        });
        btn.Bind(Button.BorderBrushProperty, new Binding(isTabProperty)
        {
            Converter = new BoolToBrushConverter(StalkerTheme.BrushAccentAmber, StalkerTheme.BrushBorderSubtle),
        });
        btn.Click += (_, _) => vm.SelectedTab = tabName;
        return btn;
    }

    private static Control BuildStatusBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
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

        var sha = new TextBlock
        {
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        sha.Bind(TextBlock.TextProperty, new Binding("SelectedSave.SourceSha256")
        {
            StringFormat = "SHA: {0}",
        });
        Grid.SetColumn(sha, 2);
        grid.Children.Add(sha);

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

    private sealed class BoolToBrushConverter(IBrush trueBrush, IBrush falseBrush) : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? trueBrush : falseBrush;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
