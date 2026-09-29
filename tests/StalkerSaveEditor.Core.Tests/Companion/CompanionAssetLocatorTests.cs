using StalkerSaveEditor.Core.Companion;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class CompanionAssetLocatorTests
{
    [Fact]
    public void Finds_payload_next_to_packaged_executable_directory()
    {
        var root = Directory.CreateTempSubdirectory("companion-assets-");
        try
        {
            var appDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "app")).FullName;
            var payload = Directory.CreateDirectory(Path.Combine(appDirectory, "mods", "companion")).FullName;

            Assert.Equal(payload, CompanionAssetLocator.ResolveSourceRoot(appDirectory));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Finds_payload_in_an_ancestor_development_checkout()
    {
        var root = Directory.CreateTempSubdirectory("companion-checkout-");
        try
        {
            var executableDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "App", "bin", "Release")).FullName;
            var payload = Directory.CreateDirectory(Path.Combine(root.FullName, "mods", "companion")).FullName;

            Assert.Equal(payload, CompanionAssetLocator.ResolveSourceRoot(executableDirectory));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
