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
}
