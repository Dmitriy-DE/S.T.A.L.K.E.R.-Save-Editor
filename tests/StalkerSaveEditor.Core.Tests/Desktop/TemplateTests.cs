using Avalonia.Controls;
using StalkerSaveEditor.Desktop.Styles;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class TemplateTests
{
    [Fact]
    public void Item_templates_survive_the_null_content_of_a_removed_row()
    {
        var template = StalkerTheme.Template<string>(text => new TextBlock { Text = text.ToUpperInvariant() });

        Assert.IsType<Panel>(template.Build(null));
        Assert.Equal("A", Assert.IsType<TextBlock>(template.Build("a")).Text);
    }

    [Fact]
    public void Binding_only_rows_recycle_and_never_see_the_item_while_being_built()
    {
        var template = StalkerTheme.RecyclingTemplate<string>(() => new TextBlock { [!TextBlock.TextProperty] = new Avalonia.Data.Binding() });

        var first = Assert.IsType<TextBlock>(template.Build("a"));
        var recycling = Assert.IsAssignableFrom<Avalonia.Controls.Templates.IRecyclingDataTemplate>(template);

        Assert.Same(first, recycling.Build("b", first));
        Assert.IsType<TextBlock>(template.Build(null));
    }
}

public sealed class ItemIconAliasTests
{
    [Fact]
    public void Shared_pictures_resolve_to_one_shipped_file()
    {
        // Quest PDAs share one picture in the game; the alias map points the other names at the shipped file.
        var key = StalkerSaveEditor.Desktop.Services.ItemIconService.IconKey("stalker-cop", "zat_b40_pda_1");

        Assert.NotNull(key);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", key!.Replace('/', Path.DirectorySeparatorChar))), key);
        Assert.NotEqual("xray/zat_b40_pda_1.png", key);
    }
}
