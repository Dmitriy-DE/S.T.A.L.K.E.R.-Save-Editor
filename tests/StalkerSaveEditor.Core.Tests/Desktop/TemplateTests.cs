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
