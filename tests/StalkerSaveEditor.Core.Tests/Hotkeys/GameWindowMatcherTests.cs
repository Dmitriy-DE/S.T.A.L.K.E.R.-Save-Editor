using StalkerSaveEditor.Core.Hotkeys;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Hotkeys;

public sealed class GameWindowMatcherTests
{
    [Theory]
    [InlineData("XR_3DA", true)]
    [InlineData("xrEngine", true)]
    [InlineData("xrengine.exe", true)]
    [InlineData("steam_app_41700", true)]
    [InlineData("firefox", false)]
    [InlineData("code", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_x_ray_game_windows_hold_the_hotkeys(string? name, bool game) =>
        Assert.Equal(game, GameWindowMatcher.IsGame(name));
}
