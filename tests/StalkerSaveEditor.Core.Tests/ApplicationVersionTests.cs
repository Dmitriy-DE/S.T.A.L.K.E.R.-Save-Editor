using StalkerSaveEditor.Core;
using Xunit;

namespace StalkerSaveEditor.Core.Tests;

public sealed class ApplicationVersionTests
{
    [Fact]
    public void Current_is_the_configured_semantic_version()
    {
        Assert.Equal("1.0.0-rc.1", ApplicationVersion.Current);
    }
}
