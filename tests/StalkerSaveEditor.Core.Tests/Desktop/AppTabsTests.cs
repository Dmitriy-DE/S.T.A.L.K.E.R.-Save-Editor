using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class AppTabsTests
{
    [Fact]
    public void Every_screen_id_is_listed_once_and_unknown_ids_fall_back_to_the_overview()
    {
        var declared = typeof(AppTabs).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(20, declared.Length);
        Assert.Equal(declared.Length, declared.Distinct(StringComparer.Ordinal).Count());
        Assert.True(AppTabs.All.SetEquals(declared));
        Assert.Equal(AppTabs.GameFixes, AppTabs.Normalize("game-fixes"));
        Assert.Equal(AppTabs.Overview, AppTabs.Normalize("game-fixs"));
        Assert.Equal(AppTabs.Overview, AppTabs.Normalize(null));
    }

    [Fact]
    public void A_mistyped_screen_id_shows_the_overview_and_every_screen_has_a_title()
    {
        using var vm = new SaveLibraryViewModel(discoverLocalSaves: false);
        vm.SelectedTab = AppTabs.Settings;
        vm.SelectedTab = "setings";

        Assert.Equal(AppTabs.Overview, vm.SelectedTab);
        foreach (var tab in AppTabs.All)
        {
            vm.SelectedTab = tab;
            Assert.Equal(tab, vm.SelectedTab);
            Assert.False(string.IsNullOrWhiteSpace(vm.CurrentPageTitle));
            Assert.False(string.IsNullOrWhiteSpace(vm.CurrentGroupTitle));
        }
    }

    [Fact]
    public void Disposing_the_root_view_model_cancels_its_lifetime_and_can_be_repeated()
    {
        var vm = new SaveLibraryViewModel(discoverLocalSaves: false);
        var lifetime = vm.Lifetime;

        vm.Dispose();
        vm.Dispose();

        Assert.True(lifetime.IsCancellationRequested);
    }
}
