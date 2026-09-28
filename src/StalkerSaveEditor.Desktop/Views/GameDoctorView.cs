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

public sealed class GameDoctorView : UserControl
{
    public GameDoctorView(GameDoctorViewModel viewModel)
    {
        DataContext = viewModel;
        Content = Build(viewModel);
    }

    private static Control Build(GameDoctorViewModel viewModel)
    {
        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"),
            Margin = new Thickness(24),
            RowSpacing = 14,
        };
        page.Children.Add(new TextBlock
        {
            Text = L.T("ДОКТОР ИГРЫ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });

        var targetRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        targetRow.Children.Add(new TextBlock
        {
            Text = L.T("ИГРА"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StalkerTheme.BrushTextSecondary,
        });
        var target = new ComboBox
        {
            ItemsSource = viewModel.Targets,
            ItemTemplate = new FuncDataTemplate<GameTargetOption>((option, _) => new TextBlock { Text = option?.Title }),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        target.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(GameDoctorViewModel.SelectedTarget))
        {
            Mode = BindingMode.TwoWay,
            Source = viewModel,
        });
        Grid.SetColumn(target, 1);
        targetRow.Children.Add(target);

        var findInstallations = StalkerTheme.StalkerButton(L.T("НАЙТИ УСТАНОВКИ"), isPrimary: false, minWidth: 160);
        findInstallations.Bind(Button.CommandProperty, new Binding(nameof(GameDoctorViewModel.DiscoverInstallationsCommand)) { Source = viewModel });
        Grid.SetColumn(findInstallations, 2);
        targetRow.Children.Add(findInstallations);
        Grid.SetRow(targetRow, 1);
        page.Children.Add(targetRow);

        var installationRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        var installationLabel = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        installationLabel.Children.Add(new TextBlock
        {
            Text = L.T("ОБНАРУЖЕННЫЕ УСТАНОВКИ"),
            Foreground = StalkerTheme.BrushTextSecondary,
        });
        var discoveryStatus = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushAccentAmber,
        };
        discoveryStatus.Bind(TextBlock.TextProperty, new Binding(nameof(GameDoctorViewModel.DiscoveryStatus)) { Source = viewModel });
        installationLabel.Children.Add(discoveryStatus);
        installationRow.Children.Add(installationLabel);
        var installations = new ComboBox
        {
            ItemsSource = viewModel.Installations,
            ItemTemplate = new FuncDataTemplate<GameDoctorInstallationOption>((option, _) => new TextBlock
            {
                Text = option?.Label,
                TextWrapping = TextWrapping.Wrap,
            }, true),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        installations.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(GameDoctorViewModel.SelectedInstallation))
        {
            Mode = BindingMode.TwoWay,
            Source = viewModel,
        });
        Grid.SetColumn(installations, 1);
        installationRow.Children.Add(installations);
        Grid.SetRow(installationRow, 2);
        page.Children.Add(installationRow);

        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        var path = new TextBox
        {
            Watermark = L.T("ПАПКА ИГРЫ (РУЧНОЙ ВЫБОР)"),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        path.Bind(TextBox.TextProperty, new Binding(nameof(GameDoctorViewModel.GameDirectory))
        {
            Mode = BindingMode.TwoWay,
            Source = viewModel,
        });
        pathRow.Children.Add(path);

        var browse = StalkerTheme.StalkerButton(L.T("Обзор…"), isPrimary: false, minWidth: 90);
        browse.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(browse);
            if (topLevel?.StorageProvider is not { CanPickFolder: true } storageProvider) return;
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L.T("ПАПКА ИГРЫ (РУЧНОЙ ВЫБОР)"),
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].Path.LocalPath is { } selectedPath)
            {
                viewModel.GameDirectory = selectedPath;
            }
        };
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);

        var analyze = StalkerTheme.StalkerButton(L.T("ПРОВЕРИТЬ УСТАНОВКУ"), isPrimary: true, minWidth: 150);
        analyze.Bind(Button.CommandProperty, new Binding(nameof(GameDoctorViewModel.AnalyzeCommand)) { Source = viewModel });
        Grid.SetColumn(analyze, 2);
        pathRow.Children.Add(analyze);
        Grid.SetRow(pathRow, 3);
        page.Children.Add(pathRow);

        var results = new StackPanel { Spacing = 10 };
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = StalkerTheme.BrushTextSecondary,
            Margin = new Thickness(0, 2, 0, 4),
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(GameDoctorViewModel.Status)) { Source = viewModel });
        results.Children.Add(status);

        var checks = new ItemsControl
        {
            ItemsSource = viewModel.Checks,
            ItemTemplate = new FuncDataTemplate<GameDoctorCheckRow>((row, _) => CreateCheckRow(row), true),
        };
        results.Children.Add(checks);

        var disableMods = StalkerTheme.StalkerButton(L.T("ВРЕМЕННО ОТКЛЮЧИТЬ КАСТОМНЫЕ МОДЫ"), isPrimary: false, minWidth: 250);
        disableMods.Bind(Button.CommandProperty, new Binding(nameof(GameDoctorViewModel.DisableS2ModsCommand)) { Source = viewModel });
        disableMods.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.ShowDisableS2ModsButton)) { Source = viewModel });
        results.Children.Add(disableMods);

        var restoreMods = StalkerTheme.StalkerButton(L.T("ВОССТАНОВИТЬ КАСТОМНЫЕ МОДЫ"), isPrimary: false, minWidth: 250);
        restoreMods.Bind(Button.CommandProperty, new Binding(nameof(GameDoctorViewModel.RestoreS2ModsCommand)) { Source = viewModel });
        restoreMods.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.ShowRestoreS2ModsButton)) { Source = viewModel });
        results.Children.Add(restoreMods);

        results.Children.Add(new TextBlock
        {
            Text = L.T("МОДИФИКАЦИИ"),
            FontFamily = StalkerTheme.HeadingFont,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = StalkerTheme.BrushAccentAmber,
            Margin = new Thickness(0, 8, 0, 0),
        });
        results.Children.Add(new ItemsControl
        {
            ItemsSource = viewModel.FileAudit,
            ItemTemplate = new FuncDataTemplate<GameDoctorFileAuditRow>((file, _) => CreateFileAuditRow(file), true),
        });

        var resultBorder = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = results,
            },
        };
        resultBorder.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.HasReport)) { Source = viewModel });
        Grid.SetRow(resultBorder, 4);
        page.Children.Add(resultBorder);

        var empty = new TextBlock
        {
            Text = L.T("ВЫБЕРИТЕ ИГРУ И ПАПКУ УСТАНОВКИ ДЛЯ ПРОВЕРКИ."),
            Foreground = StalkerTheme.BrushTextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        empty.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameDoctorViewModel.ShowEmptyState)) { Source = viewModel });
        Grid.SetRow(empty, 4);
        page.Children.Add(empty);
        return page;
    }

    private static Control CreateCheckRow(GameDoctorCheckRow? row)
    {
        if (row is null) return new Border();
        var color = row.Status switch
        {
            GameDoctorStatus.Ok => StalkerTheme.BrushSuccess,
            GameDoctorStatus.Warning => StalkerTheme.BrushAccentAmber,
            GameDoctorStatus.Error => StalkerTheme.BrushDanger,
            _ => StalkerTheme.BrushTextSecondary,
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        grid.Children.Add(new TextBlock
        {
            Text = row.Mark,
            Foreground = color,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Top,
        });
        var details = new StackPanel { Spacing = 3 };
        var name = new TextBlock
        {
            Text = row.Name,
            Foreground = StalkerTheme.BrushTextPrimary,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        details.Children.Add(name);
        var detail = new TextBlock
        {
            Text = row.Detail,
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        details.Children.Add(detail);
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        return new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 8),
            Child = grid,
        };
    }

    private static Control CreateFileAuditRow(GameDoctorFileAuditRow? row)
    {
        if (row is null) return new Border();
        var color = row.Status switch
        {
            GameDoctorStatus.Ok => StalkerTheme.BrushSuccess,
            GameDoctorStatus.Warning => StalkerTheme.BrushAccentAmber,
            GameDoctorStatus.Error => StalkerTheme.BrushDanger,
            _ => StalkerTheme.BrushTextSecondary,
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        grid.Children.Add(new TextBlock
        {
            Text = row.Mark,
            Foreground = color,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Top,
        });
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = row.RelativePath,
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        details.Children.Add(new TextBlock
        {
            Text = row.Owner + " · " + row.Detail,
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        return new Border
        {
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 5),
            Child = grid,
        };
    }
}
