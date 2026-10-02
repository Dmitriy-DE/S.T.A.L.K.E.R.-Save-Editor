using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;

namespace StalkerSaveEditor.Desktop;

public sealed partial class MainWindow : Window
{
    private static Control BuildSavesPane(SaveLibraryViewModel vm)
    {
        var dock = new DockPanel();

        var heading = new TextBlock
        {
            Text = L.T("БИБЛИОТЕКА СОХРАНЕНИЙ"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var count = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        count.Bind(TextBlock.TextProperty, new Binding("Saves.Count") { Source = vm });

        var countBadge = new Border
        {
            Background = StalkerTheme.BrushBgElevated,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            MinWidth = 28,
            Padding = new Thickness(6, 3),
            Child = count,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var refreshButton = StalkerTheme.StalkerButton("↻", isPrimary: false, minWidth: 34);
        refreshButton.Margin = new Thickness(6, 0, 0, 0);
        ToolTip.SetTip(refreshButton, L.T("Обновить"));
        Avalonia.Automation.AutomationProperties.SetName(refreshButton, L.T("Обновить"));
        refreshButton.Bind(Button.CommandProperty, new Binding(nameof(SaveLibraryViewModel.RefreshCommand)) { Source = vm });

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { countBadge, refreshButton },
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(14, 12, 10, 8),
            Children = { heading, headerActions },
        };
        Grid.SetColumn(headerActions, 1);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var saveList = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            // Keep row values on the data context so this template is ready for later recycling.
            ItemTemplate = StalkerTheme.RecyclingTemplate<SaveFileSummary>(() =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("72,*"),
                    ColumnSpacing = 10,
                };

                var previewFallback = new Grid
                {
                    Width = 72,
                    Height = 54,
                };
                var previewFrame = new Border
                {
                    Background = StalkerTheme.BrushBgBase,
                    BorderBrush = StalkerTheme.BrushBorderSubtle,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = previewFallback,
                };
                previewFallback.Children.Add(new TextBlock
                {
                    Text = "◇",
                    FontSize = 20,
                    Foreground = StalkerTheme.BrushAccentDim,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var preview = new Image { Stretch = Stretch.UniformToFill };
                preview.Bind(Image.SourceProperty, new Binding(nameof(SaveFileSummary.Preview)));
                preview.Bind(Visual.IsVisibleProperty, new Binding(nameof(SaveFileSummary.HasPreview)));
                previewFallback.Children.Add(preview);
                row.Children.Add(previewFrame);

                var details = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                    RowSpacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var name = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                name.Bind(TextBlock.TextProperty, new Binding(nameof(SaveFileSummary.DisplayName)));
                name.Bind(ToolTip.TipProperty, new Binding(nameof(SaveFileSummary.DisplayName)));
                details.Children.Add(name);

                var release = new TextBlock
                {
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushAccentDim,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                release.Bind(TextBlock.TextProperty, new Binding(nameof(SaveFileSummary.ReleaseName)));
                Grid.SetRow(release, 1);
                details.Children.Add(release);

                var metadata = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                var slot = new TextBlock
                {
                    Name = "save-library-date",
                    FontSize = 10,
                    Foreground = StalkerTheme.BrushTextSecondary,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                slot.Bind(TextBlock.TextProperty, new Binding(nameof(SaveFileSummary.LibraryMetadataDisplay)));
                metadata.Children.Add(slot);

                var sizeText = new TextBlock
                {
                    FontSize = 10,
                    // Muted grey vanished on the amber selected row; secondary keeps contrast on both.
                    Foreground = StalkerTheme.BrushTextSecondary,
                    Margin = new Thickness(6, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                sizeText.Bind(TextBlock.TextProperty, new Binding(nameof(SaveFileSummary.FileSizeDisplay)));
                Grid.SetColumn(sizeText, 1);
                metadata.Children.Add(sizeText);
                Grid.SetRow(metadata, 2);
                details.Children.Add(metadata);

                Grid.SetColumn(details, 1);
                row.Children.Add(details);
                return new Border
                {
                    Background = Avalonia.Media.Brushes.Transparent,
                    BorderBrush = StalkerTheme.BrushBorderSubtle,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Margin = new Thickness(5, 3),
                    Child = row,
                };
            }),
        };
        saveList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SaveLibraryViewModel.SelectedSave))
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
        });
        saveList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SaveLibraryViewModel.Saves)) { Source = vm });
        dock.Children.Add(saveList);

        return new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = dock,
        };
    }
}
