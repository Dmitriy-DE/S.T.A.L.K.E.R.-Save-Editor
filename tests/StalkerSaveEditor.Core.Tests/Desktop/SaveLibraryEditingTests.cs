using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class SaveLibraryEditingTests
{
    [Fact]
    public void Saves_xray_money_and_stack_changes_through_backup_and_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "slot.sav");
        File.WriteAllBytes(path, ReadXRayFixture());
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory);

        Assert.True(viewModel.AddPreviewSave(path));
        var item = Assert.Single(viewModel.SelectedInventory);
        Assert.Equal("30", item.CountInput);
        Assert.True(viewModel.CanEditMoney);
        Assert.True(item.CanEditCount);

        viewModel.MoneyInput = "9876";
        item.CountInput = "44";

        Assert.True(viewModel.CanSave);
        viewModel.SaveCommand.Execute(null);

        var saved = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path));
        Assert.Equal(9_876u, saved.Money);
        Assert.Equal((ushort?)44, Assert.Single(saved.Inventory).Count);
        Assert.False(viewModel.CanSave);
        Assert.Contains("Backup:", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal([path], Directory.GetFiles(saveDirectory));
        Assert.Equal(3, Directory.GetFiles(backupDirectory).Length);
    }

    [Fact]
    public void Keeps_s2_money_and_stack_fields_read_only_until_its_writer_is_available()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "synthetic-s2.sav");
        File.WriteAllBytes(path, ReadFixture("synthetic-s2.sav"));
        var viewModel = new SaveLibraryViewModel(discoverLocalSaves: false);

        Assert.True(viewModel.AddPreviewSave(path));
        Assert.False(viewModel.CanEditMoney);
        Assert.All(viewModel.SelectedInventory, item => Assert.False(item.CanEditCount));
        Assert.False(viewModel.CanSave);
        Assert.Equal(100u, Stalker2SaveReader.FromBytes(File.ReadAllBytes(path)).Money);
    }

    [Fact]
    public void Discovers_supported_xray_suffixes_and_skips_neighboring_recovery_artifacts()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(directory.Path, "soc.sav"), ReadFixture(Path.Combine("writer-stacks", "xray-stack-soc-source.sav")));
        File.WriteAllBytes(Path.Combine(directory.Path, "cop.scop"), ReadXRayFixture());
        File.WriteAllBytes(Path.Combine(directory.Path, "cs-ee.scs"), ReadFixture(Path.Combine("writer-stacks", "xray-stack-cs-ee-source.sav")));
        File.WriteAllBytes(Path.Combine(directory.Path, "cop_ORIGINAL.sav"), ReadXRayFixture());
        File.WriteAllBytes(Path.Combine(directory.Path, "cop_EDITED.sav"), ReadXRayFixture());
        File.WriteAllText(Path.Combine(directory.Path, "notes.txt"), "not a save");

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            saveDirectoriesProvider: () => [directory.Path]);
        viewModel.Refresh();

        Assert.Equal(3, viewModel.Saves.Count);
        Assert.Contains(viewModel.Saves, save => save.DisplayName == "cop.scop");
        Assert.Contains(viewModel.Saves, save => save.DisplayName == "cs-ee.scs");
        Assert.DoesNotContain(viewModel.Saves, save => save.DisplayName.EndsWith("_ORIGINAL.sav", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Saves, save => save.DisplayName.EndsWith("_EDITED.sav", StringComparison.Ordinal));
    }

    private static byte[] ReadXRayFixture() => ReadFixture(Path.Combine("writer-stacks", "xray-stack-cop-source.sav"));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        name));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"save-editor-ui-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
