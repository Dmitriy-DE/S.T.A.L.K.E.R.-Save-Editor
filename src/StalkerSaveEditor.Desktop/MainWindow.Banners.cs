using Avalonia;
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
        open.Click += (_, _) => vm.SelectedTab = AppTabs.Settings;
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
        openBtn.Click += (_, _) => vm.SelectedTab = AppTabs.Updates;
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
}
