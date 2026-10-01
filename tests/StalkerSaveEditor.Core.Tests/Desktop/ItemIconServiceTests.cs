using StalkerSaveEditor.Desktop.Services;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class ItemIconServiceTests
{
    [Fact]
    public void Missing_icons_do_not_pile_up_without_limit()
    {
        var prefix = "no_such_item_" + Guid.NewGuid().ToString("N") + "_";
        for (var index = 0; index < ItemIconService.MaximumCachedKeys * 3; index++)
        {
            Assert.Null(ItemIconService.Load("stalker-cop", prefix + index));
        }

        Assert.InRange(ItemIconService.CachedKeyCount, 0, ItemIconService.MaximumCachedKeys + 1);
    }
}
