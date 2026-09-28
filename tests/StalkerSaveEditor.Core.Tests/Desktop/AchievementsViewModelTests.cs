using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Steam;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class AchievementsViewModelTests
{
    private sealed class MockAchievementsAdapter : ISteamAchievementsAdapter
    {
        public bool Available { get; set; } = true;
        public string? Message { get; set; } = "Steam подключён.";
        public List<SteamAchievement> Items { get; } = new();

        public int SetCallCount { get; private set; }
        public int LastSetAppId { get; private set; }
        public string? LastSetApiName { get; private set; }
        public bool LastSetAchieved { get; private set; }

        public bool IsAvailable(int appId) => Available;

        public string? GetAvailabilityMessage(int appId) => Message;

        public Task<IReadOnlyList<SteamAchievement>> ListAsync(int appId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SteamAchievement>>(Items);
        }

        public Task<SteamAchievement> SetAsync(int appId, string apiName, bool achieved, CancellationToken cancellationToken = default)
        {
            SetCallCount++;
            LastSetAppId = appId;
            LastSetApiName = apiName;
            LastSetAchieved = achieved;
            var existing = Items.FirstOrDefault(i => i.ApiName == apiName);
            var updated = new SteamAchievement(
                apiName,
                existing?.Name ?? apiName,
                existing?.Description ?? string.Empty,
                achieved,
                achieved ? 1727481600u : 0u,
                existing?.Hidden ?? false);
            return Task.FromResult(updated);
        }
    }

    [Fact]
    public async Task Lists_achievements_and_calculates_progress()
    {
        var mock = new MockAchievementsAdapter();
        mock.Items.Add(new SteamAchievement("ACH_FIRST", "Первый шаг", "Начать игру", true, 1727481600u, false));
        mock.Items.Add(new SteamAchievement("ACH_STALKER", "Бывалый", "Завершить 10 квестов", false, 0, false));
        mock.Items.Add(new SteamAchievement("ACH_LEGEND", "Легенда Зоны", "Собрать все артефакты", false, 0, true));

        var vm = new AchievementsViewModel(mock);
        await vm.RefreshAsync();

        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(1, vm.UnlockedCount);
        Assert.Equal(33, (int)vm.ProgressPercentage);
        Assert.Contains("1 из 3 получено (33%)", vm.ProgressText);

        var first = vm.FilteredAchievements.First();
        Assert.Equal("Первый шаг", first.Name);
        Assert.True(first.IsAchieved);
        Assert.Contains("Получено:", first.StatusText);
        Assert.Equal("🏆", first.IconText);
    }

    [Fact]
    public async Task Filters_achievements_by_name_and_description()
    {
        var mock = new MockAchievementsAdapter();
        mock.Items.Add(new SteamAchievement("ACH_1", "Охотник на мутантов", "Уничтожить 50 мутантов", false, 0, false));
        mock.Items.Add(new SteamAchievement("ACH_2", "Дипломат", "Подружиться со всеми фракциями", false, 0, false));

        var vm = new AchievementsViewModel(mock);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.FilteredAchievements.Count);

        vm.SearchText = "мутант";
        Assert.Single(vm.FilteredAchievements);
        Assert.Equal("Охотник на мутантов", vm.FilteredAchievements[0].Name);

        vm.SearchText = "фракци";
        Assert.Single(vm.FilteredAchievements);
        Assert.Equal("Дипломат", vm.FilteredAchievements[0].Name);

        vm.SearchText = string.Empty;
        Assert.Equal(2, vm.FilteredAchievements.Count);
    }

    [Fact]
    public async Task Requires_explicit_confirmation_before_toggling_achievement()
    {
        var mock = new MockAchievementsAdapter();
        mock.Items.Add(new SteamAchievement("ACH_1", "Первопроходец", "Пройти Кордон", false, 0, false));

        var vm = new AchievementsViewModel(mock);
        await vm.RefreshAsync();
        var item = vm.FilteredAchievements.First();

        Assert.False(vm.ShowConfirmDialog);
        Assert.False(vm.CanConfirmToggle);

        // Click toggle to request unlocking
        vm.RequestToggleCommand.Execute(item);

        Assert.True(vm.ShowConfirmDialog);
        Assert.Same(item, vm.PendingAchievement);
        Assert.True(vm.PendingNewState);
        Assert.Contains("разблокировать", vm.ConfirmDialogTitle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Первопроходец", vm.ConfirmDialogMessage);
        Assert.True(vm.CanConfirmToggle);
        Assert.Equal(0, mock.SetCallCount); // Not mutated yet

        // Cancel
        vm.CancelToggleCommand.Execute(null);
        Assert.False(vm.ShowConfirmDialog);
        Assert.Null(vm.PendingAchievement);
        Assert.Equal(0, mock.SetCallCount);
    }

    [Fact]
    public async Task Sets_achievement_state_on_confirmation()
    {
        var mock = new MockAchievementsAdapter();
        mock.Items.Add(new SteamAchievement("ACH_1", "Первопроходец", "Пройти Кордон", false, 0, false));

        var vm = new AchievementsViewModel(mock);
        await vm.RefreshAsync();
        var item = vm.FilteredAchievements.First();

        vm.RequestToggleCommand.Execute(item);
        await vm.ConfirmToggleAsync();

        Assert.Equal(1, mock.SetCallCount);
        Assert.Equal(41700, mock.LastSetAppId);
        Assert.Equal("ACH_1", mock.LastSetApiName);
        Assert.True(mock.LastSetAchieved);

        Assert.True(item.IsAchieved);
        Assert.True(item.UnlockTime > 0);
        Assert.Equal(1, vm.UnlockedCount);
        Assert.Contains("получено в Steam", vm.StatusMessage);
    }

    [Fact]
    public async Task Handles_steam_offline_cleanly()
    {
        var mock = new MockAchievementsAdapter
        {
            Available = false,
            Message = "Steam не запущен."
        };

        var vm = new AchievementsViewModel(mock);
        await vm.RefreshAsync();

        Assert.False(vm.IsAvailable);
        Assert.Equal("Steam не запущен.", vm.AvailabilityMessage);
    }
}
