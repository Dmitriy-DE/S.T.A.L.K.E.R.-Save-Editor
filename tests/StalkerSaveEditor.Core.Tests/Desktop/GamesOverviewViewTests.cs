using Avalonia.Controls;
using Avalonia.Controls.Templates;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[Collection(AvaloniaViewTestGroup.Name)]
public sealed class GamesOverviewViewTests
{
    [Fact]
    public void Games_overview_is_available_without_a_selected_save()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory);

        viewModel.SelectedTab = "games";

        Assert.True(viewModel.IsGamesOverviewTab);
        Assert.True(viewModel.ShowGamesOverviewScreen);
        Assert.False(viewModel.ShouldShowEmptyState);
        Assert.False(viewModel.IsFirstRunWizardVisible);
    }

    [Fact]
    public void Dashboard_does_not_scan_until_user_requests_discovery()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory);

        var view = GamesOverviewView.Build(viewModel);
        var discover = Descendants(view).OfType<Button>().Single(button => button.Name == "discover-installations");

        Assert.Empty(viewModel.GameDoctor.Installations);
        Assert.False(viewModel.GameDoctor.IsDiscovering);
        Assert.Empty(viewModel.GameDoctor.DiscoveryStatus);
        Assert.Same(viewModel.GameDoctor.DiscoverInstallationsCommand, discover.Command);
    }

    [Fact]
    public void Mods_entry_is_disabled_with_a_visible_reason()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory);
        var view = GamesOverviewView.Build(viewModel);

        var mods = Descendants(view).OfType<Button>().Single(button => button.Name == "unsupported-mods");
        var reason = Assert.Single(Descendants(view).OfType<TextBlock>(), text => text.Name == "unsupported-mods-reason");

        Assert.False(mods.IsEnabled);
        Assert.True(reason.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(reason.Text));
    }

    [Fact]
    public void S2_companion_route_remains_available_as_an_experimental_status_surface()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory);
        var view = GamesOverviewView.Build(viewModel);

        viewModel.GameFixes.SelectedTarget = viewModel.GameFixes.Targets.Single(target => target.Target == GameTarget.Stalker2);

        var companion = Descendants(view).OfType<Button>().Single(button => button.Name == "open-companion");
        var reason = Assert.Single(Descendants(view).OfType<TextBlock>(), text => text.Name == "companion-disabled-reason");
        Assert.True(companion.IsEnabled);
        Assert.True(reason.IsVisible);
        Assert.Contains("экспериментальное состояние", reason.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("отдельно по запросу", reason.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enhanced_edition_companion_route_is_disabled_with_a_reason()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory);
        var view = GamesOverviewView.Build(viewModel);

        viewModel.GameFixes.SelectedTarget = viewModel.GameFixes.Targets.Single(target => target.Target == GameTarget.ShadowOfChernobylEnhancedEdition);

        var companion = Descendants(view).OfType<Button>().Single(button => button.Name == "open-companion");
        var reason = Assert.Single(Descendants(view).OfType<TextBlock>(), text => text.Name == "companion-disabled-reason");
        Assert.False(companion.IsEnabled);
        Assert.True(reason.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(reason.Text));
    }

    [Fact]
    public async Task Discovery_error_shows_a_clear_actionable_message_without_exception_details()
    {
        var viewModel = new GameDoctorViewModel(() => throw new IOException("C:/private/game: access denied"));

        await viewModel.DiscoverInstallationsAsync();

        Assert.Contains("Докторе игры", viewModel.DiscoveryStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("C:/private/game", viewModel.DiscoveryStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discovery_idle_state_is_hidden_while_the_explicit_search_is_running()
    {
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var viewModel = new GameDoctorViewModel(() =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test discovery was not released.");
            return [];
        });

        Assert.True(viewModel.IsDiscoveryIdle);
        var discovery = viewModel.DiscoverInstallationsAsync();
        var searchStarted = started.Wait(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(searchStarted);
            Assert.True(viewModel.IsDiscovering);
            Assert.False(viewModel.IsDiscoveryIdle);
        }
        finally
        {
            release.Set();
        }

        await discovery;
        Assert.False(viewModel.IsDiscoveryIdle);
    }

    [Fact]
    public async Task Refreshing_discovery_rebinds_a_still_found_installation()
    {
        var scans = 0;
        var viewModel = new GameDoctorViewModel(() =>
        {
            scans++;
            return [new GameDoctorInstallation(
                GameTarget.ShadowOfChernobyl,
                "C:/stalker",
                GameInstallSource.Steam,
                scans == 1 ? "100" : "200")];
        });

        await viewModel.DiscoverInstallationsAsync();
        viewModel.SelectedInstallation = viewModel.Installations.Single();
        await viewModel.DiscoverInstallationsAsync();

        Assert.Same(viewModel.Installations.Single(), viewModel.SelectedInstallation);
        Assert.Equal("200", viewModel.SelectedInstallation.BuildId);
    }

    [Fact]
    public async Task Refreshing_discovery_clears_a_removed_installation_and_its_directory()
    {
        var scans = 0;
        var viewModel = new GameDoctorViewModel(() =>
        {
            scans++;
            return scans == 1
                ? [new GameDoctorInstallation(GameTarget.ShadowOfChernobyl, "C:/removed-game", GameInstallSource.Steam, "100")]
                : [];
        });

        await viewModel.DiscoverInstallationsAsync();
        viewModel.SelectedInstallation = viewModel.Installations.Single();
        await viewModel.DiscoverInstallationsAsync();

        Assert.Null(viewModel.SelectedInstallation);
        Assert.Empty(viewModel.GameDirectory);
    }

    [Fact]
    public void Installation_rows_follow_their_data_context()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory);
        var first = new GameDoctorInstallationOption(GameTarget.ShadowOfChernobyl, "first game", "C:/first", GameInstallSource.Selected, "100");
        var second = new GameDoctorInstallationOption(GameTarget.ClearSky, "selected game", "C:/selected", GameInstallSource.Steam, "200");

        var view = GamesOverviewView.Build(viewModel);
        var list = Descendants(view).OfType<ListBox>()
            .Single(candidate => candidate.ItemTemplate is FuncDataTemplate<GameDoctorInstallationOption>);
        var template = Assert.IsType<FuncDataTemplate<GameDoctorInstallationOption>>(list.ItemTemplate);
        var row = template.Build(first) ?? throw new InvalidOperationException("Installation row template returned no control.");
        row.DataContext = second;

        var labels = Descendants(row).OfType<TextBlock>().Select(label => label.Text).ToArray();
        Assert.Contains(second.Title, labels);
        Assert.Contains(second.Directory, labels);
        Assert.DoesNotContain(first.Title, labels);
        Assert.DoesNotContain(first.Directory, labels);
    }

    [Fact]
    public void Changing_the_dashboard_game_clears_a_directory_from_the_previous_installation()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory);
        var installation = new GameDoctorInstallationOption(
            GameTarget.ShadowOfChernobyl,
            "Shadow of Chernobyl",
            "C:/old-game",
            GameInstallSource.Steam,
            "100");
        viewModel.GameDoctor.Installations.Add(installation);
        viewModel.GameDoctor.SelectedInstallation = installation;

        _ = GamesOverviewView.Build(viewModel);
        viewModel.GameFixes.SelectedTarget = viewModel.GameFixes.Targets.Single(target => target.Target == GameTarget.ClearSky);

        Assert.Null(viewModel.GameDoctor.SelectedInstallation);
        Assert.Empty(viewModel.GameDoctor.GameDirectory);
        Assert.Empty(viewModel.GameFixes.GameDirectory);
        Assert.Equal(GameTarget.ClearSky, viewModel.GameDoctor.SelectedTarget.Target);
    }

    private static SaveLibraryViewModel CreateViewModel(TemporaryDirectory directory) => new(
        discoverLocalSaves: false,
        saveDirectoriesProvider: () => [],
        backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
        draftsDirectory: Path.Combine(directory.Path, "drafts"));

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        IEnumerable<Control> children = root switch
        {
            Panel panel => panel.Children.OfType<Control>(),
            Decorator { Child: { } child } => [child],
            ContentControl { Content: Control child } => [child],
            _ => [],
        };
        foreach (var child in children)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stalker-games-overview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
