using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public static class SettingsView
{
    public static Control Build(SettingsViewModel settings)
    {
        var scroll = new ScrollViewer { Padding = new Thickness(16) };
        var stack = new StackPanel { Spacing = 16 };

        // 1. Save Directories Card
        var dirsStack = new StackPanel { Spacing = 8 };
        dirsStack.Children.Add(new TextBlock
        {
            Text = "Папки автоматического поиска сохранений:",
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
                row.Children.Add(new TextBlock
                {
                    Text = dir,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                });

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
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        var addInput = new TextBox
        {
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            Watermark = "Путь к папке с сейвами…",
        };
        addInput.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.NewSaveDirectory))
        {
            Source = settings,
            Mode = BindingMode.TwoWay,
        });
        addRow.Children.Add(addInput);

        var addBtn = StalkerTheme.StalkerButton("Добавить папку", isPrimary: false, minWidth: 120);
        addBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.AddSaveDirectoryCommand)) { Source = settings });
        addBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(addBtn);
        dirsStack.Children.Add(addRow);

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

        // 3. Language & Audio Card
        var prefStack = new StackPanel { Spacing = 10 };

        // Language
        var langRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,200") };
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
            Width = 200,
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

        scroll.Content = stack;
        return scroll;
    }
}
