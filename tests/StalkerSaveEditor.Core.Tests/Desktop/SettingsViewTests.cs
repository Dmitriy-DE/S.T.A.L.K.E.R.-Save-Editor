using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[Collection(AvaloniaViewTestGroup.Name)]
public sealed class SettingsViewTests
{
    [Fact]
    public void Category_navigation_keeps_all_settings_sections_reachable()
    {
        var settings = new SettingsViewModel(["/saves"], "/backups");
        var view = SettingsView.Build(settings);
        var navigation = view.GetLogicalDescendants().OfType<Button>()
            .Where(button => button.Name?.StartsWith("settings-nav-", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(9, navigation.Length);
        Assert.Contains(view.GetLogicalDescendants().OfType<Border>(), panel => panel.Name == "settings-content-general" && panel.IsVisible);

        var interfaceButton = navigation.Single(button => button.Name == "settings-nav-interface");
        interfaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Contains(view.GetLogicalDescendants().OfType<Border>(), panel => panel.Name == "settings-content-interface" && panel.IsVisible);
        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Border>(), panel => panel.Name == "settings-content-general" && panel.IsVisible);
    }

    [Fact]
    public void Requested_initial_section_is_selected_without_removing_other_panels()
    {
        var view = SettingsView.Build(new SettingsViewModel([], "/backups"), initialCategoryId: "interface");

        Assert.Contains(view.GetLogicalDescendants().OfType<Border>(), panel => panel.Name == "settings-content-interface" && panel.IsVisible);
        Assert.Contains(view.GetLogicalDescendants().OfType<Border>(), panel => panel.Name == "settings-content-general" && !panel.IsVisible);
    }

    [Fact]
    public void Editing_backup_path_survives_category_navigation()
    {
        var settings = new SettingsViewModel(["/saves"], "/backups");
        var view = SettingsView.Build(settings);
        var backupButton = FindButton(view, "settings-nav-data-backups");
        backupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var backupInput = view.GetLogicalDescendants().OfType<TextBox>()
            .Single(textBox => textBox.Name == "settings-backup-directory");
        backupInput.Text = "/new/backup/path";
        Assert.Equal("/new/backup/path", settings.BackupDirectory);

        FindButton(view, "settings-nav-interface").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindButton(view, "settings-nav-data-backups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Same(backupInput, view.GetLogicalDescendants().OfType<TextBox>()
            .Single(textBox => textBox.Name == "settings-backup-directory"));
        Assert.Equal("/new/backup/path", backupInput.Text);
    }

    [Fact]
    public void Unsupported_general_preferences_are_disabled_and_explain_why()
    {
        var view = SettingsView.Build(new SettingsViewModel([], "/backups"));

        foreach (var name in new[]
        {
            "settings-unsupported-auto-updates",
            "settings-unsupported-experimental-warnings",
            "settings-unsupported-save-confirmation",
            "settings-unsupported-open-last-source",
        })
        {
            var control = view.GetLogicalDescendants().OfType<CheckBox>().Single(checkBox => checkBox.Name == name);
            Assert.False(control.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
            var reason = view.GetLogicalDescendants().OfType<TextBlock>().Single(textBlock => textBlock.Name == name + "-reason");
            Assert.False(string.IsNullOrWhiteSpace(reason.Text));
        }
    }

    [Fact]
    public void Save_directory_rows_follow_their_current_data_context()
    {
        var view = SettingsView.Build(new SettingsViewModel(["/first/saves"], "/backups"));
        var list = view.GetLogicalDescendants().OfType<ItemsControl>()
            .Single(control => control.Name == "settings-directory-list");
        var template = Assert.IsType<FuncDataTemplate<string>>(list.ItemTemplate);
        var row = template.Build("/first/saves") ?? throw new InvalidOperationException("Save-directory row template returned no control.");
        row.DataContext = "/second/saves";

        Assert.Contains(row.GetLogicalDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "/second/saves");
        Assert.DoesNotContain(row.GetLogicalDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "/first/saves");
        Assert.Equal("/second/saves", Assert.Single(row.GetLogicalDescendants().OfType<Button>()).CommandParameter);
    }

    [Fact]
    public void Cloud_and_update_actions_route_to_the_existing_screens()
    {
        var settings = new SettingsViewModel([], "/backups");
        var destinations = new List<string>();
        var view = SettingsView.Build(settings, navigateTo: destinations.Add);

        FindButton(view, "settings-open-cloud").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindButton(view, "settings-open-updates").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindButton(view, "settings-open-game-fixes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Collection(destinations,
            destination => Assert.Equal("cloud", destination),
            destination => Assert.Equal("updates", destination),
            destination => Assert.Equal("game-fixes", destination));
    }

    private static Button FindButton(Control view, string name) => view.GetLogicalDescendants().OfType<Button>()
        .Single(button => button.Name == name);
}
