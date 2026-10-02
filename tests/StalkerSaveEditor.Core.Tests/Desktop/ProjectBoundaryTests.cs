using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void The_ui_library_shared_with_the_web_edition_does_not_reference_steam_or_the_updater()
    {
        var references = typeof(StalkerSaveEditor.Desktop.Services.HostPlatform).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .ToArray();

        Assert.Contains("StalkerSaveEditor.Core", references);
        Assert.DoesNotContain("StalkerSaveEditor.Steam", references);
        Assert.DoesNotContain("StalkerSaveEditor.Updater", references);
        Assert.DoesNotContain("StalkerSaveEditor.Host", references);
    }

    [Fact]
    public async Task Without_a_host_the_desktop_only_services_report_unavailable_instead_of_failing()
    {
        var cloud = new StalkerSaveEditor.Desktop.Services.UnavailableCloudServiceAdapter();
        var updates = new StalkerSaveEditor.Desktop.Services.UnavailableUpdateServiceAdapter();
        var achievements = new StalkerSaveEditor.Desktop.Services.UnavailableSteamAchievementsAdapter();

        Assert.False(cloud.IsSteamAvailable);
        Assert.Empty(await cloud.ListCloudFilesAsync(41700, []));
        Assert.Equal(StalkerSaveEditor.Desktop.Services.UpdateState.Unavailable, (await updates.CheckAsync()).State);
        Assert.False(achievements.IsAvailable(41700));
        Assert.Empty(await achievements.ListAsync(41700));
    }
}
