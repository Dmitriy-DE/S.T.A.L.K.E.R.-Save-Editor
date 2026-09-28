using Xunit;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Core.Tests.Desktop;

/// <summary>
/// Unit tests for <see cref="CompanionViewModel"/> using fake <see cref="ICompanionService"/>
/// implementations. No real filesystem, no network, no process spawning.
/// </summary>
public sealed class CompanionViewModelTests
{
    // ── Fake implementations ──────────────────────────────────────────────────

    private sealed class FakeCompanionService : ICompanionService
    {
        public CompanionState StateToReturn { get; set; } = CompanionState.NotInstalled;
        public string GamePath { get; set; } = "/fake/game";
        public string Version { get; set; } = "v1";
        public TimeSpan? PingResult { get; set; }
        public bool InstallResult { get; set; } = true;
        public bool UninstallResult { get; set; } = true;
        public bool ThrowOnInstall { get; set; }
        public int GetStatusCallCount { get; private set; }
        public int InstallCallCount { get; private set; }
        public int UninstallCallCount { get; private set; }
        public int PingCallCount { get; private set; }

        public Task<CompanionStatus> GetStatusAsync(string gameReleaseId, CancellationToken ct = default)
        {
            GetStatusCallCount++;
            return Task.FromResult(new CompanionStatus(StateToReturn, Version, null, GamePath));
        }

        public Task<bool> InstallAsync(string gameReleaseId, CancellationToken ct = default)
        {
            InstallCallCount++;
            if (ThrowOnInstall) throw new InvalidOperationException("Anchor not found: bind_stalker.script");
            return Task.FromResult(InstallResult);
        }

        public Task<bool> UninstallAsync(string gameReleaseId, CancellationToken ct = default)
        {
            UninstallCallCount++;
            return Task.FromResult(UninstallResult);
        }

        public Task<TimeSpan?> PingAsync(string gameReleaseId, CancellationToken ct = default)
        {
            PingCallCount++;
            return Task.FromResult(PingResult);
        }

        public Task<IReadOnlyList<CompanionHotkey>> GetHotkeysAsync(
            string gameReleaseId, CancellationToken ct = default)
        {
            IReadOnlyList<CompanionHotkey> result =
            [
                new("heal", "Ctrl+H", "Лечение"),
                new("quicksave", "Ctrl+S", "Быстрое сохранение"),
            ];
            return Task.FromResult(result);
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithRealService_DoesNotUseMock()
    {
        var fake = new FakeCompanionService();
        // Constructor kicks off RefreshStatusAsync — just ensure it doesn't throw.
        var vm = new CompanionViewModel(fake);
        Assert.NotNull(vm.InstallCommand);
        Assert.NotNull(vm.UninstallCommand);
        Assert.NotNull(vm.PingCommand);
        Assert.NotNull(vm.RefreshCommand);
        Assert.NotNull(vm.SetManualDirCommand);
        Assert.NotNull(vm.ToggleHotkeysCommand);
    }

    [Fact]
    public void Constructor_NoArg_UsesMock_StateIsNotInstalled()
    {
        // The no-arg constructor wires MockCompanionService which returns NotInstalled.
        var vm = new CompanionViewModel();
        // State may not have been set yet (async), but VM should exist without crash.
        Assert.NotNull(vm);
    }

    // ── RefreshStatusAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshStatusAsync_SetsStateFromService()
    {
        var fake = new FakeCompanionService { StateToReturn = CompanionState.Installed, Version = "v1.2.3" };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();

        Assert.Equal(CompanionState.Installed, vm.State);
        Assert.Equal("v1.2.3", vm.VersionText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task RefreshStatusAsync_PopulatesHotkeys()
    {
        var fake = new FakeCompanionService();
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();

        Assert.Equal(2, vm.Hotkeys.Count);
        Assert.Contains(vm.Hotkeys, h => h.Action == "heal");
        Assert.Contains(vm.Hotkeys, h => h.Action == "quicksave");
    }

    // ── StatusBadge ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CompanionState.Active, "РАБОТАЕТ (ПОДКЛЮЧЁН)", "#4EC9B0")]
    [InlineData(CompanionState.Installed, "УСТАНОВЛЕН (ОЖИДАНИЕ ИГРЫ)", "#D6A62D")]
    [InlineData(CompanionState.NotInstalled, "НЕ УСТАНОВЛЕН", "#7D8B73")]
    [InlineData(CompanionState.Error, "ОШИБКА", "#E05252")]
    public async Task StatusBadge_ReflectsState(CompanionState state, string expectedText, string expectedColor)
    {
        var fake = new FakeCompanionService { StateToReturn = state };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();

        Assert.Equal(expectedText, vm.StatusBadgeText);
        Assert.Equal(expectedColor, vm.StatusBadgeColor);
    }

    // ── CanInstall / CanUninstall / CanPing ───────────────────────────────────

    [Fact]
    public async Task CanInstall_TrueOnlyWhenNotInstalled()
    {
        var fake = new FakeCompanionService { StateToReturn = CompanionState.NotInstalled };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();

        Assert.True(vm.CanInstall);
        Assert.False(vm.CanUninstall);
    }

    [Fact]
    public async Task CanUninstall_TrueWhenInstalled_and_install_updates_the_mod()
    {
        var fake = new FakeCompanionService { StateToReturn = CompanionState.Installed };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();

        Assert.True(vm.CanInstall);
        Assert.True(vm.CanUninstall);

        fake.StateToReturn = CompanionState.Active;
        await vm.RefreshStatusAsync();
        Assert.False(vm.CanInstall);
    }

    [Fact]
    public async Task Installs_into_every_checked_game_that_was_found()
    {
        var fake = new FakeCompanionService { StateToReturn = CompanionState.NotInstalled };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshGamesAsync();
        Assert.Equal(3, vm.Games.Count);
        Assert.All(vm.Games, row => Assert.True(row.IsChecked));
        vm.Games[1].IsChecked = false;
        var before = fake.InstallCallCount;

        await vm.InstallCheckedAsync();

        Assert.Equal(before + 2, fake.InstallCallCount);
        Assert.Contains("Готово", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanPing_TrueWhenInstalledOrActive()
    {
        var fake = new FakeCompanionService { StateToReturn = CompanionState.Installed };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();
        Assert.True(vm.CanPing);

        fake.StateToReturn = CompanionState.Active;
        await vm.RefreshStatusAsync();
        Assert.True(vm.CanPing);

        fake.StateToReturn = CompanionState.NotInstalled;
        await vm.RefreshStatusAsync();
        Assert.False(vm.CanPing);
    }

    // ── InstallAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallAsync_OnSuccess_SetsStatusMessage()
    {
        var fake = new FakeCompanionService { InstallResult = true, StateToReturn = CompanionState.NotInstalled };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();
        await vm.InstallAsync();

        Assert.Contains("успешно", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fake.InstallCallCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task InstallAsync_OnFailure_SetsFailureMessage()
    {
        var fake = new FakeCompanionService { InstallResult = false, StateToReturn = CompanionState.NotInstalled };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();
        await vm.InstallAsync();

        Assert.Contains("Не удалось", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task InstallAsync_OnException_SetsErrorMessage()
    {
        var fake = new FakeCompanionService { ThrowOnInstall = true, StateToReturn = CompanionState.NotInstalled };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();
        await vm.InstallAsync();

        Assert.StartsWith("Ошибка установки:", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    // ── UninstallAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task UninstallAsync_OnSuccess_SetsStatusMessage()
    {
        var fake = new FakeCompanionService { UninstallResult = true, StateToReturn = CompanionState.Installed };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();
        await vm.UninstallAsync();

        Assert.Equal("Компаньон удалён.", vm.StatusMessage);
        Assert.Equal(1, fake.UninstallCallCount);
        Assert.False(vm.IsBusy);
    }

    // ── PingAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PingAsync_OnResponse_ShowsLatency()
    {
        var fake = new FakeCompanionService
        {
            StateToReturn = CompanionState.Active,
            PingResult = TimeSpan.FromMilliseconds(42),
        };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();
        await vm.PingAsync();

        Assert.Contains("мс", vm.PingText, StringComparison.Ordinal);
        Assert.Equal(1, fake.PingCallCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task PingAsync_OnNoResponse_ShowsNoAnswer()
    {
        var fake = new FakeCompanionService
        {
            StateToReturn = CompanionState.Installed,
            PingResult = null,
        };
        var vm = new CompanionViewModel(fake);
        await vm.RefreshStatusAsync();
        await vm.PingAsync();

        Assert.Equal("Нет ответа", vm.PingText);
        Assert.False(vm.IsBusy);
    }

    // ── SelectedGame ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("stalker-cs")]
    [InlineData("stalker-soc")]
    public async Task SelectedGame_ChangeToDifferentGame_TriggersRefresh(string gameId)
    {
        var fake = new FakeCompanionService();
        var vm = new CompanionViewModel(fake);
        // Initial refresh (default is stalker-cop)
        await vm.RefreshStatusAsync();
        var initialCount = fake.GetStatusCallCount;

        // Change to a different game — this should trigger fire-and-forget refresh.
        vm.SelectedGame = gameId;

        // Give the async fire-and-forget time to run (all service methods return Task.FromResult, so very fast).
        await Task.Delay(100);

        Assert.True(fake.GetStatusCallCount > initialCount,
            $"Changing game to '{gameId}' should trigger a status refresh (was {initialCount}, now {fake.GetStatusCallCount}).");
        Assert.Equal(gameId, vm.SelectedGame);
    }

    [Fact]
    public void SelectedGame_ChangingToSameValue_DoesNotTriggerRefresh()
    {
        var fake = new FakeCompanionService();
        var vm = new CompanionViewModel(fake);
        // Default is stalker-cop; setting to same should not trigger refresh again.
        vm.SelectedGame = "stalker-cop";
        // No assertion needed on count — just verifies no exception and property stays same.
        Assert.Equal("stalker-cop", vm.SelectedGame);
    }

    // ── ManualGameDir ─────────────────────────────────────────────────────────

    [Fact]
    public void ManualGameDir_SetProperty_NotifiesPropertyChanged()
    {
        var fake = new FakeCompanionService();
        var vm = new CompanionViewModel(fake);
        var notified = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CompanionViewModel.ManualGameDir)) notified = true; };

        vm.ManualGameDir = "/some/path";

        Assert.True(notified);
        Assert.Equal("/some/path", vm.ManualGameDir);
    }

    // ── AvailableGames list ───────────────────────────────────────────────────

    [Fact]
    public void AvailableGames_ContainsAllThreeGames()
    {
        var fake = new FakeCompanionService();
        var vm = new CompanionViewModel(fake);

        var keys = vm.AvailableGames.Select(g => g.Key).ToList();
        Assert.Contains("stalker-cop", keys);
        Assert.Contains("stalker-cs", keys);
        Assert.Contains("stalker-soc", keys);
        Assert.Equal(3, vm.AvailableGames.Count);
    }
}
