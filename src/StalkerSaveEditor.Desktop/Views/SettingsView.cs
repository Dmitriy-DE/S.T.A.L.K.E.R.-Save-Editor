using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class SettingsView
{
    private static readonly FuncValueConverter<string?, IBrush?> EnvironmentStatusBrush =
        new(status => status switch
        {
            "OK" => StalkerTheme.BrushSuccess,
            "!" => StalkerTheme.BrushWarning,
            _ => StalkerTheme.BrushDanger,
        });

    private sealed record SettingsCategory(string Id, string Title, string Description, Func<Control> BuildContent);

    public static Control Build(
        SettingsViewModel settings,
        DiagnosticsViewModel? diagnostics = null,
        Action<string>? navigateTo = null,
        string? initialCategoryId = null)
    {
        var categories = new[]
        {
            new SettingsCategory("general", L.T("ОБЩИЕ"), L.T("УПРАВЛЕНИЕ ОСНОВНЫМ ПОВЕДЕНИЕМ РЕДАКТОРА"),
                () => BuildGeneral(navigateTo)),
            new SettingsCategory("interface", L.T("ИНТЕРФЕЙС"), L.T("ЯЗЫК, ЗВУК И АНИМАЦИИ"),
                () => BuildInterface(settings)),
            new SettingsCategory("sound", L.T("Звук"), L.T("Звуки интерфейса"),
                () => BuildSound(settings)),
            new SettingsCategory("games-paths", L.T("ПУТИ И АВТОПОИСК"), string.Empty,
                () => BuildSaveDirectories(settings)),
            new SettingsCategory("updates", L.T("ОБНОВЛЕНИЯ"), string.Empty,
                () => BuildUpdates(navigateTo)),
            new SettingsCategory("data-backups", L.T("РЕЗЕРВНЫЕ КОПИИ"), L.T("ИСТОРИЯ РЕЗЕРВНЫХ КОПИЙ"),
                () => BuildBackupDirectory(settings)),
            new SettingsCategory("diagnostics", L.T("ИНСТРУМЕНТЫ ДЛЯ ПОДДЕРЖКИ"), L.T("Диагностика не нужна для обычного использования, но полезна для отчётов об ошибках."),
                () => diagnostics is null ? BuildDiagnosticsUnavailable() : BuildDiagnostics(diagnostics)),
            new SettingsCategory("reports-privacy", L.T("ОТЧЁТЫ И ПРИВАТНОСТЬ"), L.T("Отправить обезличенные журналы и отчёт окружения. Сохранения не отправляются."),
                () => BuildReports(settings)),
            new SettingsCategory("about", L.T("ВЕРСИЯ"), L.T("Редактор сохранений для всей серии S.T.A.L.K.E.R."),
                () => BuildAbout(navigateTo)),
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = StalkerTheme.BrushBgBase,
        };
        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("210,*"),
            Margin = new Thickness(16, 16, 16, 10),
        };

        var navigation = new Dictionary<string, Button>(StringComparer.Ordinal);
        var panels = new Dictionary<string, Border>(StringComparer.Ordinal);
        var navigationStack = new StackPanel { Spacing = 7, Margin = new Thickness(0, 0, 12, 0) };
        navigationStack.Children.Add(new TextBlock
        {
            Text = L.T("Разделы"),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(8, 0, 0, 5),
        });

        foreach (var category in categories)
        {
            var button = StalkerTheme.StalkerButton(category.Title, isPrimary: false, minWidth: 0);
            button.Content = new TextBlock
            {
                Text = category.Title,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.Name = "settings-nav-" + category.Id;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(12, 10);
            button.Click += (_, _) => SelectCategory(category.Id);
            navigation.Add(category.Id, button);
            navigationStack.Children.Add(button);
        }

        var navBorder = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = navigationStack,
        };
        contentGrid.Children.Add(navBorder);

        var contentHost = new Grid();
        foreach (var category in categories)
        {
            var panel = SettingsPage(category.Id, category.Title, category.Description, category.BuildContent());
            panel.IsVisible = category.Id == "general";
            panels.Add(category.Id, panel);
            contentHost.Children.Add(panel);
        }

        var scroll = new ScrollViewer
        {
            Content = contentHost,
            Padding = new Thickness(0, 0, 2, 0),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Grid.SetColumn(scroll, 1);
        contentGrid.Children.Add(scroll);
        root.Children.Add(contentGrid);

        var footer = BuildFooter(settings);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        SelectCategory(panels.ContainsKey(initialCategoryId ?? string.Empty) ? initialCategoryId! : "general");
        return root;

        void SelectCategory(string id)
        {
            foreach (var (categoryId, panel) in panels)
                panel.IsVisible = categoryId == id;
            foreach (var (categoryId, button) in navigation)
            {
                var selected = categoryId == id;
                button.Background = selected ? StalkerTheme.BrushBgElevated : StalkerTheme.BrushBgPanel;
                button.BorderBrush = selected ? StalkerTheme.BrushAccentAmber : StalkerTheme.BrushBorder;
                button.Foreground = selected ? StalkerTheme.BrushTextPrimary : StalkerTheme.BrushTextSecondary;
            }
        }
    }

    private static Border SettingsPage(string id, string title, string description, Control content)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
        });
        if (!string.IsNullOrWhiteSpace(description))
            stack.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = StalkerTheme.BrushTextSecondary,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, -8, 0, 2),
            });
        stack.Children.Add(content);
        return new Border
        {
            Name = "settings-content-" + id,
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(18),
            Child = stack,
        };
    }

    private static Control BuildGeneral(Action<string>? navigateTo)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(UnsupportedOption(
            "settings-unsupported-experimental-warnings",
            "Показывать технические предупреждения для experimental-функций",
            "Отдельные предупреждения для experimental-функций пока не поддерживаются."));
        stack.Children.Add(UnsupportedOption(
            "settings-unsupported-save-confirmation",
            "Подтверждать запись перед сохранением",
            "Подтверждение перед записью не настраивается: изменения проходят через проверяемый черновик."));
        stack.Children.Add(UnsupportedOption(
            "settings-unsupported-open-last-source",
            "Открывать последний источник при запуске",
            "Приложение не сохраняет и не открывает последний путь к источнику автоматически."));
        stack.Children.Add(StalkerTheme.Card(BuildCloudLink(navigateTo), L.T("Steam Cloud")));
        return stack;
    }

    private static Control BuildInterface(SettingsViewModel settings)
    {
        var stack = new StackPanel { Spacing = 12 };
        var language = new ComboBox
        {
            ItemsSource = settings.Languages,
            Width = 260,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
        };
        language.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(SettingsViewModel.SelectedLanguage))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        stack.Children.Add(BuildPreferenceRow(L.T("Язык интерфейса:"), language));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Язык применится после перезапуска приложения."),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            Margin = new Thickness(0, -7, 0, 1),
        });

        var theme = AppearanceCombo(settings.Themes, nameof(AppearanceOption.Id),
            nameof(SettingsViewModel.ThemeId), settings, width: 260);
        stack.Children.Add(BuildPreferenceRow(L.T("Тема оформления:"), theme));

        var accent = AppearanceCombo(settings.Accents, nameof(AppearanceOption.Id),
            nameof(SettingsViewModel.AccentId), settings, width: 260);
        stack.Children.Add(BuildPreferenceRow(L.T("Акцентный цвет:"), accent));

        var scale = new ComboBox
        {
            ItemsSource = settings.UiScales,
            SelectedValueBinding = new Binding(nameof(UiScaleOption.Percent)),
            Width = 260,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            ItemTemplate = StalkerTheme.Template<UiScaleOption>(option => new TextBlock { Text = option.Name }),
        };
        scale.Bind(ComboBox.SelectedValueProperty, new Binding(nameof(SettingsViewModel.UiScalePercent))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        stack.Children.Add(BuildPreferenceRow(L.T("Масштаб интерфейса:"), scale));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Тема, акцент и масштаб применяются сразу и сохраняются автоматически."),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });
        return StalkerTheme.Card(stack, L.T("ИНТЕРФЕЙС"));
    }

    private static ComboBox AppearanceCombo(
        IReadOnlyList<AppearanceOption> options,
        string valueMember,
        string settingProperty,
        SettingsViewModel settings,
        double width)
    {
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedValueBinding = new Binding(valueMember),
            Width = width,
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            ItemTemplate = StalkerTheme.Template<AppearanceOption>(option => new TextBlock { Text = option.Name }),
        };
        combo.Bind(ComboBox.SelectedValueProperty, new Binding(settingProperty)
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        return combo;
    }

    private static Control BuildSound(SettingsViewModel settings)
    {
        var stack = new StackPanel { Spacing = 12 };
        var sound = new CheckBox
        {
            Content = L.T("Включить звуковые эффекты меню игры"),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
        };
        sound.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(SettingsViewModel.SoundEnabled))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        stack.Children.Add(sound);

        var music = new CheckBox
        {
            Content = L.T("Музыка главного меню игры открытого сейва"),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
        };
        music.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(SettingsViewModel.MusicEnabled))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        stack.Children.Add(music);

        var volumeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,200,52") };
        volumeRow.Children.Add(new TextBlock
        {
            Text = L.T("Громкость звуков:"),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var slider = new Slider { Minimum = 0, Maximum = 100, Width = 200 };
        slider.Bind(Slider.ValueProperty, new Binding(nameof(SettingsViewModel.SoundVolume))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        Grid.SetColumn(slider, 1);
        volumeRow.Children.Add(slider);

        var volumeText = new TextBlock
        {
            Foreground = StalkerTheme.BrushAccentAmber,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        volumeText.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.SoundVolume))
        {
            Source = settings,
            StringFormat = "{0}%",
        });
        Grid.SetColumn(volumeText, 2);
        volumeRow.Children.Add(volumeText);
        stack.Children.Add(volumeRow);
        return StalkerTheme.Card(stack, L.T("Звук"));
    }

    private static Control BuildSaveDirectories(SettingsViewModel settings)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Папки автоматического поиска сохранений (ТЧ, ЧН, ЗП, S2):"),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        var directoryList = new ItemsControl
        {
            Name = "settings-directory-list",
            ItemsSource = settings.SaveDirectories,
            ItemTemplate = StalkerTheme.RecyclingTemplate<string>(() =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Thickness(0, 2),
                };
                var label = new TextBlock
                {
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                label.Bind(TextBlock.TextProperty, new Binding("."));
                label.Bind(ToolTip.TipProperty, new Binding("."));
                row.Children.Add(label);

                var remove = new Button
                {
                    Content = L.T("Удалить"),
                    FontSize = 10,
                    Padding = new Thickness(6, 2),
                    Background = StalkerTheme.BrushBgElevated,
                    Foreground = StalkerTheme.BrushDanger,
                    BorderBrush = StalkerTheme.BrushBorder,
                };
                remove.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.RemoveSaveDirectoryCommand)) { Source = settings });
                remove.Bind(Button.CommandParameterProperty, new Binding("."));
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                return row;
            }),
        };
        stack.Children.Add(directoryList);

        var addRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        var input = new TextBox
        {
            Name = "settings-save-directory-input",
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = L.T("Путь к папке с сейвами (savedgames или SaveGames)…"),
        };
        input.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.NewSaveDirectory))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        addRow.Children.Add(input);

        var browse = StalkerTheme.StalkerButton(L.T("Обзор…"), isPrimary: false, minWidth: 80);
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(browse);
            if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanPickFolder)
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = L.T("Выберите папку с сохранениями S.T.A.L.K.E.R."),
                    AllowMultiple = false,
                });
                if (folders.Count > 0 && folders[0].Path.LocalPath is { } path)
                    settings.NewSaveDirectory = path;
            }
        };
        Grid.SetColumn(browse, 1);
        addRow.Children.Add(browse);

        var add = StalkerTheme.StalkerButton(L.T("Добавить папку"), isPrimary: false, minWidth: 120);
        add.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.AddSaveDirectoryCommand)) { Source = settings });
        add.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(add, 2);
        addRow.Children.Add(add);
        stack.Children.Add(addRow);

        var detect = StalkerTheme.StalkerButton(L.T("Автопоиск папок на диске"), isPrimary: false, minWidth: 200);
        detect.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.AutoDetectSaveDirectoriesCommand)) { Source = settings });
        detect.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(detect);

        stack.Children.Add(UnsupportedPath(
            "Steam",
            "Путь к каталогу Steam не используется настройками поиска сохранений."));
        stack.Children.Add(UnsupportedPath(
            "S.T.A.L.K.E.R. 2 / Zone Kit / Workshop",
            "Каталоги Zone Kit и Workshop не поддерживаются этой настройкой."));
        return StalkerTheme.Card(stack, L.T("Каталоги сохранений"));
    }

    private static Control BuildBackupDirectory(SettingsViewModel settings)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Папка для создания резервных копий и журналов восстановления:"),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var input = new TextBox
        {
            Name = "settings-backup-directory",
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
        };
        input.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.BackupDirectory))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        stack.Children.Add(input);
        return StalkerTheme.Card(stack, L.T("Резервное копирование"));
    }

    private static Control BuildUpdates(Action<string>? navigateTo)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(UnsupportedOption(
            "settings-unsupported-auto-updates",
            "Автоматически проверять обновления",
            "Автоматическая проверка обновлений управляется приложением и не настраивается здесь. Откройте экран обновлений для ручной проверки и установки."));
        var openUpdates = StalkerTheme.StalkerButton(L.T("Открыть обновления приложения"), isPrimary: true, minWidth: 220);
        openUpdates.Name = "settings-open-updates";
        openUpdates.Click += (_, _) => navigateTo?.Invoke("updates");
        stack.Children.Add(openUpdates);

        var openFixes = StalkerTheme.StalkerButton(L.T("Управление исправлениями игры"), isPrimary: false, minWidth: 220);
        openFixes.Name = "settings-open-game-fixes";
        openFixes.Click += (_, _) => navigateTo?.Invoke("game-fixes");
        stack.Children.Add(openFixes);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Исправления для игры устанавливаются или обновляются только после явного действия пользователя."),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        return StalkerTheme.Card(stack, L.T("Ручное управление обновлениями"));
    }

    private static Control BuildDiagnosticsUnavailable() => StalkerTheme.Card(new TextBlock
    {
        Text = L.T("Диагностика недоступна в этом контексте."),
        Foreground = StalkerTheme.BrushTextSecondary,
        TextWrapping = TextWrapping.Wrap,
    }, L.T("Диагностика"));

    private static Control BuildDiagnostics(DiagnosticsViewModel diagnostics)
    {
        var panel = new StackPanel { Spacing = 10, DataContext = diagnostics };

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
            ItemTemplate = StalkerTheme.RecyclingTemplate<EnvironmentCheckRow>(() =>
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), Margin = new Thickness(0, 2) };
                var statusMark = new TextBlock
                {
                    FontWeight = FontWeight.Bold,
                    Foreground = StalkerTheme.BrushTextSecondary,
                };
                statusMark.Bind(TextBlock.TextProperty, new Binding(nameof(EnvironmentCheckRow.Status)));
                statusMark.Bind(TextBlock.ForegroundProperty, new Binding(nameof(EnvironmentCheckRow.Status))
                {
                    Converter = EnvironmentStatusBrush,
                });
                line.Children.Add(statusMark);
                var text = new StackPanel();
                var title = new TextBlock
                {
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                };
                title.Bind(TextBlock.TextProperty, new Binding(nameof(EnvironmentCheckRow.Title)));
                text.Children.Add(title);
                var detail = new TextBlock
                {
                    Foreground = StalkerTheme.BrushTextSecondary,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                };
                detail.Bind(TextBlock.TextProperty, new Binding(nameof(EnvironmentCheckRow.Detail)));
                text.Children.Add(detail);
                var hint = new TextBlock
                {
                    Foreground = StalkerTheme.BrushTextMuted,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                };
                hint.Bind(TextBlock.TextProperty, new Binding(nameof(EnvironmentCheckRow.Hint)));
                hint.Bind(Visual.IsVisibleProperty, new Binding(nameof(EnvironmentCheckRow.HasHint)));
                text.Children.Add(hint);
                Grid.SetColumn(text, 1);
                line.Children.Add(text);
                return line;
            }),
        });
        return StalkerTheme.Card(panel, L.T("Проверка окружения"));
    }

    private static Control BuildReports(SettingsViewModel settings)
    {
        var stack = new StackPanel { Spacing = 10 };
        var reports = new CheckBox
        {
            Content = L.T("Отправлять разработчику журнал раз в сутки и после сбоя (без путей, имён, Steam ID и сейвов)"),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 12,
        };
        reports.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(SettingsViewModel.SendReports))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        stack.Children.Add(reports);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Отправить обезличенные журналы и отчёт окружения. Сохранения не отправляются."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        return StalkerTheme.Card(stack, L.T("Отчёты об ошибках"));
    }

    private static Control BuildAbout(Action<string>? navigateTo)
    {
        var stack = new StackPanel { Spacing = 10 };
        var version = new TextBlock
        {
            Text = L.T("S.T.A.L.K.E.R. Save Editor {0}", StalkerSaveEditor.Core.ApplicationVersion.Current),
            Foreground = StalkerTheme.BrushTextPrimary,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
        };
        stack.Children.Add(version);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Редактор сохранений для всей серии S.T.A.L.K.E.R."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var updates = StalkerTheme.StalkerButton(L.T("Сведения об обновлениях"), isPrimary: false, minWidth: 210);
        updates.Click += (_, _) => navigateTo?.Invoke("updates");
        stack.Children.Add(updates);
        return StalkerTheme.Card(stack, L.T("О программе"));
    }

    private static Control BuildCloudLink(Action<string>? navigateTo)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Откройте экран Steam Cloud, чтобы просматривать состояние синхронизации и выполнять действия с явным подтверждением."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var cloud = StalkerTheme.StalkerButton(L.T("Открыть Steam Cloud"), isPrimary: false, minWidth: 180);
        cloud.Name = "settings-open-cloud";
        cloud.Click += (_, _) => navigateTo?.Invoke("cloud");
        stack.Children.Add(cloud);
        return stack;
    }

    private static Control UnsupportedOption(string name, string title, string reason)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2) };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new TextBlock
        {
            Text = L.T(title),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var check = new CheckBox
        {
            Name = name,
            IsEnabled = false,
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(check, L.T(title));
        AutomationProperties.SetHelpText(check, L.T(reason));
        ToolTip.SetTip(check, L.T(reason));
        Grid.SetColumn(check, 1);
        row.Children.Add(check);
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock
        {
            Name = name + "-reason",
            Text = L.T(reason),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 0, 0),
        });
        return stack;
    }

    private static Control UnsupportedPath(string label, string reason)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
        });
        stack.Children.Add(new TextBlock
        {
            Text = reason,
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        return stack;
    }

    private static Border BuildFooter(SettingsViewModel settings)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = StalkerTheme.StalkerButton(L.T("Сохранить настройки"), isPrimary: true, minWidth: 160);
        save.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.SaveSettingsCommand)) { Source = settings });
        actions.Children.Add(save);

        var reset = UnsupportedFooterButton("Сбросить", "Сброс несохранённых изменений пока не поддерживается.");
        actions.Children.Add(reset);
        var defaults = UnsupportedFooterButton("По умолчанию", "Восстановление настроек по умолчанию пока не поддерживается.");
        actions.Children.Add(defaults);
        var cancel = UnsupportedFooterButton("Отмена", "Отмена изменений на этой странице пока не поддерживается.");
        actions.Children.Add(cancel);

        var status = new TextBlock
        {
            Foreground = StalkerTheme.BrushSuccess,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.SettingsStatus)) { Source = settings });

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 12,
        };
        grid.Children.Add(actions);
        var unavailableReason = new TextBlock
        {
            Text = L.T("Сброс, значения по умолчанию и отмена недоступны."),
            Foreground = StalkerTheme.BrushTextMuted,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(unavailableReason, 1);
        grid.Children.Add(unavailableReason);
        Grid.SetColumn(status, 2);
        grid.Children.Add(status);

        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorder,
            BorderThickness = new Thickness(1, 1, 0, 0),
            Padding = new Thickness(16, 10),
            Child = grid,
        };
    }

    private static Button UnsupportedFooterButton(string title, string reason)
    {
        var button = StalkerTheme.StalkerButton(L.T(title), isPrimary: false, minWidth: 92);
        button.IsEnabled = false;
        ToolTip.SetTip(button, L.T(reason));
        return button;
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
}
