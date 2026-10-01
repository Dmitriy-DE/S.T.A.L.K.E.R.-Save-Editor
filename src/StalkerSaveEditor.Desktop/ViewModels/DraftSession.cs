using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>
/// The draft of the open save: its undo/redo journal and the on-disk draft store. Every change is persisted at once,
/// so a draft survives a restart. Split out of SaveLibraryViewModel, which only applies the current plan to the rows.
/// </summary>
internal sealed class DraftSession(DraftStore store)
{
    public DraftStore Store { get; } = store;

    public DraftJournal? Journal { get; private set; }

    public EditPlan? Current => Journal?.Current;

    public bool CanUndo => Journal?.CanUndo == true;

    public bool CanRedo => Journal?.CanRedo == true;

    /// <summary>The current step holds edits written by another version that this one cannot interpret: no editing, no saving.</summary>
    public bool HasUnsupportedEdits => Journal?.CurrentHasUnmappedEdits == true;

    /// <summary>Number of recorded steps (0 = nothing changed).</summary>
    public int Steps => Journal?.Index ?? 0;

    /// <summary>Opens the saved draft of <paramref name="sourceSha256"/>; returns its plan, or null for a fresh draft.</summary>
    public EditPlan? Open(string sourceSha256)
    {
        var existing = Store.Load(sourceSha256);
        Journal = existing ?? Fresh(sourceSha256);
        return existing?.Current;
    }

    public void Close() => Journal = null;

    /// <summary>Records <paramref name="plan"/>; false when it holds the same edits as the current step.</summary>
    public bool Record(string sourceSha256, EditPlan plan)
    {
        var journal = Journal ?? Fresh(sourceSha256);
        if (journal.CurrentHasUnmappedEdits) return false;
        if (journal.Current.HasSameEdits(plan)) return false;
        Journal = journal.Record(plan);
        Store.Save(Journal);
        return true;
    }

    public EditPlan? Undo() => CanUndo ? Move(Journal!.Undo()) : null;

    public EditPlan? Redo() => CanRedo ? Move(Journal!.Redo()) : null;

    /// <summary>Drops the draft of <paramref name="sourceSha256"/> and returns the empty plan to show.</summary>
    public EditPlan Discard(string sourceSha256)
    {
        if (Journal?.UnmappedLegacyPlans.Any(plan => plan.HasValue) == true) Store.SetAside(sourceSha256);
        else Store.Remove(sourceSha256);
        Journal = Fresh(sourceSha256);
        return Journal.Current;
    }

    private EditPlan Move(DraftJournal journal)
    {
        Journal = journal;
        Store.Save(journal);
        return journal.Current;
    }

    private static DraftJournal Fresh(string sourceSha256) => new([new EditPlan(sourceSha256)], 0);
}
