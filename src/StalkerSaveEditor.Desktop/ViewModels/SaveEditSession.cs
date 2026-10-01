using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>
/// The write side of the save library, without view state: turns a draft plan (or a relocation) into bytes, replaces
/// the save through the journaled backup + read-back, drops the draft, and reopens the result. Split out of
/// SaveLibraryViewModel, which keeps the UI (selection, status text, sounds).
/// </summary>
internal static class SaveEditSession
{
    /// <summary>Writes <paramref name="plan"/> to the save and returns the receipt and the re-read save.</summary>
    public static (LocalSaveReplacementReceipt Receipt, SaveFileSummary Refreshed) WritePlan(
        SaveFileSummary selected,
        EditPlan plan,
        CatalogBundle? catalog,
        string backupDirectory,
        DraftStore drafts)
    {
        // The write boundary repeats the UI rule: a draft with edits this version cannot read is never written
        // (the visible fields would be saved and the rest silently dropped with the draft).
        if (drafts.Load(selected.SourceSha256) is { CanApplyCurrent: false })
            throw new InvalidOperationException("The draft of this save holds edits this version cannot interpret; discard it first.");
        var source = File.ReadAllBytes(selected.FilePath);
        var prepared = EditService.PrepareEdit(source, plan, selected.ReleaseId, catalog);
        var receipt = LocalSaveReplacement.ReplaceLocal(
            selected.FilePath,
            prepared,
            backupDirectory,
            readBack => EditService.VerifyReadBack(readBack.Span, selected.ReleaseId, plan));

        drafts.Remove(selected.SourceSha256);
        return (receipt, Reopen(selected.FilePath, receipt.OutputSha256));
    }

    /// <summary>Moves the actor to a level-changer destination; read-back must place it there.</summary>
    public static LocalSaveReplacementReceipt Relocate(SaveFileSummary save, XRayRelocationAnchor anchor, string backupDirectory)
    {
        var prepared = XRayRelocation.Prepare(File.ReadAllBytes(save.FilePath), anchor);
        return LocalSaveReplacement.ReplaceLocal(save.FilePath, prepared, backupDirectory, readBack =>
        {
            var location = XRayRelocation.ReadActorLocation(XRayTrilogyReader.FromBytes(readBack.Span));
            if (location.Position != anchor.Position || location.GameVertexId != anchor.GameVertexId)
                throw new InvalidDataException("The written save does not place the actor at the destination.");
        });
    }

    private static SaveFileSummary Reopen(string path, string expectedSha256)
    {
        var refreshed = SaveLibraryLoader.TryReadSave(path);
        if (refreshed is null || !string.Equals(refreshed.SourceSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved file could not be reopened after write verification.");
        }

        return refreshed;
    }
}
