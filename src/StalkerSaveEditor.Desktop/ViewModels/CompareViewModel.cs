using System.Collections.ObjectModel;
using System.Globalization;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Inspection;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>Another save or backup that can be compared with the selected save.</summary>
public sealed record CompareCandidate(string Title, string Path, bool IsBackup = false)
{
    public override string ToString() => Title;
}

/// <summary>One difference between the comparison files, before display-side ordering.</summary>
public sealed record CompareRow(string Label, string Before, string After)
{
    public string CategoryId { get; init; } = "other";
    public string CategoryDisplay { get; init; } = string.Empty;
    public string ChangeTypeId { get; init; } = "changed";
    public string ChangeTypeDisplay { get; init; } = string.Empty;
}

/// <summary>A filtered difference with its values ordered for the visible A and B columns.</summary>
public sealed record CompareDisplayRow(
    string Label,
    string ValueA,
    string ValueB,
    string CategoryId,
    string CategoryDisplay,
    string ChangeTypeId,
    string ChangeTypeDisplay);

public sealed record CompareFilterOption(
    string Id,
    string Title,
    int Count,
    bool IsSelected = false)
{
    public string CountDisplay => Count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Read-only differences exposed by the current save inspection contract.</summary>
public sealed class CompareViewModel : ObservableViewModel
{
    private readonly Func<string, CatalogBundle?> _catalog;
    private string? _currentPath;
    private string _currentReleaseId = string.Empty;
    private CompareCandidate? _subject;
    private CompareCandidate? _selected;
    private string _status = string.Empty;
    private string _categoryFilterId = "all";
    private string _changeTypeFilterId = "all";
    private string _searchQuery = string.Empty;
    private bool _isSwapped;
    private string _actionStatus = string.Empty;

    public CompareViewModel(Func<string, CatalogBundle?> catalog)
    {
        _catalog = catalog;
        SelectCategoryCommand = new RelayCommand<string>(id => CategoryFilterId = id ?? "all");
        SelectChangeTypeCommand = new RelayCommand<string>(id => ChangeTypeFilterId = id ?? "all");
    }

    public ObservableCollection<CompareCandidate> Candidates { get; } = [];
    public ObservableCollection<CompareRow> Rows { get; } = [];
    public ObservableCollection<CompareDisplayRow> VisibleRows { get; } = [];
    public ObservableCollection<CompareFilterOption> CategoryFilters { get; } = [];
    public ObservableCollection<CompareFilterOption> ChangeTypeFilters { get; } = [];

    public RelayCommand<string> SelectCategoryCommand { get; }
    public RelayCommand<string> SelectChangeTypeCommand { get; }

    public bool HasCandidates => Candidates.Count > 0;
    public bool CanSwapSides => Subject is not null && Selected is not null;
    public bool HasRows => Rows.Count > 0;
    public bool HasVisibleRows => VisibleRows.Count > 0;
    public bool CanCopyOrExport => HasVisibleRows;
    public int AddedCount => Rows.Count(row => row.ChangeTypeId == "added");
    public int RemovedCount => Rows.Count(row => row.ChangeTypeId == "removed");
    public int ChangedCount => Rows.Count(row => row.ChangeTypeId == "changed");
    public int DifferenceCount => Rows.Count;
    public CompareCandidate? Subject => _subject;
    public CompareCandidate? SideA => IsSwapped ? Subject : Selected;
    public CompareCandidate? SideB => IsSwapped ? Selected : Subject;
    public string SideASourceDisplay => SideA is null ? "—" : SideA.IsBackup ? L.T("Резервная копия") : L.T("Локальный файл");
    public string SideBSourceDisplay => SideB is null ? "—" : SideB.IsBackup ? L.T("Резервная копия") : L.T("Локальный файл");

    public string ActionStatus
    {
        get => _actionStatus;
        private set => SetProperty(ref _actionStatus, value);
    }

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
            if (!SetProperty(ref _selected, value)) return;
            Run();
            NotifySideProperties();
            OnPropertyChanged(nameof(CanSwapSides));
        }
    }

    public string CategoryFilterId
    {
        get => _categoryFilterId;
        set
        {
            var selectedId = CategoryFilters.Any(option => option.Id == value) ? value : "all";
            if (!SetProperty(ref _categoryFilterId, selectedId)) return;
            RefreshFilterSelectionStates();
            RefreshVisibleRows();
        }
    }

    public string ChangeTypeFilterId
    {
        get => _changeTypeFilterId;
        set
        {
            var selectedId = ChangeTypeFilters.Any(option => option.Id == value) ? value : "all";
            if (!SetProperty(ref _changeTypeFilterId, selectedId)) return;
            RefreshFilterSelectionStates();
            RefreshVisibleRows();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value)) RefreshVisibleRows();
        }
    }

    public bool IsSwapped
    {
        get => _isSwapped;
        private set
        {
            if (!SetProperty(ref _isSwapped, value)) return;
            NotifySideProperties();
            RefreshVisibleRows();
        }
    }

    public void SetSubject(
        string? path,
        string releaseId,
        IEnumerable<CompareCandidate> candidates,
        string? title = null)
    {
        _currentPath = path;
        _currentReleaseId = releaseId;
        _subject = path is null ? null : new CompareCandidate(title ?? Path.GetFileName(path), path);
        OnPropertyChanged(nameof(Subject));
        Candidates.Clear();
        foreach (var candidate in candidates.Where(candidate => candidate.Path != path)) Candidates.Add(candidate);
        _selected = null;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CanSwapSides));
        Rows.Clear();
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(RemovedCount));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(DifferenceCount));
        Status = Candidates.Count == 0
            ? L.T("Нет других сейвов этой игры или бэкапов для сравнения.")
            : L.T("Выберите, с чем сравнить.");
        RefreshFilterOptions();
        RefreshVisibleRows();
        NotifySideProperties();
        IsSwapped = false;
    }

    public void SwapSides() => IsSwapped = !IsSwapped;

    public void Run()
    {
        Rows.Clear();
        ActionStatus = string.Empty;
        if (_currentPath is null || _selected is null)
        {
            Status = Candidates.Count == 0
                ? L.T("Нет других сейвов этой игры или бэкапов для сравнения.")
                : L.T("Выберите, с чем сравнить.");
            RefreshComparisonState();
            return;
        }

        try
        {
            var catalog = _catalog(_currentReleaseId);
            var before = SaveInspector.Inspect(File.ReadAllBytes(_selected.Path), catalog);
            var after = SaveInspector.Inspect(File.ReadAllBytes(_currentPath), catalog);
            if (before.ReleaseId != after.ReleaseId)
            {
                Status = L.T("Это сейвы разных игр.");
                RefreshComparisonState();
                return;
            }

            foreach (var difference in SaveComparer.Compare(before, after))
            {
                var (categoryId, categoryDisplay) = Category(difference.Kind);
                var changeTypeId = difference.Before is null ? "added" : difference.After is null ? "removed" : "changed";
                var label = difference.Kind == "task"
                    ? L.T("Задание: {0}", difference.Label)
                    : Label(difference);
                var oldValue = difference.Kind == "task" ? TaskState(difference.Before) : difference.Before ?? "—";
                var newValue = difference.Kind == "task" ? TaskState(difference.After) : difference.After ?? "—";
                Rows.Add(new CompareRow(label, oldValue, newValue)
                {
                    CategoryId = categoryId,
                    CategoryDisplay = categoryDisplay,
                    ChangeTypeId = changeTypeId,
                    ChangeTypeDisplay = ChangeType(changeTypeId),
                });
            }

            Status = Rows.Count == 0
                ? L.T("Различий нет.")
                : L.T("Различий: {0}. Показаны только данные, доступные для сравнения.", Rows.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Status = L.T("Не удалось прочитать: ") + exception.Message;
        }

        RefreshComparisonState();
    }

    private void RefreshComparisonState()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(RemovedCount));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(DifferenceCount));
        RefreshFilterOptions();
        RefreshVisibleRows();
    }

    private void RefreshFilterOptions()
    {
        var categories = new List<CompareFilterOption>();
        var changeTypes = new List<CompareFilterOption>();
        if (Rows.Count > 0)
        {
            categories.Add(new CompareFilterOption("all", L.T("Все различия"), Rows.Count));
            categories.AddRange(Rows
                .GroupBy(row => new { row.CategoryId, row.CategoryDisplay })
                .Select(group => new CompareFilterOption(group.Key.CategoryId, group.Key.CategoryDisplay, group.Count())));
            changeTypes.Add(new CompareFilterOption("all", L.T("Все"), Rows.Count));
            changeTypes.AddRange(Rows
                .GroupBy(row => new { row.ChangeTypeId, row.ChangeTypeDisplay })
                .Select(group => new CompareFilterOption(group.Key.ChangeTypeId, group.Key.ChangeTypeDisplay, group.Count())));
        }

        ReplaceFilterOptions(CategoryFilters, categories);
        ReplaceFilterOptions(ChangeTypeFilters, changeTypes);

        if (!CategoryFilters.Any(option => option.Id == _categoryFilterId))
        {
            _categoryFilterId = "all";
            OnPropertyChanged(nameof(CategoryFilterId));
        }

        if (!ChangeTypeFilters.Any(option => option.Id == _changeTypeFilterId))
        {
            _changeTypeFilterId = "all";
            OnPropertyChanged(nameof(ChangeTypeFilterId));
        }

        RefreshFilterSelectionStates();
    }

    private static void ReplaceFilterOptions(
        ObservableCollection<CompareFilterOption> target,
        IEnumerable<CompareFilterOption> options)
    {
        target.Clear();
        foreach (var option in options) target.Add(option);
    }

    private void RefreshFilterSelectionStates()
    {
        for (var index = 0; index < CategoryFilters.Count; index++)
        {
            var option = CategoryFilters[index];
            CategoryFilters[index] = option with { IsSelected = option.Id == CategoryFilterId };
        }

        for (var index = 0; index < ChangeTypeFilters.Count; index++)
        {
            var option = ChangeTypeFilters[index];
            ChangeTypeFilters[index] = option with { IsSelected = option.Id == ChangeTypeFilterId };
        }
    }

    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        var query = SearchQuery.Trim();
        foreach (var row in Rows)
        {
            if (CategoryFilterId != "all" && row.CategoryId != CategoryFilterId) continue;
            if (ChangeTypeFilterId != "all" && row.ChangeTypeId != ChangeTypeFilterId) continue;

            var valueA = IsSwapped ? row.After : row.Before;
            var valueB = IsSwapped ? row.Before : row.After;
            if (query.Length > 0 && !MatchesSearch(row, valueA, valueB, query)) continue;

            VisibleRows.Add(new CompareDisplayRow(
                row.Label,
                valueA,
                valueB,
                row.CategoryId,
                row.CategoryDisplay,
                row.ChangeTypeId,
                row.ChangeTypeDisplay));
        }

        OnPropertyChanged(nameof(HasVisibleRows));
        OnPropertyChanged(nameof(CanCopyOrExport));
    }

    public string BuildCopyListTsv() => BuildDelimitedText('\t', csv: false);

    public string BuildExportCsv() => BuildDelimitedText(',', csv: true);

    public void ReportActionFailure(string message) => ActionStatus = message;

    private string BuildDelimitedText(char delimiter, bool csv)
    {
        var headers = new[]
        {
            L.T("ТИП"),
            L.T("ПАРАМЕТР / ОБЪЕКТ"),
            L.T("ЗНАЧЕНИЕ A"),
            L.T("ЗНАЧЕНИЕ B"),
            L.T("КАТЕГОРИЯ"),
        };
        var lines = new List<string> { string.Join(delimiter, headers.Select(value => EncodeField(value, delimiter, csv))) };
        lines.AddRange(VisibleRows.Select(row => string.Join(delimiter, new[]
        {
            row.ChangeTypeDisplay,
            row.Label,
            row.ValueA,
            row.ValueB,
            row.CategoryDisplay,
        }.Select(value => EncodeField(value, delimiter, csv)))));
        return string.Join("\r\n", lines);
    }

    private static string EncodeField(string value, char delimiter, bool csv)
    {
        if (!csv)
        {
            return value.Replace('\t', ' ').Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        return value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static bool MatchesSearch(CompareRow row, string valueA, string valueB, string query) =>
        row.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        valueA.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        valueB.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        row.CategoryDisplay.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        row.ChangeTypeDisplay.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private void NotifySideProperties()
    {
        OnPropertyChanged(nameof(SideA));
        OnPropertyChanged(nameof(SideB));
        OnPropertyChanged(nameof(SideASourceDisplay));
        OnPropertyChanged(nameof(SideBSourceDisplay));
    }

    private static (string Id, string Display) Category(string kind) => kind switch
    {
        "item" => ("items", L.T("Предметы")),
        "money" or "health" or "rank" or "reputation" => ("character", L.T("Персонаж")),
        "task" => ("tasks", L.T("Задания")),
        _ => ("other", L.T("Другое")),
    };

    private static string ChangeType(string id) => id switch
    {
        "added" => L.T("Добавлено"),
        "removed" => L.T("Удалено"),
        _ => L.T("Изменено"),
    };

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
