using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class SaveDoctorView : UserControl
{
    public SaveDoctorView(SaveDoctorViewModel viewModel)
    {
        DataContext = viewModel;
        Content = Build(viewModel);
    }

    private static Control Build(SaveDoctorViewModel viewModel)
    {
        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            Margin = new Thickness(24),
            RowSpacing = 14,
        };
        page.Children.Add(new TextBlock
        {
            Text = L.T("ДОКТОР СОХРАНЕНИЯ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });

        var instructions = new TextBlock
        {
            Text = L.T("ПРОВЕРКА ЧИТАЕМОСТИ ФОРМАТА. СЕМАНТИЧЕСКИЕ ПРАВИЛА И РЕМОНТ ДОСТУПНЫ ТОЛЬКО ПРИ НАЛИЧИИ ПРОВЕРЕННЫХ ДАННЫХ."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
        };
        Grid.SetRow(instructions, 1);
        page.Children.Add(instructions);

        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        var path = new TextBox
        {
            Watermark = L.T("Файл"),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        path.Bind(TextBox.TextProperty, new Binding(nameof(SaveDoctorViewModel.SavePath))
        {
            Mode = BindingMode.TwoWay,
            Source = viewModel,
        });
        pathRow.Children.Add(path);

        var browse = StalkerTheme.StalkerButton(L.T("Открыть сохранение"), isPrimary: false, minWidth: 150);
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(browse)?.StorageProvider is not { CanOpen: true } storage) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L.T("Открыть сохранение"),
                AllowMultiple = false,
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } selectedPath) viewModel.SavePath = selectedPath;
        };
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);

        var analyze = StalkerTheme.StalkerButton(L.T("ПРОВЕРИТЬ СОХРАНЕНИЕ"), isPrimary: true, minWidth: 180);
        analyze.Bind(Button.CommandProperty, new Binding(nameof(SaveDoctorViewModel.AnalyzeCommand)) { Source = viewModel });
        Grid.SetColumn(analyze, 2);
        pathRow.Children.Add(analyze);
        Grid.SetRow(pathRow, 2);
        page.Children.Add(pathRow);

        var report = new StackPanel { Spacing = 8 };
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = StalkerTheme.BrushTextSecondary,
            FontWeight = FontWeight.SemiBold,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(SaveDoctorViewModel.Status)) { Source = viewModel });
        report.Children.Add(status);
        report.Children.Add(new ItemsControl
        {
            ItemsSource = viewModel.Checks,
            ItemTemplate = new FuncDataTemplate<SaveDoctorCheckRow>((row, _) => CreateCheckRow(row), true),
        });
        var reportBorder = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14),
            Child = new ScrollViewer { Content = report, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
        };
        reportBorder.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveDoctorViewModel.HasReport)) { Source = viewModel });
        Grid.SetRow(reportBorder, 3);
        page.Children.Add(reportBorder);

        var empty = new TextBlock
        {
            Text = L.T("ВЫБЕРИТЕ ФАЙЛ СОХРАНЕНИЯ ДЛЯ ПРОВЕРКИ."),
            Foreground = StalkerTheme.BrushTextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        empty.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveDoctorViewModel.ShowEmptyState)) { Source = viewModel });
        Grid.SetRow(empty, 3);
        page.Children.Add(empty);
        return page;
    }

    private static Control CreateCheckRow(SaveDoctorCheckRow? row)
    {
        if (row is null) return new Border();
        var color = row.Status switch
        {
            SaveDoctorStatus.Ok => StalkerTheme.BrushSuccess,
            SaveDoctorStatus.Warning => StalkerTheme.BrushAccentAmber,
            SaveDoctorStatus.Error => StalkerTheme.BrushDanger,
            _ => StalkerTheme.BrushTextSecondary,
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        grid.Children.Add(new TextBlock { Text = row.Mark, Foreground = color, FontWeight = FontWeight.Bold });
        var details = new StackPanel { Spacing = 3 };
        details.Children.Add(new TextBlock { Text = row.Name, Foreground = StalkerTheme.BrushTextPrimary, FontWeight = FontWeight.SemiBold });
        details.Children.Add(new TextBlock { Text = row.Detail, Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        return new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8),
            Child = grid,
        };
    }
}
