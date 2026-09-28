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
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

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
    public void Saves_s2_money_through_backup_and_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "synthetic-s2.sav");
        File.WriteAllBytes(path, ReadFixture("synthetic-s2.sav"));
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory);

        Assert.True(viewModel.AddPreviewSave(path));
        Assert.True(viewModel.CanEditMoney);
        Assert.False(viewModel.CanSave);
        viewModel.MoneyInput = "200";
        Assert.True(viewModel.CanSave);

        viewModel.SaveCommand.Execute(null);

        var saved = Stalker2SaveReader.FromBytes(File.ReadAllBytes(path));
        Assert.True(saved.CrcOk);
        Assert.Equal(200u, saved.Money);
        Assert.False(viewModel.CanSave);
        Assert.Contains("Backup:", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal([path], Directory.GetFiles(saveDirectory));
        Assert.NotEmpty(Directory.GetFiles(backupDirectory));
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

    [Fact]
    public void Mutates_xray_durability_and_saves_with_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "durability.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("writer-durability", "xray-durability-cop-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        var item = Assert.Single(viewModel.SelectedInventory);
        Assert.True(item.CanEditCondition);

        item.ConditionPercent = 75;
        Assert.True(viewModel.CanSave);
        viewModel.SaveCommand.Execute(null);

        var saved = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path));
        var savedItem = Assert.Single(saved.Inventory);
        Assert.NotNull(savedItem.Condition);
        Assert.InRange(savedItem.Condition.Value, 0.70f, 0.80f);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void Mutates_xray_placement_and_saves_with_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "placement.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("writer-placement", "xray-placement-cop-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        var item = viewModel.SelectedInventory.First(i => i.CanEditPlacement && i.Placement == "ruck");
        item.Placement = "slot";

        Assert.True(viewModel.CanSave);
        viewModel.SaveCommand.Execute(null);

        var saved = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path));
        var savedItem = saved.Inventory.First(i => i.Handle == item.Handle);
        Assert.Equal("slot", savedItem.PlacementType);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void Mutates_xray_upgrades_and_saves_with_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "upgrades.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("writer-upgrades", "xray-upgrades-cop-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        var item = viewModel.SelectedInventory.First(i => i.UpgradeItems.Count > 0 && i.CanEditUpgrades);
        var upgrade = item.UpgradeItems.First();
        var targetInstalled = !upgrade.IsInstalled;
        upgrade.IsInstalled = targetInstalled;

        Assert.True(viewModel.CanSave);
        viewModel.SaveCommand.Execute(null);

        var saved = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path));
        var savedItem = saved.Inventory.First(i => i.Handle == item.Handle);
        if (targetInstalled)
        {
            Assert.NotNull(savedItem.Upgrades);
            Assert.Contains(upgrade.Key, savedItem.Upgrades);
        }
        else
        {
            Assert.True(savedItem.Upgrades == null || !savedItem.Upgrades.Contains(upgrade.Key));
        }
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void Mutates_xray_factions_and_saves_with_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "factions.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("writer-factions", "cs-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        Assert.NotEmpty(viewModel.SelectedSave!.FactionRelations);
        var relation = viewModel.SelectedSave.FactionRelations.First(r => r.Goodwill <= 500);
        var originalGoodwill = relation.Goodwill;

        viewModel.AdjustFactionRelation(relation, 100);
        Assert.Equal(originalGoodwill + 100, relation.Goodwill);
        Assert.True(viewModel.CanSave);

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(originalGoodwill + 100, viewModel.SelectedSave.FactionRelations.First(r => r.Community == relation.Community).Goodwill);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void Mutates_xray_stashes_and_saves_with_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "stashes.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("xray-stashes", "xray-stash-cop-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        Assert.NotEmpty(viewModel.SelectedSave!.Stashes);
        var stashItem = viewModel.SelectedSave.Stashes.First().Items.First();

        viewModel.TakeStashItem(stashItem);
        Assert.True(stashItem.IsTaken);
        Assert.True(viewModel.CanSave);

        viewModel.SaveCommand.Execute(null);

        var saved = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path));
        Assert.Contains(saved.Inventory, i => i.Handle == stashItem.Handle);
        Assert.DoesNotContain(saved.Stashes.SelectMany(s => s.Items), i => i.Handle == stashItem.Handle);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void Saves_a_staged_item_addition_through_backup_and_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "add.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("writer-add", "xray-add-cop-ammo-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        viewModel.StageItemAddition("ammo_9x18_fmj", 15);

        Assert.True(viewModel.HasDraftChanges);
        var before = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path)).Inventory.Count(item => item.TypeKey == "ammo_9x18_fmj");
        Assert.True(viewModel.CanSave, viewModel.SaveDisabledReason);
        viewModel.SaveCommand.Execute(null);
        Assert.Contains("Backup:", viewModel.StatusMessage, StringComparison.Ordinal);

        var saved = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path));
        Assert.Equal(before + 1, saved.Inventory.Count(item => item.TypeKey == "ammo_9x18_fmj"));
        Assert.Contains("Backup:", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(backupDirectory));
    }

    [Fact]
    public void Saves_a_staged_item_removal_through_backup_and_readback()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "delete.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("writer-delete", "xray-delete-cop-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        var toRemove = viewModel.SelectedInventory.First(i => viewModel.SelectedSave!.CanRemoveItems);
        viewModel.SelectedItem = toRemove;

        viewModel.RemoveSelectedItemCommand.Execute(null);
        Assert.True(toRemove.IsDeleted);
        Assert.True(viewModel.HasDraftChanges);

        var handle = toRemove.Handle;
        Assert.True(viewModel.CanSave, viewModel.SaveDisabledReason);
        viewModel.SaveCommand.Execute(null);

        var saved = XRayTrilogyReader.FromBytes(File.ReadAllBytes(path));
        Assert.DoesNotContain(saved.Inventory, item => item.Handle == handle);
        Assert.Contains("Backup:", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Supports_draft_undo_redo_and_discard()
    {
        using var directory = new TemporaryDirectory();
        var saveDirectory = Path.Combine(directory.Path, "saves");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "drafts.sav");
        File.WriteAllBytes(path, ReadXRayFixture());

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => backupDirectory,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        var originalMoney = viewModel.MoneyInput;
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);

        viewModel.MoneyInput = "55555";
        Assert.True(viewModel.CanUndo);
        Assert.True(viewModel.CanSave);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal(originalMoney, viewModel.MoneyInput);
        Assert.True(viewModel.CanRedo);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal("55555", viewModel.MoneyInput);

        viewModel.DiscardDraftCommand.Execute(null);
        Assert.Equal(originalMoney, viewModel.MoneyInput);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void Undo_of_an_item_edit_keeps_redo_and_restores_the_item()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "items.sav");
        File.WriteAllBytes(path, ReadXRayFixture());
        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            backupDirectoryProvider: () => Path.Combine(directory.Path, "backups"),
            draftsDirectory: Path.Combine(directory.Path, "drafts"));

        Assert.True(viewModel.AddPreviewSave(path));
        var item = Assert.Single(viewModel.SelectedInventory);
        item.CountInput = "44";
        viewModel.MoneyInput = "777";

        viewModel.UndoCommand.Execute(null);
        Assert.Equal("44", item.CountInput);
        Assert.True(viewModel.CanRedo);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal("30", item.CountInput);
        Assert.False(viewModel.CanUndo);
        Assert.True(viewModel.CanRedo);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal("44", item.CountInput);
        viewModel.RedoCommand.Execute(null);
        Assert.Equal("777", viewModel.MoneyInput);
        Assert.False(viewModel.CanRedo);

        viewModel.SelectedItem = item;
        viewModel.RemoveSelectedItemCommand.Execute(null);
        Assert.Empty(viewModel.FilteredInventory);
        viewModel.UndoCommand.Execute(null);
        Assert.Same(item, Assert.Single(viewModel.FilteredInventory));

        viewModel.DiscardDraftCommand.Execute(null);
        Assert.Equal("30", item.CountInput);
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.HasDraftChanges);
    }

    [Fact]
    public void Guards_unsupported_operations_with_capability_service()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "soc.sav");
        File.WriteAllBytes(path, ReadFixture(Path.Combine("writer-upgrades", "xray-upgrades-soc-unsupported-source.sav")));

        var viewModel = new SaveLibraryViewModel(
            discoverLocalSaves: false,
            draftsDirectory: Path.Combine(directory.Path, "drafts"));
        Assert.True(viewModel.AddPreviewSave(path));

        // In SoC, upgrades are unsupported
        Assert.False(viewModel.SelectedSave!.CanEditUpgrades);
        Assert.NotNull(viewModel.SelectedSave.UpgradesDisabledReason);
        Assert.All(viewModel.SelectedInventory, item => Assert.False(item.CanEditUpgrades));
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
