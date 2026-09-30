using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class CompareView
{
    private static readonly FuncValueConverter<bool, IBrush> FilterBackgroundConverter =
        new(selected => selected ? StalkerTheme.BrushBgHover : StalkerTheme.BrushBgPanel);

    private static readonly FuncValueConverter<bool, IBrush> FilterBorderConverter =
        new(selected => selected ? StalkerTheme.BrushAccentAmber : StalkerTheme.BrushBorderSubtle);

    private static readonly FuncValueConverter<bool, IBrush> FilterTextConverter =
        new(isSupported => isSupported ? StalkerTheme.BrushTextPrimary : StalkerTheme.BrushTextMuted);

    public static Control Build(CompareViewModel viewModel)
    {
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        content.Children.Add(BuildHeader());
        content.Children.Add(BuildCandidatePicker(viewModel));
        content.Children.Add(BuildSavePairAndSummary(viewModel));
        content.Children.Add(BuildFilterCard(viewModel));
        content.Children.Add(BuildDifferenceTable(viewModel));
        content.Children.Add(BuildUnsupportedActions(viewModel));

        return new Border
        {
            Name = "compare-screen",
            Background = StalkerTheme.BrushBgBase,
            DataContext = viewModel,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            },
        };
    }

    private static Control BuildHeader()
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("СРАВНЕНИЕ СОХРАНЕНИЙ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Сравнивайте выбранное сохранение с другим сейвом или его резервной копией. Показаны только данные, доступные для чтения."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        return stack;
    }

    private static Control BuildCandidatePicker(CompareViewModel viewModel)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new TextBlock
        {
            Text = L.T("Сравнить с"),
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var picker = new ComboBox
        {
            Name = "compare-candidate-picker",
            MinWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = StalkerTheme.BrushTextPrimary,
            PlaceholderText = L.T("Выберите сохранение или резервную копию"),
            ItemTemplate = new FuncDataTemplate<CompareCandidate>((_, _) =>
            {
                var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
                title.Bind(TextBlock.TextProperty, new Binding(nameof(CompareCandidate.Title)));
                return title;
            }),
        };
        picker.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CompareViewModel.Candidates)));
        picker.Bind(Visual.IsVisibleProperty, new Binding(nameof(CompareViewModel.HasCandidates)));
        picker.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(CompareViewModel.Selected))
        {
            Mode = BindingMode.TwoWay,
        });
        picker.Bind(InputElement.IsEnabledProperty, new Binding(nameof(CompareViewModel.HasCandidates)));
        var selection = new Grid();
        selection.Children.Add(picker);
        var noCandidates = new TextBlock
        {
            Name = "compare-no-candidates",
            Text = L.T("Нет других сейвов этой игры или бэкапов для сравнения."),
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        noCandidates.Bind(Visual.IsVisibleProperty, new Binding(nameof(CompareViewModel.HasCandidates))
        {
            Converter = new FuncValueConverter<bool, bool>(hasCandidates => !hasCandidates),
        });
        selection.Children.Add(noCandidates);
        Grid.SetColumn(selection, 1);
        row.Children.Add(selection);
        return row;
    }

    private static Control BuildSavePair(CompareViewModel viewModel)
    {
        var pair = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            ColumnSpacing = 8,
        };
        pair.Children.Add(BuildIdentityCard("A", "SideA", "SideASourceDisplay"));

        var swap = StalkerTheme.StalkerButton("⇄", isPrimary: false, minWidth: 52);
        swap.Name = "compare-swap";
        swap.Bind(Button.IsEnabledProperty, new Binding(nameof(CompareViewModel.CanSwapSides)));
        ToolTip.SetTip(swap, L.T("Поменять местами только отображение столбцов A и B"));
        swap.Click += (_, _) => viewModel.SwapSides();
        Grid.SetColumn(swap, 1);
        pair.Children.Add(swap);

        var cardB = BuildIdentityCard("B", "SideB", "SideBSourceDisplay");
        Grid.SetColumn(cardB, 2);
        pair.Children.Add(cardB);
        return pair;
    }

    private static Control BuildSavePairAndSummary(CompareViewModel viewModel)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,1.1*"),
            ColumnSpacing = 10,
        };
        row.Children.Add(BuildSavePair(viewModel));
        var summary = BuildSummary(viewModel);
        Grid.SetColumn(summary, 1);
        row.Children.Add(summary);
        return row;
    }

    private static Control BuildIdentityCard(string side, string candidatePath, string sourcePath)
    {
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        content.Children.Add(new TextBlock
        {
            Text = L.T("СОХРАНЕНИЕ {0}", side),
            Foreground = StalkerTheme.BrushAccentAmber,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
        });

        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0),
        };
        title.Bind(TextBlock.TextProperty, new Binding(candidatePath + ".Title") { TargetNullValue = "—" });
        Grid.SetRow(title, 1);
        content.Children.Add(title);

        var metadata = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var source = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Top,
        };
        source.Bind(TextBlock.TextProperty, new Binding(sourcePath) { TargetNullValue = "—" });
        metadata.Children.Add(source);
        var path = new TextBlock
        {
            FontSize = 10,
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        };
        path.Bind(TextBlock.TextProperty, new Binding(candidatePath + ".Path") { TargetNullValue = "—" });
        Grid.SetColumn(path, 1);
        metadata.Children.Add(path);
        Grid.SetRow(metadata, 2);
        content.Children.Add(metadata);

        return StalkerTheme.Card(content);
    }

    private static Control BuildSummary(CompareViewModel viewModel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 8,
        };
        AddSummaryCell(grid, L.T("РАЗЛИЧИЙ"), nameof(CompareViewModel.DifferenceCount), 0, StalkerTheme.BrushAccentAmber);
        AddSummaryCell(grid, L.T("ДОБАВЛЕНО"), nameof(CompareViewModel.AddedCount), 1, StalkerTheme.BrushSuccess);
        AddSummaryCell(grid, L.T("УДАЛЕНО"), nameof(CompareViewModel.RemovedCount), 2, StalkerTheme.BrushDanger);
        AddSummaryCell(grid, L.T("ИЗМЕНЕНО"), nameof(CompareViewModel.ChangedCount), 3, StalkerTheme.BrushWarning);
        return StalkerTheme.Card(grid, L.T("Итоги сравнения"));
    }

    private static void AddSummaryCell(Grid grid, string label, string bindingPath, int column, IBrush valueBrush)
    {
        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(4) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            Foreground = StalkerTheme.BrushTextMuted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var value = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = valueBrush,
        };
        value.Bind(TextBlock.TextProperty, new Binding(bindingPath));
        stack.Children.Add(value);
        var border = new Border
        {
            Background = StalkerTheme.BrushBgElevated,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4),
            Child = stack,
        };
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private static Control BuildFilterCard(CompareViewModel viewModel)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = L.T("ФИЛЬТРЫ СРАВНЕНИЯ"),
            Foreground = StalkerTheme.BrushAccentAmber,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
        });
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Категория"),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 11,
        });
        panel.Children.Add(BuildFilterOptions(
            viewModel,
            nameof(CompareViewModel.CategoryFilters),
            nameof(CompareViewModel.SelectCategoryCommand),
            "compare-category-filters"));
        panel.Children.Add(new TextBlock
        {
            Name = "compare-unsupported-categories-reason",
            Text = viewModel.UnsupportedCategoryReason,
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = L.T("Тип изменения"),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 11,
        });
        panel.Children.Add(BuildFilterOptions(
            viewModel,
            nameof(CompareViewModel.ChangeTypeFilters),
            nameof(CompareViewModel.SelectChangeTypeCommand),
            "compare-change-filters"));

        var controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
        };
        var search = new TextBox
        {
            Name = "compare-search",
            Watermark = L.T("Поиск по различиям"),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        search.Bind(TextBox.TextProperty, new Binding(nameof(CompareViewModel.SearchQuery))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        controls.Children.Add(search);

        var unchanged = new CheckBox
        {
            Name = "compare-show-unchanged",
            Content = new TextBlock
            {
                Text = L.T("Показывать без изменений"),
                Foreground = StalkerTheme.BrushTextMuted,
            },
            IsEnabled = false,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(unchanged, viewModel.UnchangedRowsDisabledReason);
        AutomationProperties.SetName(unchanged, L.T("Показывать без изменений"));
        Grid.SetColumn(unchanged, 1);
        controls.Children.Add(unchanged);
        panel.Children.Add(controls);

        var unchangedReason = new TextBlock
        {
            Name = "compare-show-unchanged-reason",
            Text = viewModel.UnchangedRowsDisabledReason,
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(unchangedReason);
        return StalkerTheme.Card(panel);
    }

    private static ItemsControl BuildFilterOptions(
        CompareViewModel viewModel,
        string itemsSourcePath,
        string commandPath,
        string name)
    {
        var control = new ItemsControl
        {
            Name = name,
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
            {
                ItemSpacing = 6,
                LineSpacing = 6,
            }),
            ItemTemplate = new FuncDataTemplate<CompareFilterOption>((_, _) =>
            {
                var content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                title.Bind(TextBlock.TextProperty, new Binding(nameof(CompareFilterOption.Title)));
                title.Bind(TextBlock.ForegroundProperty, new Binding(nameof(CompareFilterOption.IsSupported))
                {
                    Converter = FilterTextConverter,
                });
                content.Children.Add(title);
                var count = new TextBlock
                {
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushTextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                count.Bind(TextBlock.TextProperty, new Binding(nameof(CompareFilterOption.CountDisplay)));
                content.Children.Add(count);

                var button = new Button
                {
                    Padding = new Thickness(9, 5),
                    MinHeight = 30,
                    Content = content,
                    Background = StalkerTheme.BrushBgPanel,
                    BorderBrush = StalkerTheme.BrushBorderSubtle,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                };
                button.Bind(Button.CommandProperty, new Binding(commandPath) { Source = viewModel });
                button.Bind(Button.CommandParameterProperty, new Binding(nameof(CompareFilterOption.Id)));
                button.Bind(Button.IsEnabledProperty, new Binding(nameof(CompareFilterOption.IsSupported)));
                button.Bind(ToolTip.TipProperty, new Binding(nameof(CompareFilterOption.DisabledReason)));
                button.Bind(AutomationProperties.NameProperty, new Binding(nameof(CompareFilterOption.Title)));
                button.Bind(TemplatedControl.BackgroundProperty, new Binding(nameof(CompareFilterOption.IsSelected))
                {
                    Converter = FilterBackgroundConverter,
                });
                button.Bind(TemplatedControl.BorderBrushProperty, new Binding(nameof(CompareFilterOption.IsSelected))
                {
                    Converter = FilterBorderConverter,
                });
                return button;
            }),
        };
        control.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsSourcePath));
        return control;
    }

    private static Control BuildDifferenceTable(CompareViewModel viewModel)
    {
        var panel = new StackPanel { Spacing = 8 };
        var status = new TextBlock
        {
            Name = "compare-status",
            Foreground = StalkerTheme.BrushTextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(CompareViewModel.Status)));
        status.Bind(Visual.IsVisibleProperty, new Binding(nameof(CompareViewModel.HasVisibleRows))
        {
            Converter = new FuncValueConverter<bool, bool>(hasVisibleRows => !hasVisibleRows),
        });
        panel.Children.Add(status);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,1.1*,24,1.1*,95"),
            ColumnSpacing = 8,
            Margin = new Thickness(6, 0, 6, 4),
        };
        AddTableHeader(header, L.T("ТИП"), 0);
        AddTableHeader(header, L.T("ПАРАМЕТР / ОБЪЕКТ"), 1);
        AddTableHeader(header, L.T("ЗНАЧЕНИЕ A"), 2);
        AddTableHeader(header, "→", 3);
        AddTableHeader(header, L.T("ЗНАЧЕНИЕ B"), 4);
        AddTableHeader(header, L.T("КАТЕГОРИЯ"), 5);
        panel.Children.Add(header);

        var rows = new ItemsControl
        {
            Name = "compare-rows-list",
            ItemTemplate = new FuncDataTemplate<CompareDisplayRow>((_, _) => BuildDifferenceRow()),
        };
        rows.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CompareViewModel.VisibleRows)));
        panel.Children.Add(rows);

        var empty = new TextBlock
        {
            Name = "compare-no-visible-rows",
            Text = L.T("Здесь отображаются только различия, возвращённые текущей моделью сравнения."),
            Foreground = StalkerTheme.BrushTextMuted,
            Margin = new Thickness(10, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        empty.Bind(Visual.IsVisibleProperty, new Binding(nameof(CompareViewModel.HasVisibleRows))
        {
            Converter = new FuncValueConverter<bool, bool>(hasRows => !hasRows),
        });
        panel.Children.Add(empty);
        return StalkerTheme.Card(panel, L.T("Различия"));
    }

    private static Control BuildDifferenceRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,1.1*,24,1.1*,95"),
            ColumnSpacing = 8,
            Margin = new Thickness(6, 4),
        };
        AddBoundCell(grid, nameof(CompareDisplayRow.ChangeTypeDisplay), 0, StalkerTheme.BrushWarning);
        AddBoundCell(grid, nameof(CompareDisplayRow.Label), 1, StalkerTheme.BrushTextPrimary);
        AddBoundCell(grid, nameof(CompareDisplayRow.ValueA), 2, StalkerTheme.BrushTextSecondary);
        AddBoundCell(grid, "", 3, StalkerTheme.BrushTextMuted, "→");
        AddBoundCell(grid, nameof(CompareDisplayRow.ValueB), 4, StalkerTheme.BrushAccentAmber);
        AddBoundCell(grid, nameof(CompareDisplayRow.CategoryDisplay), 5, StalkerTheme.BrushTextMuted);
        return grid;
    }

    private static void AddTableHeader(Grid grid, string text, int column)
    {
        var header = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushTextMuted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(header, column);
        grid.Children.Add(header);
    }

    private static void AddBoundCell(Grid grid, string path, int column, IBrush foreground, string? literal = null)
    {
        var cell = new TextBlock
        {
            Foreground = foreground,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (literal is not null) cell.Text = literal;
        else cell.Bind(TextBlock.TextProperty, new Binding(path));
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static Control BuildUnsupportedActions(CompareViewModel viewModel)
    {
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        actions.Children.Add(DisabledAction("compare-export", L.T("Экспорт CSV"), viewModel.UnsupportedExportReason));
        actions.Children.Add(DisabledAction("compare-copy-list", L.T("Копировать список"), viewModel.UnsupportedExportReason));
        actions.Children.Add(DisabledAction("compare-apply", L.T("Применить выбранные различия"), viewModel.UnsupportedApplyReason, isPrimary: true));
        return actions;
    }

    private static Control DisabledAction(string name, string title, string reason, bool isPrimary = false)
    {
        var button = StalkerTheme.StalkerButton(title, isPrimary, minWidth: 135);
        button.Name = name;
        button.IsEnabled = false;
        ToolTip.SetTip(button, reason);
        AutomationProperties.SetName(button, title);
        return new StackPanel
        {
            Name = name + "-group",
            Spacing = 4,
            Width = 205,
            Children =
            {
                button,
                new TextBlock
                {
                    Name = name + "-reason",
                    Text = reason,
                    Foreground = StalkerTheme.BrushTextMuted,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
    }
}
