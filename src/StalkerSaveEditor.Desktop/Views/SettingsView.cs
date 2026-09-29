using StalkerSaveEditor.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
            Text = L.T("Папки автоматического поиска сохранений (ТЧ, ЧН, ЗП, S2):"),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
        });

        var dirsList = new ItemsControl
        {
            ItemsSource = settings.SaveDirectories,
            ItemTemplate = StalkerTheme.Template<string>(dir =>
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
                    Content = L.T("Удалить"),
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
            Watermark = L.T("Путь к папке с сейвами (savedgames или SaveGames)…"),
        };
        addInput.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.NewSaveDirectory))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        addRow.Children.Add(addInput);

        var browseBtn = StalkerTheme.StalkerButton(L.T("Обзор…"), isPrimary: false, minWidth: 80);
        browseBtn.Margin = new Thickness(8, 0, 0, 0);
        browseBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(browseBtn);
            if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanPickFolder)
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = L.T("Выберите папку с сохранениями S.T.A.L.K.E.R."),
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

        var addBtn = StalkerTheme.StalkerButton(L.T("Добавить папку"), isPrimary: false, minWidth: 120);
        addBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.AddSaveDirectoryCommand)) { Source = settings });
        addBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(addBtn, 2);
        addRow.Children.Add(addBtn);
        dirsStack.Children.Add(addRow);

        var autoDetectBtn = StalkerTheme.StalkerButton(L.T("Автопоиск папок на диске"), isPrimary: false, minWidth: 200);
        autoDetectBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.AutoDetectSaveDirectoriesCommand)) { Source = settings });
        autoDetectBtn.Margin = new Thickness(0, 6, 0, 0);
        dirsStack.Children.Add(autoDetectBtn);

        stack.Children.Add(StalkerTheme.Card(dirsStack, L.T("Каталоги сохранений")));

        // 2. Backup Directory Card
        var backupStack = new StackPanel { Spacing = 6 };
        backupStack.Children.Add(new TextBlock
        {
            Text = L.T("Папка для создания резервных копий и журналов восстановления:"),
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
        stack.Children.Add(StalkerTheme.Card(backupStack, L.T("Резервное копирование")));

        // 3. Interface & Audio Card
        var prefStack = new StackPanel { Spacing = 10 };

        // Language
        var langRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,220") };
        langRow.Children.Add(new TextBlock
        {
            Text = L.T("Язык интерфейса:"),
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

        // Appearance preferences apply and persist as soon as they change.
        prefStack.Children.Add(new TextBlock
        {
            Text = L.T("Язык применится после перезапуска приложения."),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
        });

        var themeCombo = new ComboBox
        {
            ItemsSource = settings.Themes,
            SelectedValueBinding = new Binding(nameof(AppearanceOption.Id)),
            Width = 220,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            ItemTemplate = StalkerTheme.Template<AppearanceOption>(option => new TextBlock { Text = option.Name }),
        };
        themeCombo.Bind(ComboBox.SelectedValueProperty, new Binding(nameof(SettingsViewModel.ThemeId))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        prefStack.Children.Add(BuildPreferenceRow(L.T("Тема оформления:"), themeCombo));

        var accentCombo = new ComboBox
        {
            ItemsSource = settings.Accents,
            SelectedValueBinding = new Binding(nameof(AppearanceOption.Id)),
            Width = 220,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            ItemTemplate = StalkerTheme.Template<AppearanceOption>(option => new TextBlock { Text = option.Name }),
        };
        accentCombo.Bind(ComboBox.SelectedValueProperty, new Binding(nameof(SettingsViewModel.AccentId))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        prefStack.Children.Add(BuildPreferenceRow(L.T("Акцентный цвет:"), accentCombo));

        var scaleCombo = new ComboBox
        {
            ItemsSource = settings.UiScales,
            SelectedValueBinding = new Binding(nameof(UiScaleOption.Percent)),
            Width = 220,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            ItemTemplate = StalkerTheme.Template<UiScaleOption>(option => new TextBlock { Text = option.Name }),
        };
        scaleCombo.Bind(ComboBox.SelectedValueProperty, new Binding(nameof(SettingsViewModel.UiScalePercent))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        prefStack.Children.Add(BuildPreferenceRow(L.T("Масштаб интерфейса:"), scaleCombo));
        prefStack.Children.Add(new TextBlock
        {
            Text = L.T("Тема, акцент и масштаб применяются сразу и сохраняются автоматически."),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });

        // Audio
        var soundChk = new CheckBox
        {
            Content = L.T("Включить звуковые эффекты меню игры"),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
        };
        soundChk.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(SettingsViewModel.SoundEnabled))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        prefStack.Children.Add(soundChk);

        var musicChk = new CheckBox
        {
            Content = L.T("Музыка главного меню игры открытого сейва"),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
        };
        musicChk.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(SettingsViewModel.MusicEnabled))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        prefStack.Children.Add(musicChk);

        var volRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,200,50") };
        volRow.Children.Add(new TextBlock
        {
            Text = L.T("Громкость звуков:"),
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

        stack.Children.Add(StalkerTheme.Card(prefStack, L.T("Интерфейс и звуки")));

        // Save Button & Status
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 10, 0, 0) };
        var saveSettingsBtn = StalkerTheme.StalkerButton(L.T("Сохранить настройки"), isPrimary: true, minWidth: 160);
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

        if (diagnostics is not null) stack.Children.Add(BuildDiagnostics(diagnostics, settings));

        scroll.Content = stack;
        return scroll;
    }

    private static string ShortPath(string? path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path is not null && home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path ?? string.Empty;
    }

    private static Grid BuildPreferenceRow(string label, Control control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,*"),
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Control BuildDiagnostics(DiagnosticsViewModel diagnostics, SettingsViewModel settings)
    {
        var panel = new StackPanel { Spacing = 10, DataContext = diagnostics };
        var reports = new CheckBox
        {
            Content = L.T("Отправлять разработчику журнал раз в сутки и после сбоя (без путей, имён, Steam ID и сейвов)"),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
        };
        reports.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(SettingsViewModel.SendReports)) { Source = settings, Mode = BindingMode.TwoWay });
        panel.Children.Add(reports);

        var crash = new StackPanel { Spacing = 6 };
        crash.Bind(Visual.IsVisibleProperty, new Binding(nameof(DiagnosticsViewModel.HasPendingCrash)));
        crash.Children.Add(new TextBlock
        {
            Text = L.T("Прошлый запуск завершился ошибкой. Сохраните отчёт и приложите его к issue."),
            Foreground = StalkerTheme.BrushDanger,
            TextWrapping = TextWrapping.Wrap,
        });
        var crashText = new TextBox { IsReadOnly = true, MaxHeight = 140, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        crashText.Bind(TextBox.TextProperty, new Binding(nameof(DiagnosticsViewModel.PendingCrash)));
        crash.Children.Add(crashText);
        panel.Children.Add(crash);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var check = StalkerTheme.StalkerButton(L.T("Проверить окружение"), isPrimary: true, minWidth: 170);
        check.Bind(Button.CommandProperty, new Binding(nameof(DiagnosticsViewModel.RunChecksCommand)));
        buttons.Children.Add(check);

        var export = StalkerTheme.StalkerButton(L.T("Сохранить отчёт…"), isPrimary: false, minWidth: 150);
        export.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(export);
            if (topLevel?.StorageProvider is not { } storage) return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.T("Отчёт для поддержки"),
                SuggestedFileName = "save-editor-report.txt.gz",
            });
            if (file?.TryGetLocalPath() is { } path) diagnostics.ExportBundle(path);
        };
        buttons.Children.Add(export);

        var sendNow = StalkerTheme.StalkerButton(L.T("Отправить отчёт сейчас"), isPrimary: false, minWidth: 170);
        sendNow.Bind(Button.CommandProperty, new Binding(nameof(DiagnosticsViewModel.SendNowCommand)));
        buttons.Children.Add(sendNow);

        var dismiss = StalkerTheme.StalkerButton(L.T("Скрыть ошибку"), isPrimary: false, minWidth: 130);
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
            ItemTemplate = StalkerTheme.Template<EnvironmentCheckRow>(row =>
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

        return StalkerTheme.Card(panel, L.T("Диагностика"));
    }
}
