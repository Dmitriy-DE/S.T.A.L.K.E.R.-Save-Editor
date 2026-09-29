using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class GamesOverviewView
{
    private static readonly BoolVisibilityConverter VisibleWhenTrue = new(invert: false);
    private static readonly EmptyTextConverter IsEmpty = new(invert: false);
    private static readonly EmptyTextConverter IsNotEmpty = new(invert: true);

    public static Control Build(SaveLibraryViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        var page = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        page.Children.Add(BuildHeader());

        var workspace = BuildWorkspace(vm);
        page.Children.Add(workspace);
        page.Children.Add(BuildToolActions(vm));
        page.Children.Add(BuildUnsupportedModsNotice());

        return new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    private static Control BuildHeader()
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("ИГРЫ И ИНСТРУМЕНТЫ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Управляйте установленными играми, исправлениями, средой и игровыми инструментами."),
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        return stack;
    }

    private static Grid BuildWorkspace(SaveLibraryViewModel vm)
    {
        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,1.15*"),
            ColumnSpacing = 12,
        };
        var installations = BuildInstallationsPane(vm);
        installations.MinHeight = 270;
        var selectedGame = BuildSelectedGamePane(vm);
        selectedGame.MinHeight = 270;
        workspace.Children.Add(installations);
        Grid.SetColumn(selectedGame, 1);
        workspace.Children.Add(selectedGame);

        var stacked = false;
        workspace.SizeChanged += (_, args) =>
        {
            var shouldStack = args.NewSize.Width < 760;
            if (shouldStack == stacked) return;
            stacked = shouldStack;
            workspace.ColumnDefinitions.Clear();
            workspace.RowDefinitions.Clear();
            if (stacked)
            {
                workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                workspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                workspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                workspace.RowSpacing = 10;
                Grid.SetColumn(selectedGame, 0);
                Grid.SetRow(selectedGame, 1);
            }
            else
            {
                workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.15, GridUnitType.Star)));
                workspace.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
                workspace.RowSpacing = 0;
                Grid.SetColumn(selectedGame, 1);
                Grid.SetRow(selectedGame, 0);
            }
        };
        return workspace;
    }

    private static Control BuildInstallationsPane(SaveLibraryViewModel vm)
    {
        var title = new TextBlock
        {
            Text = L.T("НАЙДЕННЫЕ УСТАНОВКИ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = StalkerTheme.BrushAccentAmber,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var count = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        count.Bind(TextBlock.TextProperty, new Binding("Installations.Count")
        {
            Source = vm.GameDoctor,
            StringFormat = L.T("Найдено: {0}"),
        });
        var discover = StalkerTheme.StalkerButton(L.T("Найти установки"), isPrimary: true, minWidth: 158);
        discover.Name = "discover-installations";
        Avalonia.Automation.AutomationProperties.SetName(discover, L.T("Найти установки игр"));
        discover.Bind(Button.CommandProperty, new Binding(nameof(GameDoctorViewModel.DiscoverInstallationsCommand)) { Source = vm.GameDoctor });

        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            Children = { title, count, discover },
        };
        Grid.SetColumn(count, 1);
        Grid.SetColumn(discover, 2);

        var pending = new TextBlock
        {
            Text = L.T("Поиск установок ещё не выполнялся."),
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
        };
        pending.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.IsDiscoveryIdle))
        {
            Source = vm.GameDoctor,
            Converter = VisibleWhenTrue,
        });
        var discoveryResult = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushAccentDim,
            TextWrapping = TextWrapping.Wrap,
        };
        discoveryResult.Bind(TextBlock.TextProperty, new Binding(nameof(GameDoctorViewModel.DiscoveryStatus)) { Source = vm.GameDoctor });
        discoveryResult.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.DiscoveryStatus))
        {
            Source = vm.GameDoctor,
            Converter = IsNotEmpty,
        });
        var searching = new TextBlock
        {
            Text = L.T("Поиск установок…"),
            FontSize = 11,
            Foreground = StalkerTheme.BrushAccentAmber,
        };
        searching.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.IsDiscovering))
        {
            Source = vm.GameDoctor,
            Converter = VisibleWhenTrue,
        });

        var rows = new ListBox
        {
            ItemsSource = vm.GameDoctor.Installations,
            ItemTemplate = StalkerTheme.Template<GameDoctorInstallationOption>(_ => CreateInstallationRow()),
            Background = StalkerTheme.BrushBgBase,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            MinHeight = 150,
        };
        Avalonia.Automation.AutomationProperties.SetName(rows, L.T("Найденные установки игр"));
        rows.Bind(ListBox.SelectedItemProperty, new Binding(nameof(GameDoctorViewModel.SelectedInstallation))
        {
            Source = vm.GameDoctor,
            Mode = BindingMode.TwoWay,
        });

        var empty = new StackPanel
        {
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18),
        };
        empty.Children.Add(new TextBlock
        {
            Text = L.T("Установки не выбраны"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var noMatches = new TextBlock
        {
            Text = L.T("Если поиск ничего не нашёл, укажите папку игры в Докторе игры."),
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        noMatches.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.DiscoveryStatus))
        {
            Source = vm.GameDoctor,
            Converter = IsNotEmpty,
        });
        var noSearch = new TextBlock
        {
            Text = L.T("Нажмите «Найти установки», чтобы проверить поддерживаемые игры на этом компьютере."),
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        noSearch.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.IsDiscoveryIdle))
        {
            Source = vm.GameDoctor,
            Converter = VisibleWhenTrue,
        });
        empty.Children.Add(noSearch);
        empty.Children.Add(noMatches);
        var openDoctor = StalkerTheme.StalkerButton(L.T("Открыть Доктор игры"), isPrimary: false, minWidth: 178);
        openDoctor.Click += (_, _) => OpenGameDoctor(vm);
        empty.Children.Add(openDoctor);

        var listHost = new Grid { Children = { rows, empty } };
        void UpdateRowsState()
        {
            var hasRows = vm.GameDoctor.Installations.Count > 0;
            rows.IsVisible = hasRows;
            empty.IsVisible = !hasRows;
        }
        vm.GameDoctor.Installations.CollectionChanged += (_, _) => UpdateRowsState();
        UpdateRowsState();

        vm.GameDoctor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(GameDoctorViewModel.SelectedInstallation)) return;
            if (vm.GameDoctor.SelectedInstallation is not { } installation)
            {
                vm.GameFixes.GameDirectory = string.Empty;
                return;
            }

            if (vm.GameFixes.Targets.FirstOrDefault(target => target.Target == installation.Target) is { } target)
                vm.GameFixes.SelectedTarget = target;
            vm.GameFixes.GameDirectory = installation.Directory;
        };

        var content = new StackPanel
        {
            Spacing = 8,
            Children = { heading, pending, discoveryResult, searching, listHost },
        };
        return StalkerTheme.Card(content, L.T("Мои игры"), margin: new Thickness(0));
    }

    private static Control CreateInstallationRow()
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(8, 6),
        };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(GameDoctorInstallationOption.Title)));
        text.Children.Add(title);
        var directory = new TextBlock
        {
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        directory.Bind(TextBlock.TextProperty, new Binding(nameof(GameDoctorInstallationOption.Directory)));
        text.Children.Add(directory);
        root.Children.Add(text);

        var source = StalkerTheme.Badge(string.Empty, StalkerTheme.BrushBgElevated, StalkerTheme.BrushTextSecondary, 10);
        ((TextBlock)source.Child!).Bind(TextBlock.TextProperty, new Binding(nameof(GameDoctorInstallationOption.SourceDisplay)));
        Grid.SetColumn(source, 1);
        root.Children.Add(source);
        return root;
    }

    private static Control BuildSelectedGamePane(SaveLibraryViewModel vm)
    {
        var targetPicker = new ComboBox
        {
            ItemsSource = vm.GameFixes.Targets,
            ItemTemplate = new FuncDataTemplate<GameTargetOption>((_, _) =>
            {
                var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
                title.Bind(TextBlock.TextProperty, new Binding(nameof(GameTargetOption.Title)));
                return title;
            }),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        targetPicker.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(GameFixesViewModel.SelectedTarget))
        {
            Source = vm.GameFixes,
            Mode = BindingMode.TwoWay,
        });
        Avalonia.Automation.AutomationProperties.SetName(targetPicker, L.T("Выбранная игра"));
        vm.GameFixes.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(GameFixesViewModel.SelectedTarget)) return;

            var target = vm.GameFixes.SelectedTarget.Target;
            if (vm.GameDoctor.Targets.FirstOrDefault(option => option.Target == target) is { } doctorTarget)
                vm.GameDoctor.SelectedTarget = doctorTarget;

            if (vm.GameDoctor.SelectedInstallation?.Target == target) return;
            vm.GameDoctor.SelectedInstallation = null;
            vm.GameDoctor.GameDirectory = string.Empty;
            vm.GameFixes.GameDirectory = string.Empty;
        };

        var title = new TextBlock
        {
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
        };
        title.Bind(TextBlock.TextProperty, new Binding("SelectedTarget.Title") { Source = vm.GameFixes });
        var supported = new TextBlock
        {
            Text = L.T("Игра в каталоге"),
            Foreground = StalkerTheme.BrushSuccess,
            FontSize = 11,
        };

        var identity = new StackPanel
        {
            Spacing = 4,
            Children = { title, supported },
        };

        var detailGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("130,*"),
            RowSpacing = 6,
            ColumnSpacing = 8,
        };
        AddDetailRow(detailGrid, 0, L.T("Статус"), SelectedInstallationValue(vm, nameof(GameDoctorViewModel.SelectedInstallationStatus), L.T("Не выбрана")));
        AddDetailRow(detailGrid, 1, L.T("Платформа"), SelectedInstallationValue(vm, "SelectedInstallation.SourceDisplay", "—"));
        AddDetailRow(detailGrid, 2, L.T("Номер сборки"), SelectedInstallationValue(vm, "SelectedInstallation.BuildDisplay", "—"));
        AddDetailRow(detailGrid, 3, L.T("Папка игры"), SelectedInstallationValue(vm, "SelectedInstallation.Directory", "—"));

        var catalogueState = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
        };
        catalogueState.Bind(TextBlock.TextProperty, new Binding(nameof(GameFixesViewModel.CatalogueStatus)) { Source = vm.GameFixes });

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,320"),
            ColumnSpacing = 12,
            Children = { identity, targetPicker },
        };
        Grid.SetColumn(targetPicker, 1);

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                header,
                new Border
                {
                    Background = StalkerTheme.BrushBgElevated,
                    BorderBrush = StalkerTheme.BrushBorderSubtle,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(12),
                    Child = detailGrid,
                },
                catalogueState,
            },
        };

        return StalkerTheme.Card(content, L.T("Выбранная игра"), margin: new Thickness(0));
    }

    private static TextBlock SelectedInstallationValue(SaveLibraryViewModel vm, string path, string emptyValue)
    {
        var value = new TextBlock
        {
            Foreground = StalkerTheme.BrushTextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11,
        };
        value.Bind(TextBlock.TextProperty, new Binding(path)
        {
            Source = vm.GameDoctor,
            FallbackValue = emptyValue,
            TargetNullValue = emptyValue,
        });
        if (path == "SelectedInstallation.Directory")
            value.Bind(ToolTip.TipProperty, new Binding(path) { Source = vm.GameDoctor, FallbackValue = emptyValue, TargetNullValue = emptyValue });
        return value;
    }

    private static void AddDetailRow(Grid grid, int row, string label, Control value)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        Grid.SetRow(grid.Children[^2], row);
    }

    private static Control BuildToolActions(SaveLibraryViewModel vm)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"),
            ColumnSpacing = 8,
        };
        var fixes = MakeActionButton(L.T("Исправления"), L.T("Проверить и установить вручную"), "⚒", () => vm.SelectedTab = "game-fixes");
        grid.Children.Add(fixes);
        var doctor = MakeActionButton(L.T("Доктор игры"), L.T("Проверить выбранную установку"), "⌖", () => OpenGameDoctor(vm));
        Grid.SetColumn(doctor, 1);
        grid.Children.Add(doctor);
        var environment = MakeActionButton(L.T("Среда игры"), L.T("Снимки, профили и конфигурация"), "⚙", () => vm.SelectedTab = "toolkit-environment");
        Grid.SetColumn(environment, 2);
        grid.Children.Add(environment);
        var companion = MakeActionButton(L.T("Компаньон"), L.T("Состояние и управление компаньоном"), "●", () => vm.SelectedTab = "companion");
        companion.Name = "open-companion";
        Grid.SetColumn(companion, 3);
        grid.Children.Add(companion);
        var achievements = MakeActionButton(L.T("Достижения"), L.T("Просмотр и управление достижениями Steam"), "★", () => vm.SelectedTab = "achievements");
        Grid.SetColumn(achievements, 4);
        grid.Children.Add(achievements);

        var companionReason = new TextBlock
        {
            Name = "companion-disabled-reason",
            Text = L.T("Компаньон недоступен для этой игры или издания."),
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 0),
        };
        companionReason.IsVisible = false;

        void UpdateCompanionAvailability()
        {
            var target = vm.GameFixes.SelectedTarget.Target;
            var isExperimentalS2 = target == GameTarget.Stalker2;
            var canUseCompanion = GameTargetCatalog.Get(target).CompanionSupported || isExperimentalS2;
            companion.IsEnabled = canUseCompanion;
            companionReason.Text = isExperimentalS2
                ? L.T("Для S.T.A.L.K.E.R. 2 показано только экспериментальное состояние; установка выполняется отдельно по запросу.")
                : L.T("Компаньон недоступен для этой игры или издания.");
            companionReason.IsVisible = !canUseCompanion || isExperimentalS2;
        }
        vm.GameFixes.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null or nameof(GameFixesViewModel.SelectedTarget)) UpdateCompanionAvailability();
        };
        UpdateCompanionAvailability();

        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(new TextBlock
        {
            Text = L.T("БЫСТРЫЕ ДЕЙСТВИЯ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        root.Children.Add(grid);
        root.Children.Add(companionReason);
        return root;
    }

    private static Button MakeActionButton(string title, string description, string glyph, Action activate)
    {
        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(new TextBlock
        {
            Text = $"{glyph}  {title}",
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        var button = StalkerTheme.StalkerButton(string.Empty, isPrimary: false, minWidth: 110);
        button.Content = content;
        button.MinHeight = 62;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Click += (_, _) => activate();
        return button;
    }

    private static Control BuildUnsupportedModsNotice()
    {
        var button = StalkerTheme.StalkerButton(L.T("Моды"), isPrimary: false, minWidth: 92);
        button.Name = "unsupported-mods";
        button.IsEnabled = false;
        var reason = new TextBlock
        {
            Name = "unsupported-mods-reason",
            Text = L.T("Отдельный менеджер модов отсутствует; используйте профили в разделе «Среда игры»."),
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, reason.Text);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { button, reason },
        };
        return StalkerTheme.Card(row, L.T("МОДЫ"), margin: new Thickness(0));
    }

    private static void OpenGameDoctor(SaveLibraryViewModel vm)
    {
        var target = vm.GameFixes.SelectedTarget.Target;
        if (vm.GameDoctor.Targets.FirstOrDefault(option => option.Target == target) is { } doctorTarget)
            vm.GameDoctor.SelectedTarget = doctorTarget;
        vm.GameDoctor.GameDirectory = vm.GameFixes.GameDirectory;
        vm.SelectedTab = "game-doctor";
    }

    private sealed class BoolVisibilityConverter(bool invert) : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            (value is bool enabled && enabled) != invert;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyTextConverter(bool invert) : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var empty = string.IsNullOrWhiteSpace(value as string);
            return empty != invert;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
