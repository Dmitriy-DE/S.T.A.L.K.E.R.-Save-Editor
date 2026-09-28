using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Storage;

public sealed class SaveSlotDiscoveryLinkTests
{
    [Fact]
    public void Lists_a_save_once_when_its_folder_is_reachable_through_a_link()
    {
        var root = Path.Combine(Path.GetTempPath(), "se-links-" + Guid.NewGuid().ToString("N"));
        var real = Path.Combine(root, "AppData", "Local", "savedgames");
        Directory.CreateDirectory(real);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xray-call-of-pripyat.sav"), Path.Combine(real, "quick.scop"));
        var linkParent = Path.Combine(root, "Local Settings");
        Directory.CreateDirectory(linkParent);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(linkParent, "Application Data"), Path.Combine(root, "AppData", "Local"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return; // Creating links needs privileges on some Windows runners.
            }

            var result = SaveSlotDiscovery.Discover(
            [
                new SaveDirectoryCandidate("cop", "stalker-cop", real),
                new SaveDirectoryCandidate("cop", "stalker-cop", Path.Combine(linkParent, "Application Data", "savedgames")),
            ]);

            var slot = Assert.Single(result.Slots);
            Assert.Equal("stalker-cop", slot.FormatId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Explains_why_a_file_is_not_readable()
    {
        var root = Path.Combine(Path.GetTempPath(), "se-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "broken.sav"), [1, 2, 3, 4]);
            var slot = Assert.Single(SaveSlotDiscovery.Discover([new SaveDirectoryCandidate("cop", "stalker-cop", root)]).Slots);
            Assert.Null(slot.FormatId);
            Assert.False(string.IsNullOrWhiteSpace(slot.DetectionError));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
