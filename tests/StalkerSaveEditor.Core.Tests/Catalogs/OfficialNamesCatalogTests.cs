using StalkerSaveEditor.Core.Catalogs;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Catalogs;

public sealed class OfficialNamesCatalogTests
{
    [Fact]
    public void Resolves_release_scoped_translations_and_language_fallbacks()
    {
        var names = OfficialNamesCatalog.LoadEmbedded();

        Assert.Equal("АКМ-74/2", names.Resolve("stalker-cs-ee", "items", "wpn_ak74", "ru"));
        Assert.Equal("9x39 mm SP-5", names.Resolve("stalker-cop", "items", "ammo_9x39_pab9", "pt_BR"));
        Assert.Equal("Free Stalker", names.Resolve("stalker-cop", "factions", "actor", "unknown"));
        Assert.Null(names.Resolve("stalker2", "items", "wpn_ak74", "ru"));
        Assert.Null(names.Resolve("stalker-soc", "items", "missing_key", "ru"));
        Assert.Null(names.Resolve("stalker-cop", "items", null, "ru"));
    }

    [Theory]
    [InlineData("stalker-soc-ee", "soc")]
    [InlineData("stalker-cs-ee", "clear_sky")]
    [InlineData("stalker-cop-ee", "cop")]
    [InlineData("Shadow of Chernobyl", "soc")]
    [InlineData("Call of Pripyat", "cop")]
    [InlineData("stalker2", null)]
    [InlineData("unknown-game", null)]
    public void Maps_only_supported_trilogy_release_names(string releaseId, string? family)
    {
        Assert.Equal(family, OfficialNamesCatalog.ReleaseFamily(releaseId));
    }
}
