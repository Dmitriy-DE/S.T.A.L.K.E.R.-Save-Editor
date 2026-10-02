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
    private static Control BuildFirstRunWizard(SaveLibraryViewModel vm)
    {
        if (HostPlatform.IsBrowser)
        {
            return new Border
            {
                Background = StalkerTheme.BrushBgPanel,
                BorderBrush = StalkerTheme.BrushAccentAmber,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(24),
                MaxWidth = 680,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = L.T("Откройте файл сохранения кнопкой «Открыть…» вверху.\n") +
                           L.T("Файл не покидает браузер: он разбирается и изменяется здесь, а после «Сохранить» скачивается обратно."),
                    Foreground = StalkerTheme.BrushTextSecondary,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        var card = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushAccentAmber,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(24),
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var stack = new StackPanel { Spacing = 14 };

        stack.Children.Add(new TextBlock
        {
            Text = L.T("МАСТЕР ПЕРВОГО ЗАПУСКА"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
        });

        stack.Children.Add(new TextBlock
        {
            Text = L.T("Сохранения S.T.A.L.K.E.R. не были найдены в стандартных каталогах.\nУкажите папку с файлами сохранений (savedgames или SaveGames) или запустите автоматический поиск на диске."),
            Foreground = StalkerTheme.BrushTextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        // Auto-detect Button
        var autoDetectBtn = StalkerTheme.StalkerButton(L.T("АВТОПОИСК ПАПОК НА ДИСКЕ"), isPrimary: true, minWidth: 220);
        autoDetectBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.WizardAutoDetectCommand)));
        stack.Children.Add(autoDetectBtn);

        var orDivider = new TextBlock
        {
            Text = L.T("— ИЛИ УКАЖИТЕ ПУТЬ ВРУЧНУЮ —"),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
        };
        stack.Children.Add(orDivider);

        // Manual Input Row
        var inputRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
        };

        var input = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = L.T("Путь к папке с сейвами…"),
        };
        input.Bind(TextBox.TextProperty, new Binding(nameof(SaveLibraryViewModel.WizardDirectoryInput))
        {
            Mode = BindingMode.TwoWay,
        });
        inputRow.Children.Add(input);

        var browseBtn = StalkerTheme.StalkerButton(L.T("Обзор…"), isPrimary: false, minWidth: 80);
        browseBtn.Margin = new Thickness(8, 0, 0, 0);
        browseBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(browseBtn);
            if (topLevel?.StorageProvider is { } storageProvider && storageProvider.CanPickFolder)
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = L.T("Выберите папку с сохранениями"),
                    AllowMultiple = false,
                });
                if (folders.Count > 0 && folders[0].Path.LocalPath is { } path)
                {
                    vm.WizardDirectoryInput = path;
                }
            }
        };
        Grid.SetColumn(browseBtn, 1);
        inputRow.Children.Add(browseBtn);

        var addBtn = StalkerTheme.StalkerButton(L.T("Добавить"), isPrimary: false, minWidth: 90);
        addBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.WizardAddDirectoryCommand)));
        addBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(addBtn, 2);
        inputRow.Children.Add(addBtn);

        stack.Children.Add(inputRow);

        // Bottom Actions
        var bottomActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var settingsBtn = StalkerTheme.StalkerButton(L.T("Перейти в настройки"), isPrimary: false, minWidth: 140);
        settingsBtn.Click += (_, _) => vm.SelectedTab = AppTabs.Settings;
        bottomActions.Children.Add(settingsBtn);

        var dismissBtn = StalkerTheme.StalkerButton(L.T("Пропустить"), isPrimary: false, minWidth: 100);
        dismissBtn.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.DismissWizardCommand)));
        bottomActions.Children.Add(dismissBtn);

        stack.Children.Add(bottomActions);

        card.Child = stack;
        return card;
    }
}
