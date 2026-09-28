using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed record SaveDoctorCheckRow(string Mark, string Name, string Detail, SaveDoctorStatus Status);

public sealed class SaveDoctorViewModel : ObservableViewModel
{
    private string _savePath = string.Empty;
    private string _status = string.Empty;
    private bool _isAnalyzing;
    private bool _hasReport;

    public SaveDoctorViewModel() => AnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync(), CanAnalyze);

    public ObservableCollection<SaveDoctorCheckRow> Checks { get; } = [];
    public RelayCommand AnalyzeCommand { get; }

    public string SavePath
    {
        get => _savePath;
        set
        {
            if (SetProperty(ref _savePath, value))
            {
                Status = string.Empty;
                Checks.Clear();
                HasReport = false;
                AnalyzeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (SetProperty(ref _isAnalyzing, value)) AnalyzeCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasReport
    {
        get => _hasReport;
        private set
        {
            if (SetProperty(ref _hasReport, value)) OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    public bool ShowEmptyState => !HasReport;

    public async Task AnalyzeAsync()
    {
        if (!CanAnalyze()) return;
        IsAnalyzing = true;
        Checks.Clear();
        Status = string.Empty;
        try
        {
            var path = SavePath;
            var report = await Task.Run(() => SaveDoctor.Analyze(File.ReadAllBytes(path)));
            if (report.Overview is { } overview)
            {
                Checks.Add(new SaveDoctorCheckRow("✓", L.T("СТРУКТУРНАЯ ПРОВЕРКА"),
                    L.T("ФОРМАТ: {0}; ЗАПИСЕЙ ИНВЕНТАРЯ: {1}.", overview.FormatId, overview.ItemCount),
                    SaveDoctorStatus.Ok));
            }
            foreach (var check in report.Checks.Where(check => check.Id != "structure" || report.Overview is null))
            {
                var (name, detail) = check.Id switch
                {
                    "structure" => (L.T("СТРУКТУРНАЯ ПРОВЕРКА"), L.T("ФАЙЛ НЕ РАСПОЗНАН ИЛИ ПОВРЕЖДЁН: {0}", check.Detail)),
                    "semantic-state" => (L.T("СЕМАНТИЧЕСКОЕ СОСТОЯНИЕ"), L.T("НЕТ ПОДТВЕРЖДЁННЫХ ПРАВИЛ ДЛЯ КВЕСТОВ И СОСТОЯНИЯ ОБЪЕКТОВ.")),
                    "repair" => (L.T("РЕМОНТ СОХРАНЕНИЯ"), L.T("НЕТ ВАЛИДИРОВАННОГО РЕМОНТА; ФАЙЛ НЕ МЕНЯЛСЯ.")),
                    _ => (check.Id, check.Detail),
                };
                var mark = check.Status switch
                {
                    SaveDoctorStatus.Ok => "✓",
                    SaveDoctorStatus.Warning => "⚠",
                    SaveDoctorStatus.Error => "×",
                    _ => "?",
                };
                Checks.Add(new SaveDoctorCheckRow(mark, name, detail, check.Status));
            }
            Status = string.Empty;
            HasReport = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Checks.Clear();
            Status = L.T("Ошибка");
            Checks.Add(new SaveDoctorCheckRow("×", L.T("СТРУКТУРНАЯ ПРОВЕРКА"), exception.Message, SaveDoctorStatus.Error));
            HasReport = true;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private bool CanAnalyze() => !IsAnalyzing && !string.IsNullOrWhiteSpace(SavePath);
}
