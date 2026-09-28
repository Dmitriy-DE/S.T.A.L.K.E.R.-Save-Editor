using Xunit;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void SettingsViewModel_InitializesWith15Languages()
    {
        var vm = new SettingsViewModel(
            saveDirectories: ["/path/one", "/path/two"],
            backupDirectory: "/path/backups",
            currentLanguageCode: "en");

        Assert.Equal(15, vm.Languages.Count); // 14 translated + 1 original ru
        Assert.Equal("en", vm.SelectedLanguage.Code);
        Assert.Equal(2, vm.SaveDirectories.Count);
        Assert.Equal("/path/backups", vm.BackupDirectory);
        Assert.True(vm.SoundEnabled);
        Assert.Equal(80, vm.SoundVolume);
    }

    [Fact]
    public void AddSaveDirectory_AddsUniqueDirectory()
    {
        var vm = new SettingsViewModel(
            saveDirectories: ["/path/one"],
            backupDirectory: "/path/backups");

        var added = vm.AddSaveDirectory("/path/two");
        Assert.True(added);
        Assert.Equal(2, vm.SaveDirectories.Count);
        Assert.Contains("/path/two", vm.SaveDirectories);

        // Duplicate is rejected
        var duplicate = vm.AddSaveDirectory("/path/two");
        Assert.False(duplicate);
        Assert.Equal(2, vm.SaveDirectories.Count);

        // Empty/null is rejected
        Assert.False(vm.AddSaveDirectory(""));
        Assert.False(vm.AddSaveDirectory(null));
    }

    [Fact]
    public void RemoveSaveDirectory_RemovesExistingDirectory()
    {
        var vm = new SettingsViewModel(
            saveDirectories: ["/path/one", "/path/two"],
            backupDirectory: "/path/backups");

        vm.RemoveSaveDirectory("/path/one");
        Assert.Single(vm.SaveDirectories);
        Assert.DoesNotContain("/path/one", vm.SaveDirectories);
    }

    [Fact]
    public void FirstRunWizard_AppearsWhenNoSavesAndHidesOnDismiss()
    {
        var vm = new SaveLibraryViewModel(discoverLocalSaves: false);

        // No saves loaded -> wizard visible on startup
        Assert.Empty(vm.Saves);
        Assert.True(vm.IsFirstRunWizardVisible);
        Assert.False(vm.ShouldShowEmptyState);

        // User dismisses wizard -> empty state is shown instead
        vm.DismissWizardCommand.Execute(null);
        Assert.False(vm.IsFirstRunWizardVisible);
        Assert.True(vm.ShouldShowEmptyState);
    }

    [Fact]
    public void FirstRunWizard_HidesWhenNavigatingToSettingsTab()
    {
        var vm = new SaveLibraryViewModel(discoverLocalSaves: false);
        Assert.True(vm.IsFirstRunWizardVisible);

        vm.SelectedTab = "settings";

        Assert.False(vm.IsFirstRunWizardVisible);
        Assert.True(vm.ShowSettingsScreen);
    }
}
