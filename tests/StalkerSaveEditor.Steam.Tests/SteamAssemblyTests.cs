using System.Reflection;
using Xunit;

namespace StalkerSaveEditor.Steam.Tests;

public sealed class SteamAssemblyTests
{
    [Fact]
    public void Steam_project_has_the_expected_assembly_name()
    {
        var assembly = Assembly.Load("StalkerSaveEditor.Steam");

        Assert.Equal("StalkerSaveEditor.Steam", assembly.GetName().Name);
    }
}
