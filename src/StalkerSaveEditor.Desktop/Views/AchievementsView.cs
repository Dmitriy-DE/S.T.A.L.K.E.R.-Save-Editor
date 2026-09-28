using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using StalkerSaveEditor.Desktop.Styles;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Desktop.Views;

public sealed class AchievementsView : UserControl
{
    public AchievementsView()
    {
        Content = BuildContent();
    }

    private static Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(20),
        };

        // 1. Header & Availability Banner
        var headerPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "ДОСТИЖЕНИЯ STEAM",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.2,
        });

        var statusBorder = new Border
        {
            Background = StalkerTheme.BrushBgInput,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
        };
        var statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextSecondary,
        };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(AchievementsViewModel.AvailabilityMessage)));
        statusBorder.Child = statusText;
        headerPanel.Children.Add(statusBorder);

        root.Children.Add(headerPanel);

        // 2. Control Row: Game Selector, Search, Refresh
        var controlRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,*,12,Auto"),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var appSelector = new ComboBox
        {
            Width = 260,
            ItemsSource = new[]
            {
                new ComboBoxItem { Content = "S.T.A.L.K.E.R.: Зов Припяти (41700)", Tag = 41700 },
                new ComboBoxItem { Content = "S.T.A.L.K.E.R.: Чистое Небо (20510)", Tag = 20510 },
                new ComboBoxItem { Content = "S.T.A.L.K.E.R.: Тень Чернобыля (4500)", Tag = 4500 },
                new ComboBoxItem { Content = "S.T.A.L.K.E.R. 2: Heart of Chornobyl (1643320)", Tag = 1643320 },
            },
            SelectedIndex = 0,
        };
        appSelector.SelectionChanged += (s, e) =>
        {
            if (appSelector.SelectedItem is ComboBoxItem item && item.Tag is int appId)
            {
                if (appSelector.DataContext is AchievementsViewModel vm)
                {
                    vm.SelectedAppId = appId;
                }
            }
        };
        controlRow.Children.Add(appSelector);

        var searchBox = new TextBox
        {
            Watermark = "Поиск по названию или описанию...",
            Background = StalkerTheme.BrushBgInput,
            Foreground = StalkerTheme.BrushTextPrimary,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
        };
        searchBox.Bind(TextBox.TextProperty, new Binding(nameof(AchievementsViewModel.SearchText), BindingMode.TwoWay));
        Grid.SetColumn(searchBox, 2);
        controlRow.Children.Add(searchBox);

        var refreshBtn = StalkerTheme.StalkerButton("ОБНОВИТЬ");
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(AchievementsViewModel.RefreshCommand)));
        Grid.SetColumn(refreshBtn, 4);
        controlRow.Children.Add(refreshBtn);

        Grid.SetRow(controlRow, 1);
        root.Children.Add(controlRow);

        // 3. Progress Panel
        var progressCard = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 14),
        };

        var progressStack = new StackPanel { Spacing = 6 };
        var progressLabel = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushTextPrimary,
        };
        progressLabel.Bind(TextBlock.TextProperty, new Binding(nameof(AchievementsViewModel.ProgressText)));
        progressStack.Children.Add(progressLabel);

        var progressBar = new ProgressBar
        {
            Height = 10,
            Minimum = 0,
            Maximum = 100,
            Foreground = StalkerTheme.BrushAccentAmber,
            Background = StalkerTheme.BrushBgInput,
        };
        progressBar.Bind(ProgressBar.ValueProperty, new Binding(nameof(AchievementsViewModel.ProgressPercentage)));
        progressStack.Children.Add(progressBar);

        progressCard.Child = progressStack;
        Grid.SetRow(progressCard, 2);
        root.Children.Add(progressCard);

        // 4. Main Area: List of achievements + Confirmation dialog overlay
        var listContainer = new Grid();

        ListBox? listBox = null;
        listBox = new ListBox
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushBorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ItemTemplate = StalkerTheme.Template<AchievementItemViewModel>(item =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    Margin = new Thickness(8, 6),
                };

                // Trophy / Lock Icon
                var iconText = new TextBlock
                {
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                iconText.Bind(TextBlock.TextProperty, new Binding(nameof(AchievementItemViewModel.IconText)));
                row.Children.Add(iconText);

                // Title + Description
                var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                var nameBlock = new TextBlock
                {
                    Text = item.Name,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = StalkerTheme.BrushTextPrimary,
                    FontSize = 13,
                };
                infoStack.Children.Add(nameBlock);

                var descBlock = new TextBlock
                {
                    Text = item.Description,
                    FontSize = 11,
                    Foreground = StalkerTheme.BrushTextSecondary,
                    TextWrapping = TextWrapping.Wrap,
                };
                infoStack.Children.Add(descBlock);

                Grid.SetColumn(infoStack, 1);
                row.Children.Add(infoStack);

                // Status Badge
                var statusBadge = new Border
                {
                    Background = Brush.Parse(item.StatusBadgeColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0),
                };
                statusBadge.Child = new TextBlock
                {
                    Text = item.StatusText,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                };
                Grid.SetColumn(statusBadge, 2);
                row.Children.Add(statusBadge);

                // Toggle Button
                var toggleBtn = StalkerTheme.StalkerButton(item.IsAchieved ? "СНЯТЬ" : "ПОЛУЧИТЬ", isPrimary: !item.IsAchieved);
                toggleBtn.VerticalAlignment = VerticalAlignment.Center;
                toggleBtn.Click += (s, e) =>
                {
                    if (listBox?.DataContext is AchievementsViewModel vm)
                    {
                        vm.RequestToggle(item);
                    }
                };
                Grid.SetColumn(toggleBtn, 3);
                row.Children.Add(toggleBtn);

                return row;
            }),
        };
        listBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(AchievementsViewModel.FilteredAchievements)));
        listContainer.Children.Add(listBox);

        // Confirmation Modal Overlay
        var confirmOverlay = new Border
        {
            Background = Brush.Parse("#CC1A1612"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        confirmOverlay.Bind(Visual.IsVisibleProperty, new Binding(nameof(AchievementsViewModel.ShowConfirmDialog)));

        var modalCard = new Border
        {
            Background = StalkerTheme.BrushBgPanel,
            BorderBrush = StalkerTheme.BrushAccentAmber,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(24),
            Width = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var modalStack = new StackPanel { Spacing = 14 };
        var modalTitle = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = StalkerTheme.BrushAccentAmber,
            LetterSpacing = 1.1,
        };
        modalTitle.Bind(TextBlock.TextProperty, new Binding(nameof(AchievementsViewModel.ConfirmDialogTitle)));
        modalStack.Children.Add(modalTitle);

        var modalMsg = new TextBlock
        {
            FontSize = 12,
            Foreground = StalkerTheme.BrushTextPrimary,
            TextWrapping = TextWrapping.Wrap,
        };
        modalMsg.Bind(TextBlock.TextProperty, new Binding(nameof(AchievementsViewModel.ConfirmDialogMessage)));
        modalStack.Children.Add(modalMsg);

        var modalBtnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var confirmBtn = StalkerTheme.StalkerButton("ПОДТВЕРДИТЬ", isPrimary: true);
        confirmBtn.Bind(Button.CommandProperty, new Binding(nameof(AchievementsViewModel.ConfirmToggleCommand)));
        modalBtnRow.Children.Add(confirmBtn);

        var cancelBtn = StalkerTheme.StalkerButton("ОТМЕНА", isPrimary: false);
        cancelBtn.Bind(Button.CommandProperty, new Binding(nameof(AchievementsViewModel.CancelToggleCommand)));
        Grid.SetColumn(cancelBtn, 2);
        modalBtnRow.Children.Add(cancelBtn);

        modalStack.Children.Add(modalBtnRow);
        modalCard.Child = modalStack;
        confirmOverlay.Child = modalCard;

        listContainer.Children.Add(confirmOverlay);

        Grid.SetRow(listContainer, 3);
        root.Children.Add(listContainer);

        // 5. Status Footer
        var footerText = new TextBlock
        {
            FontSize = 11,
            Foreground = StalkerTheme.BrushAccentAmber,
            Margin = new Thickness(0, 10, 0, 0),
        };
        footerText.Bind(TextBlock.TextProperty, new Binding(nameof(AchievementsViewModel.StatusMessage)));
        Grid.SetRow(footerText, 4);
        root.Children.Add(footerText);

        return root;
    }
}
