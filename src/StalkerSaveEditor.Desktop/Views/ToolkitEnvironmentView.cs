using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class ToolkitEnvironmentView : UserControl
{
    public ToolkitEnvironmentView(ToolkitEnvironmentViewModel viewModel)
    {
        DataContext = viewModel;
        Content = Build(viewModel);
    }

    private static Control Build(ToolkitEnvironmentViewModel vm)
    {
        var page = new StackPanel { Spacing = 14 };
        page.Children.Add(new TextBlock
        {
            Text = L.T("СРЕДА ИГРЫ"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
        });
        var installation = new TextBlock { Foreground = StalkerTheme.BrushTextMuted, TextWrapping = TextWrapping.Wrap };
        installation.Bind(TextBlock.TextProperty, new Binding(nameof(ToolkitEnvironmentViewModel.CurrentInstallationDisplay)) { Source = vm });
        page.Children.Add(installation);

        var operationStatus = new TextBlock
        {
            Foreground = StalkerTheme.BrushAccentAmber,
            TextWrapping = TextWrapping.Wrap,
        };
        operationStatus.Bind(TextBlock.TextProperty, new Binding(nameof(ToolkitEnvironmentViewModel.Status)) { Source = vm });
        page.Children.Add(operationStatus);

        page.Children.Add(BuildSnapshots(vm));
        page.Children.Add(BuildProfiles(vm));
        page.Children.Add(BuildUserLtx(vm));
        page.Children.Add(BuildAudit(vm));
        var root = new ScrollViewer { Padding = new Thickness(20), Content = page };
        return root;
    }

    /// <summary>A list with a centred hint while its collection is empty (instead of a blank box).</summary>
    private static Control EmptyOverlay(ListBox list, ToolkitEnvironmentViewModel vm, string collection, string hint)
    {
        var empty = new TextBlock
        {
            Text = hint,
            Foreground = StalkerTheme.BrushTextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        empty.Bind(Visual.IsVisibleProperty, new Binding("!" + collection + ".Count") { Source = vm });
        return new Grid { Children = { list, empty } };
    }

    private static Control BuildSnapshots(ToolkitEnvironmentViewModel vm)
    {
        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(SectionTitle(L.T("УПРАВЛЯЕМЫЕ СНИМКИ")));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Снимки включают только файлы и манифесты Game Fix, Companion и настроек, которыми владеет инструмент. Восстановление повторно применяет их через исходные провайдеры."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        var list = new ListBox
        {
            ItemsSource = vm.Snapshots,
            ItemTemplate = new FuncDataTemplate<ToolkitSnapshotInfo>((snapshot, _) => new TextBlock
            {
                Text = snapshot is null ? string.Empty : L.T("{0:yyyy-MM-dd HH:mm} · {1} · исправлений: {2} · файлов: {3}",
                    snapshot.CreatedUtc.ToLocalTime(), GameTargetCatalog.Get(snapshot.Target).Title, snapshot.FixIds.Count, snapshot.Files.Count),
                Margin = new Thickness(4),
                TextWrapping = TextWrapping.Wrap,
            }, true),
            MinHeight = 68,
            MaxHeight = 142,
            Background = StalkerTheme.BrushBgPanel,
            Foreground = StalkerTheme.BrushTextPrimary,
        };
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ToolkitEnvironmentViewModel.SelectedSnapshot)) { Source = vm, Mode = BindingMode.TwoWay });
        stack.Children.Add(EmptyOverlay(list, vm, nameof(ToolkitEnvironmentViewModel.Snapshots), L.T("Снимков пока нет: создайте первый кнопкой ниже.")));
        var details = new TextBlock { Foreground = StalkerTheme.BrushTextMuted, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        details.Bind(TextBlock.TextProperty, new Binding(nameof(ToolkitEnvironmentViewModel.SelectedSnapshotDetails)) { Source = vm });
        stack.Children.Add(details);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var create = StalkerTheme.StalkerButton(L.T("СОЗДАТЬ СНИМОК"), true, minWidth: 155);
        create.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.CreateSnapshotCommand)) { Source = vm });
        buttons.Children.Add(create);
        var restore = StalkerTheme.StalkerButton(L.T("ВОССТАНОВИТЬ ВЫБРАННЫЙ"), false, minWidth: 190);
        restore.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.RestoreSnapshotCommand)) { Source = vm });
        restore.Bind(ToolTip.TipProperty, new Binding(nameof(ToolkitEnvironmentViewModel.SelectedSnapshot)) { Source = vm,
            Converter = new FuncValueConverter<ToolkitSnapshotInfo?, string?>(snapshot => snapshot is null ? L.T("Сначала выберите снимок.") : L.T("Восстановление создаст новый резервный снимок.")) });
        buttons.Children.Add(restore);
        var delete = StalkerTheme.StalkerButton(L.T("УДАЛИТЬ СНИМОК"), false, minWidth: 145);
        delete.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.DeleteSnapshotCommand)) { Source = vm });
        buttons.Children.Add(delete);
        var refresh = StalkerTheme.StalkerButton(L.T("ОБНОВИТЬ СПИСОК"), false, minWidth: 145);
        refresh.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.RefreshCommand)) { Source = vm });
        buttons.Children.Add(refresh);
        stack.Children.Add(buttons);
        return StalkerTheme.Card(stack);
    }

    private static Control BuildProfiles(ToolkitEnvironmentViewModel vm)
    {
        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(SectionTitle(L.T("ПРОФИЛИ ИГРЫ")));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Профиль сохраняет установленные идентификаторы Game Fix, состояние Companion и только явные настройки user.ltx, управляемые инструментом."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        var name = new TextBox
        {
            Watermark = L.T("ИМЯ ПРОФИЛЯ"),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
            MinHeight = 36,
        };
        name.Bind(TextBox.TextProperty, new Binding(nameof(ToolkitEnvironmentViewModel.ProfileName)) { Source = vm, Mode = BindingMode.TwoWay });
        stack.Children.Add(name);
        var list = new ListBox
        {
            ItemsSource = vm.Profiles,
            ItemTemplate = new FuncDataTemplate<ToolkitProfile>((profile, _) => new TextBlock
            {
                Text = profile is null ? string.Empty : L.T("{0} · {1} · исправлений: {2} · Companion: {3}", profile.Name,
                    GameTargetCatalog.Get(profile.Target).Title, profile.FixIds.Count,
                    profile.CompanionInstalled ? L.T("включён") : L.T("выключен")),
                Margin = new Thickness(4),
                TextWrapping = TextWrapping.Wrap,
            }, true),
            MinHeight = 68,
            MaxHeight = 142,
            Background = StalkerTheme.BrushBgPanel,
            Foreground = StalkerTheme.BrushTextPrimary,
        };
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ToolkitEnvironmentViewModel.SelectedProfile)) { Source = vm, Mode = BindingMode.TwoWay });
        stack.Children.Add(EmptyOverlay(list, vm, nameof(ToolkitEnvironmentViewModel.Profiles), L.T("Профилей пока нет: введите имя и сохраните текущее состояние.")));
        var details = new TextBlock { Foreground = StalkerTheme.BrushTextMuted, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        details.Bind(TextBlock.TextProperty, new Binding(nameof(ToolkitEnvironmentViewModel.SelectedProfileDetails)) { Source = vm });
        stack.Children.Add(details);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = StalkerTheme.StalkerButton(L.T("СОХРАНИТЬ ТЕКУЩЕЕ СОСТОЯНИЕ"), true, minWidth: 230);
        save.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.SaveProfileCommand)) { Source = vm });
        buttons.Children.Add(save);
        var apply = StalkerTheme.StalkerButton(L.T("ПРИМЕНИТЬ ПРОФИЛЬ"), false, minWidth: 165);
        apply.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.ApplyProfileCommand)) { Source = vm });
        buttons.Children.Add(apply);
        var delete = StalkerTheme.StalkerButton(L.T("УДАЛИТЬ ПРОФИЛЬ"), false, minWidth: 155);
        delete.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.DeleteProfileCommand)) { Source = vm });
        buttons.Children.Add(delete);
        stack.Children.Add(buttons);
        return StalkerTheme.Card(stack);
    }

    private static Control BuildUserLtx(ToolkitEnvironmentViewModel vm)
    {
        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(SectionTitle(L.T("НАСТРОЙКИ user.ltx")));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Редактируются только известные настройки с явным смыслом. Отсутствующее исходное значение означает значение по умолчанию движка; остальные строки сохраняются без изменений."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
        var path = new TextBox
        {
            Watermark = L.T("ПУТЬ К СУЩЕСТВУЮЩЕМУ user.ltx"),
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorder,
        };
        path.Bind(TextBox.TextProperty, new Binding(nameof(ToolkitEnvironmentViewModel.UserLtxPath)) { Source = vm, Mode = BindingMode.TwoWay });
        pathRow.Children.Add(path);
        var browse = StalkerTheme.StalkerButton(L.T("ОБЗОР…"), false, minWidth: 92);
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(browse)?.StorageProvider is not { CanOpen: true } storage) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L.T("ВЫБЕРИТЕ СУЩЕСТВУЮЩИЙ user.ltx"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("user.ltx") { Patterns = ["user.ltx"] }],
            });
            if (files.Count > 0 && files[0].Path.LocalPath is { } selectedPath) vm.UserLtxPath = selectedPath;
        };
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);
        var load = StalkerTheme.StalkerButton(L.T("ЗАГРУЗИТЬ"), true, minWidth: 105);
        load.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.LoadConfigCommand)) { Source = vm });
        Grid.SetColumn(load, 2);
        pathRow.Children.Add(load);
        stack.Children.Add(pathRow);
        var rows = new ItemsControl
        {
            ItemsSource = vm.ConfigSettings,
            ItemTemplate = new FuncDataTemplate<ToolkitConfigSettingRow>((row, _) => row is null ? null : BuildConfigRow(vm, row), true),
        };
        stack.Children.Add(rows);
        return StalkerTheme.Card(stack);
    }

    private static Control BuildConfigRow(ToolkitEnvironmentViewModel vm, ToolkitConfigSettingRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,*,*,*,150,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 3),
        };
        AddLabel(row.Key, 0, StalkerTheme.BrushAccentAmber);
        AddLabel(row.Meaning, 1, StalkerTheme.BrushTextPrimary);
        AddLabel(L.T("Текущее: {0}", row.CurrentDisplay), 2, StalkerTheme.BrushTextSecondary);
        AddLabel(L.T("По умолчанию: {0}", row.DefaultDisplay), 3, StalkerTheme.BrushTextMuted);
        AddLabel(row.StateDisplay, 4, row.HasConflict ? StalkerTheme.BrushDanger : StalkerTheme.BrushTextMuted);
        var value = new TextBox { Watermark = L.T("Новое значение"), MinWidth = 100 };
        value.Bind(TextBox.TextProperty, new Binding(nameof(ToolkitConfigSettingRow.ValueInput)) { Source = row, Mode = BindingMode.TwoWay });
        Grid.SetColumn(value, 5);
        grid.Children.Add(value);
        var apply = StalkerTheme.StalkerButton(L.T("ПРИМЕНИТЬ"), false, minWidth: 95);
        apply.IsEnabled = row.CanApplyValue;
        row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ToolkitConfigSettingRow.CanApplyValue)) apply.IsEnabled = row.CanApplyValue; };
        ToolTip.SetTip(apply, row.HasConflict ? L.T("Сначала разрешите конфликт изменения вне инструмента.") : L.T("Введите значение в допустимом диапазоне."));
        apply.Click += (_, _) => vm.ApplyConfig(row);
        Grid.SetColumn(apply, 6);
        grid.Children.Add(apply);
        var restore = StalkerTheme.StalkerButton(L.T("ПО УМОЛЧАНИЮ"), false, minWidth: 125);
        restore.IsEnabled = row.CanRestoreDefault;
        ToolTip.SetTip(restore, L.T("Возвращает исходную строку или удаляет явную команду, если раньше действовало значение движка."));
        restore.Click += (_, _) => vm.RestoreConfigDefault(row);
        Grid.SetColumn(restore, 7);
        grid.Children.Add(restore);
        return StalkerTheme.Card(grid, padding: new Thickness(8));

        void AddLabel(string text, int column, IBrush foreground)
        {
            var label = new TextBlock { Text = text, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(label, column);
            grid.Children.Add(label);
        }
    }

    private static Control BuildAudit(ToolkitEnvironmentViewModel vm)
    {
        var stack = new StackPanel { Spacing = 9 };
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        title.Children.Add(SectionTitle(L.T("АУДИТ УСТАНОВКИ")));
        var audit = StalkerTheme.StalkerButton(L.T("ПРОВЕРИТЬ"), true, minWidth: 120);
        audit.Bind(Button.CommandProperty, new Binding(nameof(ToolkitEnvironmentViewModel.AuditCommand)) { Source = vm });
        Grid.SetColumn(audit, 1);
        title.Children.Add(audit);
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Файлы с валидными манифестами отмечаются как управляемые. Файл считается ванильным только при совпадении известного хэша точной Steam-сборки; остальные loose-файлы остаются неизвестными. Очистка устаревшего Game Fix восстанавливает записанные исходные байты только после проверки манифеста, резервных копий и владения; неизвестное состояние не удаляется."),
            Foreground = StalkerTheme.BrushTextMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        var status = new TextBlock { Foreground = StalkerTheme.BrushTextSecondary, TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ToolkitEnvironmentViewModel.AuditStatus)) { Source = vm });
        stack.Children.Add(status);
        var list = new ItemsControl
        {
            ItemsSource = vm.AuditRows,
            ItemTemplate = new FuncDataTemplate<ToolkitAuditRow>((row, _) =>
            {
                if (row is null) return null;
                var content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = row.RelativePath, Foreground = StalkerTheme.BrushTextPrimary, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = row.Classification, Foreground = StalkerTheme.BrushAccentAmber },
                        new TextBlock { Text = row.Detail, Foreground = StalkerTheme.BrushTextMuted, TextWrapping = TextWrapping.Wrap },
                    },
                };
                if (row.CanCleanup)
                {
                    var cleanup = StalkerTheme.StalkerButton(L.T("ОЧИСТИТЬ УСТАРЕВШИЙ ФИКС"), false, minWidth: 210);
                    ToolTip.SetTip(cleanup, L.T("Восстановит файлы только при совпадении текущих и резервных SHA-256."));
                    cleanup.Click += async (_, _) => await vm.CleanupOrphanAsync(row);
                    content.Children.Add(cleanup);
                }

                return StalkerTheme.Card(content, padding: new Thickness(8));
            }, true),
        };
        stack.Children.Add(list);
        return StalkerTheme.Card(stack);
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Foreground = StalkerTheme.BrushAccentAmber,
    };
}
