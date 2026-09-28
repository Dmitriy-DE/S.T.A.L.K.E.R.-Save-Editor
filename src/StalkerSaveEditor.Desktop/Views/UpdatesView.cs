using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class UpdatesView : UserControl
{
    public UpdatesView()
    {
        Content = BuildContent();
    }

    private static Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(24),
            MaxWidth = 800,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // 1. Header
        var header = new TextBlock
        {
            Text = "ОБНОВЛЕНИЕ ПРИЛОЖЕНИЯ",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
            Margin = new Thickness(0, 0, 0, 16),
        };
        root.Children.Add(header);

        // 2. Info Card
        var infoCard = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var infoStack = new StackPanel { Spacing = 12 };

        // Version Row
        var versionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,Auto,16,Auto") };
        var curVerStack = new StackPanel { Spacing = 2 };
        curVerStack.Children.Add(new TextBlock { Text = "ТЕКУЩАЯ ВЕРСИЯ", FontSize = 10, Foreground = StalkerTheme.BrushTextMuted });
        var curVerText = new TextBlock { FontSize = 16, FontWeight = FontWeight.Bold, Foreground = StalkerTheme.BrushTextPrimary };
        curVerText.Bind(TextBlock.TextProperty, new Binding(nameof(UpdatesViewModel.CurrentVersion)));
        curVerStack.Children.Add(curVerText);
        versionRow.Children.Add(curVerStack);

        var arrow = new TextBlock { Text = "➔", FontSize = 18, Foreground = StalkerTheme.BrushAccentAmber, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1);
        versionRow.Children.Add(arrow);

        var latVerStack = new StackPanel { Spacing = 2 };
        latVerStack.Children.Add(new TextBlock { Text = "ПОСЛЕДНЯЯ ВЕРСИЯ", FontSize = 10, Foreground = StalkerTheme.BrushTextMuted });
        var latVerText = new TextBlock { FontSize = 16, FontWeight = FontWeight.Bold, Foreground = StalkerTheme.BrushAccentAmber };
        latVerText.Bind(TextBlock.TextProperty, new Binding(nameof(UpdatesViewModel.LatestVersion)));
        latVerStack.Children.Add(latVerText);
        Grid.SetColumn(latVerStack, 2);
        versionRow.Children.Add(latVerStack);

        // Status Badge
        var statusBadge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusBadge.Bind(Border.BackgroundProperty, new Binding(nameof(UpdatesViewModel.StateBadgeColor))
        {
            Converter = new ColorToBrushConverter(),
        });
        var statusBadgeText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
        };
        statusBadgeText.Bind(TextBlock.TextProperty, new Binding(nameof(UpdatesViewModel.StateBadgeText)));
        statusBadge.Child = statusBadgeText;
        Grid.SetColumn(statusBadge, 4);
        versionRow.Children.Add(statusBadge);

        infoStack.Children.Add(versionRow);
        infoCard.Child = infoStack;
        Grid.SetRow(infoCard, 1);
        root.Children.Add(infoCard);

        // 3. Action Buttons Row
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var checkBtn = StalkerTheme.StalkerButton("ПРОВЕРИТЬ ОБНОВЛЕНИЯ");
        checkBtn.Bind(Button.CommandProperty, new Binding(nameof(UpdatesViewModel.CheckUpdatesCommand)));
        btnRow.Children.Add(checkBtn);

        var downloadBtn = StalkerTheme.StalkerButton("СКАЧАТЬ ОБНОВЛЕНИЕ", isPrimary: true);
        downloadBtn.Bind(Button.CommandProperty, new Binding(nameof(UpdatesViewModel.DownloadCommand)));
        btnRow.Children.Add(downloadBtn);

        var installBtn = StalkerTheme.StalkerButton("УСТАНОВИТЬ ОБНОВЛЕНИЕ", isPrimary: true);
        installBtn.Bind(Button.CommandProperty, new Binding(nameof(UpdatesViewModel.InstallCommand)));
        btnRow.Children.Add(installBtn);

        Grid.SetRow(btnRow, 2);
        root.Children.Add(btnRow);

        // 4. Progress Card
        var progressCard = new Border
        {
            Background = StalkerTheme.BrushBgInput,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 16),
        };
        progressCard.Bind(Visual.IsVisibleProperty, new Binding(nameof(UpdatesViewModel.IsBusy)));

        var progStack = new StackPanel { Spacing = 8 };
        var stageText = new TextBlock { FontSize = 12, Foreground = StalkerTheme.BrushTextPrimary };
        stageText.Bind(TextBlock.TextProperty, new Binding(nameof(UpdatesViewModel.ProgressStageText)));
        progStack.Children.Add(stageText);

        var pBar = new ProgressBar
        {
            Height = 10,
            Minimum = 0,
            Maximum = 100,
            Foreground = StalkerTheme.BrushAccentAmber,
            Background = StalkerTheme.BrushBgPanel,
        };
        pBar.Bind(ProgressBar.ValueProperty, new Binding(nameof(UpdatesViewModel.ProgressPercentage)));
        progStack.Children.Add(pBar);

        progressCard.Child = progStack;
        Grid.SetRow(progressCard, 3);
        root.Children.Add(progressCard);

        // 5. Error Card
        var errorCard = new Border
        {
            Background = Brush.Parse("#381A14"),
            BorderBrush = Brush.Parse("#8E3A2E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16),
        };
        errorCard.Bind(Visual.IsVisibleProperty, new Binding(nameof(UpdatesViewModel.HasError)));

        var errorText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush.Parse("#FF7A6E"),
            TextWrapping = TextWrapping.Wrap,
        };
        errorText.Bind(TextBlock.TextProperty, new Binding(nameof(UpdatesViewModel.ErrorMessage)));
        errorCard.Child = errorText;
        Grid.SetRow(errorCard, 4);
        root.Children.Add(errorCard);

        // 6. Status Message
        var statusMsg = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
            Margin = new Thickness(0, 8, 0, 0),
        };
        statusMsg.Bind(TextBlock.TextProperty, new Binding(nameof(UpdatesViewModel.StatusMessage)));
        Grid.SetRow(statusMsg, 5);
        root.Children.Add(statusMsg);

        return root;
    }

    private sealed class ColorToBrushConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string hex)
            {
                return Brush.Parse(hex);
            }
            return Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
