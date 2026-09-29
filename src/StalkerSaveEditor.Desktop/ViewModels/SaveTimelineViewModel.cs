using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed record SaveTimelineEntry(SaveFileSummary Save, SaveFileSummary? Previous)
{
    public string Title => $"{Save.ReleaseName} · {Save.DisplayName}";
    public string Details => $"{Save.LastModifiedDisplay} · {Save.MoneyDisplay} · {Save.SlotTitle}";
}

/// <summary>Chronological rows built only from the save files already discovered by the library.</summary>
public sealed class SaveTimelineViewModel(Action<SaveFileSummary, SaveFileSummary>? compareAdjacent = null)
    : ObservableViewModel
{
    private readonly Action<SaveFileSummary, SaveFileSummary>? _compareAdjacent = compareAdjacent;

    public ObservableCollection<SaveTimelineEntry> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    public bool CanCompare(SaveTimelineEntry? entry) =>
        entry?.Previous is not null && _compareAdjacent is not null;

    public void SetSaves(IEnumerable<SaveFileSummary> saves)
    {
        ArgumentNullException.ThrowIfNull(saves);
        Entries.Clear();
        var ordered = saves
            .OrderBy(save => save.LastModified ?? DateTime.MaxValue)
            .ThenBy(save => save.ReleaseId, StringComparer.Ordinal)
            .ThenBy(save => save.FilePath, StringComparer.Ordinal)
            .ToArray();
        var timestampCounts = ordered
            .Where(save => save.LastModified.HasValue)
            .GroupBy(save => (save.ReleaseId, save.LastModified!.Value))
            .ToDictionary(group => group.Key, group => group.Count());
        var priorByRelease = new Dictionary<string, SaveFileSummary>(StringComparer.Ordinal);
        foreach (var save in ordered)
        {
            priorByRelease.TryGetValue(save.ReleaseId, out var previous);
            // A tie or missing timestamp has no proven temporal order, so do not offer an
            // adjacent comparison that could imply a sequence the file metadata cannot prove.
            if (previous?.LastModified is not { } previousTime || save.LastModified is not { } currentTime ||
                previousTime >= currentTime ||
                timestampCounts[(save.ReleaseId, previousTime)] != 1 ||
                timestampCounts[(save.ReleaseId, currentTime)] != 1)
            {
                previous = null;
            }

            Entries.Add(new SaveTimelineEntry(save, previous));
            priorByRelease[save.ReleaseId] = save;
        }

        OnPropertyChanged(nameof(HasEntries));
    }

    public void CompareAdjacent(SaveTimelineEntry? entry)
    {
        if (entry?.Previous is { } previous) _compareAdjacent?.Invoke(entry.Save, previous);
    }
}
