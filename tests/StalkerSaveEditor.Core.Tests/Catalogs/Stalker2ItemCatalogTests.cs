using StalkerSaveEditor.Core.Catalogs;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Catalogs;

public sealed class Stalker2ItemCatalogTests
{
    private static readonly Stalker2ItemCatalog Catalog = Stalker2ItemCatalog.LoadEmbedded();

    [Fact]
    public void Embedded_catalog_has_official_names_and_icons()
    {
        Assert.True(Catalog.Count > 1000);
        Assert.Equal("12/76 мм жекан", Catalog.Name("A012A"));
        Assert.Equal("12x76mm Slug", Catalog.Name("A012A", "en"));
        Assert.Equal("s2/A012A.png", Catalog.Icon("A012A"));
        Assert.NotNull(Catalog.Description("A012A"));
    }

    [Fact]
    public void Non_russian_ui_falls_back_to_english_not_cyrillic()
    {
        Assert.Equal("12x76mm Slug", Catalog.Name("A012A", "de"));
    }

    [Theory]
    [InlineData("a012a")]
    [InlineData("A012A_Player")]
    public void Canonicalizes_save_sids(string sid)
    {
        Assert.Equal("A012A", Catalog.CanonicalSid(sid));
    }

    [Fact]
    public void Unknown_sid_has_no_name_but_family_icon_fallback_works()
    {
        Assert.Null(Catalog.Name("NoSuchItem_12345"));
        Assert.Null(Catalog.Icon("NoSuchItem_12345"));
        Assert.Equal("s2/KozimkovPDA.png", Catalog.Icon("SomeUnknownQuest_PDA_Garpia"));
    }

    [Fact]
    public void Rejects_a_payload_without_items()
    {
        Assert.Throws<CatalogBundleException>(() => Stalker2ItemCatalog.Load("{\"schema_version\":2}"u8));
    }
}
