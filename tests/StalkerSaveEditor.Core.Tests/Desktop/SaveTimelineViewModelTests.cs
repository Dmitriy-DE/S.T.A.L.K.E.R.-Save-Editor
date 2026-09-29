using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class SaveTimelineViewModelTests
{
    [Fact]
    public void Orders_real_save_summaries_by_time_and_exposes_only_same_release_predecessors()
    {
        var early = Summary("soc-early.sav", "stalker-soc", "Shadow of Chernobyl", new DateTime(2026, 1, 1, 10, 0, 0));
        var late = Summary("soc-late.sav", "stalker-soc", "Shadow of Chernobyl", new DateTime(2026, 1, 1, 12, 0, 0));
        var other = Summary("cop.sav", "stalker-cop", "Call of Pripyat", new DateTime(2026, 1, 1, 11, 0, 0));
        var viewModel = new SaveTimelineViewModel();

        viewModel.SetSaves([late, other, early]);

        Assert.Equal([early, other, late], viewModel.Entries.Select(entry => entry.Save));
        Assert.Null(viewModel.Entries[0].Previous);
        Assert.Null(viewModel.Entries[1].Previous);
        Assert.Same(early, viewModel.Entries[2].Previous);
    }

    [Fact]
    public void Does_not_claim_adjacency_when_file_timestamps_are_tied_or_missing()
    {
        var tiedA = Summary("soc-a.sav", "stalker-soc", "Shadow of Chernobyl", new DateTime(2026, 1, 1, 10, 0, 0));
        var tiedB = Summary("soc-b.sav", "stalker-soc", "Shadow of Chernobyl", new DateTime(2026, 1, 1, 10, 0, 0));
        var later = Summary("soc-later.sav", "stalker-soc", "Shadow of Chernobyl", new DateTime(2026, 1, 1, 11, 0, 0));
        var unknown = Summary("soc-unknown.sav", "stalker-soc", "Shadow of Chernobyl", null);
        var viewModel = new SaveTimelineViewModel();

        viewModel.SetSaves([unknown, later, tiedB, tiedA]);

        Assert.All(viewModel.Entries, entry => Assert.Null(entry.Previous));
    }

    private static SaveFileSummary Summary(string name, string releaseId, string releaseName, DateTime? modified) => new(
        Path.Combine(Path.GetTempPath(), name),
        releaseName,
        releaseId,
        new string('a', 64),
        0,
        false,
        [],
        lastModified: modified);
}
