using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Storage;

public sealed class SaveSlotDiscoveryTests
{
    [Fact]
    public void Sorts_slots_newest_first_and_marks_unrecognized_files_without_guessing_from_folder()
    {
        using var directory = new TemporaryDirectory();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        var xray = Path.Combine(saves, "older.scop");
        var unknown = Path.Combine(saves, "newer.scs");
        File.WriteAllBytes(xray, ReadXRayFixture());
        File.WriteAllBytes(unknown, "not a save"u8.ToArray());
        File.SetLastWriteTimeUtc(xray, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(unknown, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var candidate = new SaveDirectoryCandidate("stalker2", "stalker2", saves);

        var discovery = SaveSlotDiscovery.Discover([candidate]);

        Assert.Equal([unknown, xray], discovery.Slots.Select(slot => slot.Path));
        Assert.Null(discovery.Slots[0].FormatId);
        Assert.Null(discovery.Slots[0].GameId);
        Assert.Equal("stalker-cop", discovery.Slots[1].FormatId);
        Assert.Equal("cop", discovery.Slots[1].GameId);
        Assert.Equal([saves], discovery.SearchedPaths);
    }

    [Fact]
    public void Includes_supported_foreign_extensions_but_excludes_sidecars_and_stalker2_metadata()
    {
        using var directory = new TemporaryDirectory();
        var saves = Directory.CreateDirectory(Path.Combine(directory.Path, "saves")).FullName;
        foreach (var name in new[] { "slot.scop", "slot.scs", "thumb.dds", "meta.info", "campaignssave.sav", "analyticsdata.sav" })
        {
            File.WriteAllBytes(Path.Combine(saves, name), [1, 2, 3]);
        }

        var result = SaveSlotDiscovery.Discover([
            new SaveDirectoryCandidate("soc", "stalker-soc", saves),
        ]);

        Assert.Equal(["slot.scop", "slot.scs"], result.Slots.Select(slot => Path.GetFileName(slot.Path)).Order(StringComparer.Ordinal));
    }

    private static byte[] ReadXRayFixture() => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "writer-stacks", "xray-stack-cop-source.sav"));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"slot-discovery-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
