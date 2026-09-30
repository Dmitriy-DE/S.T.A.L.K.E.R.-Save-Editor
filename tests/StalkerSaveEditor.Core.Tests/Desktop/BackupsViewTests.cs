using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Desktop.ViewModels;
using StalkerSaveEditor.Desktop.Views;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[Collection(AvaloniaViewTestGroup.Name)]
public sealed class BackupsViewTests
{
    [Fact]
    public void Backup_history_and_detail_panel_bind_to_the_selected_record()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory.Path);
        var verified = Backup(directory.Path, "verified.sav", BackupVerificationStatus.Verified);
        var corrupt = Backup(directory.Path, "corrupt.sav", BackupVerificationStatus.Corrupt);
        viewModel.Backups.Add(verified);
        viewModel.Backups.Add(corrupt);

        var selectedBackup = typeof(SaveLibraryViewModel).GetProperty("SelectedBackup");
        Assert.NotNull(selectedBackup);
        selectedBackup.SetValue(viewModel, verified);

        var view = BackupsView.Build(viewModel);
        var list = view.GetLogicalDescendants().OfType<ListBox>()
            .Single(control => control.Name == "backup-history-list");
        Assert.Same(verified, list.SelectedItem);
        var details = view.GetLogicalDescendants().OfType<Border>()
            .Single(border => border.Name == "backup-details");
        Assert.True(details.IsVisible);
        var detailText = details.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains("verified.sav", detailText);
        Assert.Contains(verified.SourceSha256, detailText);

        var restoreInPlace = view.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "backup-restore-in-place");
        var restoreCopy = view.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "backup-restore-copy");
        Assert.True(restoreInPlace.IsEnabled);
        Assert.True(restoreCopy.IsEnabled);

        list.SelectedItem = corrupt;
        Assert.Same(corrupt, selectedBackup.GetValue(viewModel));

        Assert.Contains(corrupt.Error!, details.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text));
        Assert.False(restoreInPlace.IsEnabled);
        Assert.False(restoreCopy.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(restoreInPlace)?.ToString()));
    }

    [Fact]
    public void Empty_backup_history_explains_where_backups_come_from()
    {
        using var directory = new TemporaryDirectory();
        var view = BackupsView.Build(CreateViewModel(directory.Path));

        var emptyState = view.GetLogicalDescendants().OfType<StackPanel>()
            .Single(panel => panel.Name == "backup-empty-state");
        Assert.True(emptyState.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(emptyState.GetLogicalDescendants().OfType<TextBlock>().Last().Text));
    }

    [Fact]
    public void Backup_rows_follow_their_current_data_context()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory.Path);
        var first = Backup(directory.Path, "first.sav", BackupVerificationStatus.Verified);
        var second = Backup(directory.Path, "selected.sav", BackupVerificationStatus.Corrupt);
        viewModel.Backups.Add(first);

        var view = BackupsView.Build(viewModel);
        var list = view.GetLogicalDescendants().OfType<ItemsControl>()
            .Single(control => control.ItemTemplate is FuncDataTemplate<BackupRecordViewModel>);
        var template = Assert.IsType<FuncDataTemplate<BackupRecordViewModel>>(list.ItemTemplate);
        var row = template.Build(first) ?? throw new InvalidOperationException("Backup row template returned no control.");
        row.DataContext = second;

        var rowText = row.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains("selected.sav", rowText);
        Assert.Contains(second.StatusDisplay, rowText);
        Assert.DoesNotContain("first.sav", rowText);
        Assert.All(row.GetLogicalDescendants().OfType<Button>(), button => Assert.False(button.IsEnabled));
    }

    [Fact]
    public void Verified_export_backup_allows_restore_to_copy_but_not_in_place()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreateViewModel(directory.Path);
        var exported = Backup(directory.Path, "exported.sav", BackupVerificationStatus.Verified, mode: "export");
        viewModel.Backups.Add(exported);
        typeof(SaveLibraryViewModel).GetProperty("SelectedBackup")!.SetValue(viewModel, exported);

        var view = BackupsView.Build(viewModel);
        var restoreInPlace = view.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "backup-restore-in-place");
        var restoreCopy = view.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "backup-restore-copy");
        var disabledReason = view.GetLogicalDescendants().OfType<TextBlock>()
            .Single(text => text.Name == "backup-restore-disabled-reason");

        Assert.False(restoreInPlace.IsEnabled);
        Assert.True(restoreCopy.IsEnabled);
        Assert.True(disabledReason.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(disabledReason.Text));
        Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(restoreInPlace)?.ToString()));
    }

    [Fact]
    public void Verified_replacement_backup_requires_its_output_path_to_match_the_source()
    {
        using var directory = new TemporaryDirectory();
        var backup = Backup(
            directory.Path,
            "mismatched.sav",
            BackupVerificationStatus.Verified,
            outputMatchesSource: false);

        Assert.False(backup.CanRestoreInPlace);
        Assert.True(backup.CanRestoreCopy);
        Assert.False(string.IsNullOrWhiteSpace(backup.RestoreInPlaceDisabledReason));
    }

    private static BackupRecordViewModel Backup(
        string directory,
        string sourceName,
        BackupVerificationStatus status,
        string mode = "replace",
        bool outputMatchesSource = true)
    {
        var sourcePath = Path.Combine(directory, sourceName);
        var outputPath = outputMatchesSource && (mode is "replace" or "restore")
            ? sourcePath
            : Path.Combine(directory, sourceName + "_OUTPUT.sav");
        var operation = JsonDocument.Parse($"{{\"mode\":\"{mode}\"}}").RootElement.Clone();
        return new BackupRecordViewModel(new LocalSaveBackupRecord(
            Path.Combine(directory, sourceName + ".json"),
            Path.Combine(directory, sourceName + "_ORIGINAL.sav"),
            "2026-09-30T10:00:00Z",
            sourcePath,
            new string('a', 64),
            outputPath,
            new string('b', 64),
            operation,
            status,
            Error: status == BackupVerificationStatus.Verified ? null : "Journal check failed."));
    }

    private static SaveLibraryViewModel CreateViewModel(string directory) => new(
        discoverLocalSaves: false,
        saveDirectoriesProvider: () => [],
        backupDirectoryProvider: () => Path.Combine(directory, "backups"),
        draftsDirectory: Path.Combine(directory, "drafts"));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stalker-backups-view-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
