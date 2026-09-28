using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class SettingsView
{
    public static Control Build(SettingsViewModel settings, DiagnosticsViewModel? diagnostics = null)
    {
        var scroll = new ScrollViewer { Padding = new Thickness(16) };
        var stack = new StackPanel { Spacing = 16 };

        // 1. Save Directories Card
        var dirsStack = new StackPanel { Spacing = 8 };
        dirsStack.Children.Add(new TextBlock
        {
            Text = "Папки автоматического поиска сохранений (ТЧ, ЧН, ЗП, S2):",
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
        });

        var dirsList = new ItemsControl
        {
            ItemsSource = settings.SaveDirectories,
            ItemTemplate = new FuncDataTemplate<string>((dir, _) =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Thickness(0, 2),
                };
                var label = new TextBlock
                {
                    Text = ShortPath(dir),
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.PrefixCharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                ToolTip.SetTip(label, dir);
                row.Children.Add(label);

                var removeBtn = new Button
                {
                    Content = "Удалить",
                    FontSize = 10,
                    Padding = new Thickness(6, 2),
                    Background = StalkerTheme.BrushBgElevated,
                    Foreground = StalkerTheme.BrushDanger,
                    BorderBrush = StalkerTheme.BrushBorder,
                };
                removeBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.RemoveSaveDirectoryCommand)) { Source = settings });
                removeBtn.CommandParameter = dir;
                Grid.SetColumn(removeBtn, 1);
                row.Children.Add(removeBtn);

                return row;
            }),
        };
        dirsStack.Children.Add(dirsList);

        var addRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        var addInput = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = "Путь к папке с сейвами (savedgames или SaveGames)…",
        };
        addInput.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.NewSaveDirectory))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        addRow.Children.Add(addInput);

        var browseBtn = StalkerTheme.StalkerButton("Обзор…", isPrimary: false, minWidth: 80);
        browseBtn.Margin = new Thickness(8, 0, 0, 0);
        browseBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(browseBtn);
            if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanPickFolder)
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Выберите папку с сохранениями S.T.A.L.K.E.R.",
                    AllowMultiple = false,
                });
                if (folders.Count > 0 && folders[0].Path.LocalPath is { } path)
                {
                    settings.NewSaveDirectory = path;
                }
            }
        };
        Grid.SetColumn(browseBtn, 1);
        addRow.Children.Add(browseBtn);

        var addBtn = StalkerTheme.StalkerButton("Добавить папку", isPrimary: false, minWidth: 120);
        addBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.AddSaveDirectoryCommand)) { Source = settings });
        addBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(addBtn, 2);
        addRow.Children.Add(addBtn);
        dirsStack.Children.Add(addRow);

        var autoDetectBtn = StalkerTheme.StalkerButton("Автопоиск папок на диске", isPrimary: false, minWidth: 200);
        autoDetectBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.AutoDetectSaveDirectoriesCommand)) { Source = settings });
        autoDetectBtn.Margin = new Thickness(0, 6, 0, 0);
        dirsStack.Children.Add(autoDetectBtn);

        stack.Children.Add(StalkerTheme.Card(dirsStack, "Каталоги сохранений"));

        // 2. Backup Directory Card
        var backupStack = new StackPanel { Spacing = 6 };
        backupStack.Children.Add(new TextBlock
        {
            Text = "Папка для создания резервных копий и журналов восстановления:",
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
        });
        var backupInput = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
        };
        backupInput.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.BackupDirectory))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        backupStack.Children.Add(backupInput);
        stack.Children.Add(StalkerTheme.Card(backupStack, "Резервное копирование"));

        // 3. Interface & Audio Card
        var prefStack = new StackPanel { Spacing = 10 };

        // Language
        var langRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,220") };
        langRow.Children.Add(new TextBlock
        {
            Text = "Язык интерфейса:",
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var langCombo = new ComboBox
        {
            ItemsSource = settings.Languages,
            Width = 220,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
        };
        langCombo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(SettingsViewModel.SelectedLanguage))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        Grid.SetColumn(langCombo, 1);
        langRow.Children.Add(langCombo);
        prefStack.Children.Add(langRow);

        // Theme
        var themeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,220") };
        themeRow.Children.Add(new TextBlock
        {
            Text = "Тема оформления:",
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var themeCombo = new ComboBox
        {
            ItemsSource = settings.Themes,
            Width = 220,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
        };
        themeCombo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(SettingsViewModel.SelectedTheme))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        Grid.SetColumn(themeCombo, 1);
        themeRow.Children.Add(themeCombo);
        prefStack.Children.Add(themeRow);

        // Audio
        var soundChk = new CheckBox
        {
            Content = "Включить звуковые эффекты меню игры",
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
        };
        soundChk.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(SettingsViewModel.SoundEnabled))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        prefStack.Children.Add(soundChk);

        var volRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,200,50") };
        volRow.Children.Add(new TextBlock
        {
            Text = "Громкость звуков:",
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var volSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 200,
        };
        volSlider.Bind(Slider.ValueProperty, new Binding(nameof(SettingsViewModel.SoundVolume))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        Grid.SetColumn(volSlider, 1);
        volRow.Children.Add(volSlider);

        var volText = new TextBlock
        {
            Foreground = StalkerTheme.BrushAccentAmber,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        volText.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.SoundVolume))
        {
            Source = settings,
            StringFormat = "{0}%",
        });
        Grid.SetColumn(volText, 2);
        volRow.Children.Add(volText);
        prefStack.Children.Add(volRow);

        stack.Children.Add(StalkerTheme.Card(prefStack, "Интерфейс и звуки"));

        // Save Button & Status
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 10, 0, 0) };
        var saveSettingsBtn = StalkerTheme.StalkerButton("Сохранить настройки", isPrimary: true, minWidth: 160);
        saveSettingsBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.SaveSettingsCommand)) { Source = settings });
        saveRow.Children.Add(saveSettingsBtn);

        var statusText = new TextBlock
        {
            Foreground = StalkerTheme.BrushSuccess,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.SettingsStatus)) { Source = settings });
        saveRow.Children.Add(statusText);

        stack.Children.Add(saveRow);

        if (diagnostics is not null) stack.Children.Add(BuildDiagnostics(diagnostics));

        scroll.Content = stack;
        return scroll;
    }

    private static string ShortPath(string? path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path is not null && home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path ?? string.Empty;
    }

    private static Control BuildDiagnostics(DiagnosticsViewModel diagnostics)
    {
        var panel = new StackPanel { Spacing = 10, DataContext = diagnostics };

        var crash = new StackPanel { Spacing = 6 };
        crash.Bind(Visual.IsVisibleProperty, new Binding(nameof(DiagnosticsViewModel.HasPendingCrash)));
        crash.Children.Add(new TextBlock
        {
            Text = "Прошлый запуск завершился ошибкой. Сохраните отчёт и приложите его к issue.",
            Foreground = StalkerTheme.BrushDanger,
            TextWrapping = TextWrapping.Wrap,
        });
        var crashText = new TextBox { IsReadOnly = true, MaxHeight = 140, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        crashText.Bind(TextBox.TextProperty, new Binding(nameof(DiagnosticsViewModel.PendingCrash)));
        crash.Children.Add(crashText);
        panel.Children.Add(crash);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var check = StalkerTheme.StalkerButton("Проверить окружение", isPrimary: true, minWidth: 170);
        check.Bind(Button.CommandProperty, new Binding(nameof(DiagnosticsViewModel.RunChecksCommand)));
        buttons.Children.Add(check);

        var export = StalkerTheme.StalkerButton("Сохранить отчёт…", isPrimary: false, minWidth: 150);
        export.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(export);
            if (topLevel?.StorageProvider is not { } storage) return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Отчёт для поддержки",
                SuggestedFileName = "save-editor-report.txt.gz",
            });
            if (file?.TryGetLocalPath() is { } path) diagnostics.ExportBundle(path);
        };
        buttons.Children.Add(export);

        var dismiss = StalkerTheme.StalkerButton("Скрыть ошибку", isPrimary: false, minWidth: 130);
        dismiss.Bind(Button.CommandProperty, new Binding(nameof(DiagnosticsViewModel.DismissCrashCommand)));
        dismiss.Bind(Visual.IsVisibleProperty, new Binding(nameof(DiagnosticsViewModel.HasPendingCrash)));
        buttons.Children.Add(dismiss);
        panel.Children.Add(buttons);

        var status = new TextBlock { Foreground = StalkerTheme.BrushTextSecondary, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(DiagnosticsViewModel.Status)));
        panel.Children.Add(status);

        panel.Children.Add(new ItemsControl
        {
            ItemsSource = diagnostics.Checks,
            ItemTemplate = new FuncDataTemplate<EnvironmentCheckRow>((row, _) =>
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), Margin = new Thickness(0, 2) };
                if (row is null) return line;
                line.Children.Add(new TextBlock
                {
                    Text = row.Status,
                    FontWeight = FontWeight.Bold,
                    Foreground = row.Status == "OK" ? StalkerTheme.BrushSuccess : row.Status == "!" ? StalkerTheme.BrushWarning : StalkerTheme.BrushDanger,
                });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = row.Title + ": " + row.Detail, Foreground = StalkerTheme.BrushTextPrimary, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                if (row.HasHint) text.Children.Add(new TextBlock { Text = row.Hint, Foreground = StalkerTheme.BrushTextMuted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(text, 1);
                line.Children.Add(text);
                return line;
            }),
        });

        return StalkerTheme.Card(panel, "Диагностика");
    }
}
