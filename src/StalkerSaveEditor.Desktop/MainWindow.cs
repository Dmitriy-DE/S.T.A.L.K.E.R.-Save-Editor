using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly SaveLibraryViewModel _viewModel;

    public MainWindow(SaveLibraryViewModel? viewModel = null)
    {
        _viewModel = viewModel ?? new SaveLibraryViewModel();
        DataContext = _viewModel;
        // The view model owns a timer and background work; they end with the window.
        Closed += (_, _) => _viewModel.Dispose();

        Title = "S.T.A.L.K.E.R. Save Editor";
        FontFamily = StalkerTheme.BodyFont;
        Width = ScreenshotDimension("STALKER_EDITOR_SCREENSHOT_WIDTH", 1260);
        Height = ScreenshotDimension("STALKER_EDITOR_SCREENSHOT_HEIGHT", 820);
        MinWidth = 940;
        MinHeight = 600;
        Background = StalkerTheme.BrushBgBase;

        Content = BuildRoot(_viewModel);
        if (SaveLibraryViewModel.InteractiveApp)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Opened += (_, _) => FitToScreen();
        }
        else
        {
            _viewModel.Settings.AutoUiScalePercent = SettingsViewModel.AutoScaleFor(Width);
        }
    }

    /// <summary>
    /// The window opens almost as large as the display allows and the interface is enlarged to match: the fixed
    /// 1260 × 820 start left small print in a corner of a Full HD screen.
    /// </summary>
    private void FitToScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var area = screen.WorkingArea;
        double width = area.Width / scaling, height = area.Height / scaling;
        _viewModel.Settings.AutoUiScalePercent = SettingsViewModel.AutoScaleFor(width);
        if (WindowState != WindowState.Normal) return;
        Width = Math.Max(MinWidth, Math.Floor(width * 0.94));
        Height = Math.Max(MinHeight, Math.Floor(height * 0.92));
        Position = new PixelPoint(
            area.X + (int)((area.Width - Width * scaling) / 2),
            area.Y + (int)((area.Height - Height * scaling) / 2));
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

        // While a save is being written nothing may be edited or selected: the rows on screen belong to the old file.
        var idle = new Binding(nameof(SaveLibraryViewModel.IsSaving)) { Source = vm, Converter = Avalonia.Data.Converters.BoolConverters.Not };
        savesPane.Bind(InputElement.IsEnabledProperty, idle);
        workspacePane.Bind(InputElement.IsEnabledProperty, idle);
        topBar.Bind(InputElement.IsEnabledProperty, idle);

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

    private static Control BuildTopBar(SaveLibraryViewModel vm)
    {
        var grid = new Grid
        {
            // The commands keep their size; the title gives way (a narrow window at 125% used to push Save off screen).
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(18, 12, 18, 12),
        };

        // App title & badge
        var titleStack = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
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
        Grid.SetColumn(releaseBadge, 1);
        titleStack.Children.Add(releaseBadge);
        grid.Children.Add(titleStack);
        grid.SizeChanged += (_, args) => releaseBadge.IsVisible = args.NewSize.Width >= 1000;

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

        Grid.SetColumn(actions, 1);
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

    private sealed class ActiveNavigationBrushConverter(IBrush active, IBrush inactive) : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is string selected && parameter is string tab && selected == tab ? active : inactive;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
