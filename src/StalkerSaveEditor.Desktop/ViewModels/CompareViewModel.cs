using StalkerSaveEditor.Desktop.Services;
using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Inspection;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>Something the selected save can be compared with: another save of the same game or one of its backups.</summary>
public sealed record CompareCandidate(string Title, string Path)
{
    public override string ToString() => Title;
}

/// <summary>One row of the comparison: what, before (the other file), after (the selected save).</summary>
public sealed record CompareRow(string Label, string Before, string After);

/// <summary>
/// Overview → «Сравнить»: read-only difference between the selected save and another file
/// (money, actor facts, item totals by type — handles are reassigned by the game, so items are not matched by id).
/// </summary>
public sealed class CompareViewModel : ObservableViewModel
{
    private readonly Func<string, CatalogBundle?> _catalog;
    private string? _currentPath;
    private string _currentReleaseId = string.Empty;
    private CompareCandidate? _selected;
    private string _status = string.Empty;

    public CompareViewModel(Func<string, CatalogBundle?> catalog) => _catalog = catalog;

    public ObservableCollection<CompareCandidate> Candidates { get; } = [];
    public ObservableCollection<CompareRow> Rows { get; } = [];
    public bool HasCandidates => Candidates.Count > 0;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public CompareCandidate? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value)) Run();
        }
    }

    public void SetSubject(string? path, string releaseId, IEnumerable<CompareCandidate> candidates)
    {
        _currentPath = path;
        _currentReleaseId = releaseId;
        Candidates.Clear();
        foreach (var candidate in candidates.Where(candidate => candidate.Path != path)) Candidates.Add(candidate);
        _selected = null;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasCandidates));
        Rows.Clear();
        Status = Candidates.Count == 0 ? L.T("Нет других сейвов этой игры или бэкапов для сравнения.") : L.T("Выберите, с чем сравнить.");
    }

    public void Run()
    {
        Rows.Clear();
        if (_currentPath is null || _selected is null) return;
        try
        {
            var catalog = _catalog(_currentReleaseId);
            var before = SaveInspector.Inspect(File.ReadAllBytes(_selected.Path), catalog);
            var after = SaveInspector.Inspect(File.ReadAllBytes(_currentPath), catalog);
            if (before.ReleaseId != after.ReleaseId)
            {
                Status = L.T("Это сейвы разных игр.");
                return;
            }

            foreach (var difference in SaveComparer.Compare(before, after))
            {
                Rows.Add(difference.Kind == "task"
                    ? new CompareRow(L.T("Задание: {0}", difference.Label), TaskState(difference.Before), TaskState(difference.After))
                    : new CompareRow(Label(difference), difference.Before ?? "—", difference.After ?? "—"));
            }

            Status = Rows.Count == 0 ? L.T("Различий нет.") : L.T("Различий: {0}. Слева — «{1}», справа — выбранный сейв.", Rows.Count, _selected.Title);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Status = L.T("Не удалось прочитать: ") + exception.Message;
        }
    }

    private static string TaskState(string? state) => state switch
    {
        null => L.T("нет"),
        "InProgress" => L.T("выполняется"),
        "Completed" => L.T("выполнено"),
        "Failed" => L.T("провалено"),
        "Skipped" => L.T("пропущено"),
        _ => "—",
    };

    private static string Label(SaveDifference difference) => difference.Kind switch
    {
        "money" => L.T("Деньги"),
        "health" => L.T("Здоровье"),
        "rank" => L.T("Ранг"),
        "reputation" => L.T("Репутация"),
        _ => difference.Label,
    };
}
