using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Backups;
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
    private bool _canRepairQuests;
    private string? _preventingFixId;
    private readonly Func<string> _backupDirectory;

    public SaveDoctorViewModel(Func<string>? backupDirectory = null)
    {
        _backupDirectory = backupDirectory ?? (() => AppPaths.Backups);
        AnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync(), CanAnalyze);
        RepairQuestsCommand = new RelayCommand(async () => await RepairQuestsAsync(), () => CanRepairQuests);
        OpenGameFixCommand = new RelayCommand(() =>
        {
            if (PreventingFixId is { } id) OpenGameFixRequested?.Invoke(id);
        }, () => HasPreventingFix);
    }

    /// <summary>Raised when the user asks to install the Game Fix that prevents a found break.</summary>
    public event Action<string>? OpenGameFixRequested;

    public RelayCommand OpenGameFixCommand { get; private set; } = null!;

    /// <summary>The Game Fix that prevents the first broken quest, when it has one.</summary>
    public string? PreventingFixId
    {
        get => _preventingFixId;
        private set
        {
            if (SetProperty(ref _preventingFixId, value))
            {
                OnPropertyChanged(nameof(HasPreventingFix));
                OpenGameFixCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasPreventingFix => PreventingFixId is not null;

    /// <summary>Raised after a repair replaced the save, so the library can reload it.</summary>
    public event Action<string>? SaveRepaired;

    public ObservableCollection<SaveDoctorCheckRow> Checks { get; } = [];
    public RelayCommand AnalyzeCommand { get; }
    public RelayCommand RepairQuestsCommand { get; }

    public bool CanRepairQuests
    {
        get => _canRepairQuests && !IsAnalyzing;
        private set
        {
            if (SetProperty(ref _canRepairQuests, value)) RepairQuestsCommand.NotifyCanExecuteChanged();
        }
    }

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
                CanRepairQuests = false;
                PreventingFixId = null;
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
            if (!SetProperty(ref _isAnalyzing, value)) return;
            AnalyzeCommand.NotifyCanExecuteChanged();
            RepairQuestsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanRepairQuests));
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
        CanRepairQuests = false;
        PreventingFixId = null;
        var path = SavePath;
        try
        {
            var (report, quests) = await Task.Run(() =>
            {
                var bytes = File.ReadAllBytes(path);
                return (SaveDoctor.Analyze(bytes), QuestDoctor.Analyze(bytes));
            });
            // Another save was selected meanwhile: this report (and its repair button) belongs to the old file.
            if (!string.Equals(path, SavePath, StringComparison.Ordinal)) return;
            if (report.Overview is { } overview)
            {
                Checks.Add(new SaveDoctorCheckRow("✓", L.T("СТРУКТУРНАЯ ПРОВЕРКА"),
                    L.T("ФОРМАТ: {0}; ЗАПИСЕЙ ИНВЕНТАРЯ: {1}.", overview.FormatId, overview.ItemCount),
                    SaveDoctorStatus.Ok));
            }
            foreach (var check in report.Checks.Where(check => check.Id != "structure" || report.Overview is null))
            {
                if (quests.QuestStatesAvailable && check.Id is "quest-state" or "repair") continue;
                var (name, detail) = check.Id switch
                {
                    "structure" => (L.T("СТРУКТУРНАЯ ПРОВЕРКА"), L.T("ФАЙЛ НЕ РАСПОЗНАН ИЛИ ПОВРЕЖДЁН: {0}", check.Detail)),
                    "semantic-state" => (L.T("СЕМАНТИЧЕСКОЕ СОСТОЯНИЕ"), L.T("НЕТ ПОДТВЕРЖДЁННЫХ ПРАВИЛ ДЛЯ КВЕСТОВ И СОСТОЯНИЯ ОБЪЕКТОВ.")),
                    "quest-state" => (L.T("СОСТОЯНИЕ КВЕСТОВ"), L.T("ТЕКУЩИЙ МОДУЛЬ ЧТЕНИЯ НЕ ВЫСТАВЛЯЕТ ПРОВЕРЕННЫЕ СОСТОЯНИЯ ЗАДАНИЙ; СПИСОК НЕ ПОКАЗЫВАЕТСЯ.")),
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
            if (quests.QuestStatesAvailable) AddQuestRows(quests);
            Status = string.Empty;
            HasReport = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (string.Equals(path, SavePath, StringComparison.Ordinal))
            {
                Checks.Clear();
                Status = L.T("Ошибка");
                Checks.Add(new SaveDoctorCheckRow("×", L.T("СТРУКТУРНАЯ ПРОВЕРКА"), exception.Message, SaveDoctorStatus.Error));
                HasReport = true;
            }
        }
        finally
        {
            IsAnalyzing = false;
        }

        // The selection changed while the old file was being read: analyse what is selected now.
        if (!string.Equals(path, SavePath, StringComparison.Ordinal)) await AnalyzeAsync();
    }

    private void AddQuestRows(QuestDoctorReport quests)
    {
        foreach (var state in quests.States)
        {
            var name = state.TaskId switch
            {
                "cs.wolf-dead" => L.T("КВЕСТ: ЗАДАНИЯ ВОЛКА ПОСЛЕ ЕГО СМЕРТИ"),
                "cs.wild-napr-dead" => L.T("КВЕСТ: ЗАДАНИЯ НАПРА НА БАРАХОЛКЕ ПОСЛЕ ЕГО СМЕРТИ"),
                "cs.hog-dead" => L.T("КВЕСТ: СЮЖЕТ НА АРМЕЙСКИХ СКЛАДАХ ПОСЛЕ СМЕРТИ КАБАНА"),
                "soc.mole-dead" => L.T("КВЕСТ: ВСТРЕЧА С КРОТОМ НА АГРОПРОМЕ ПОСЛЕ ЕГО СМЕРТИ"),
                "soc.prisoner-dead" => L.T("КВЕСТ: ПЛЕННЫЙ ДОЛГОВЕЦ В ТЁМНОЙ ДОЛИНЕ ПОСЛЕ ЕГО СМЕРТИ"),
                "soc.courier-dead" => L.T("КВЕСТ: КУРЬЕР СВОБОДЫ ПОСЛЕ ЕГО СМЕРТИ"),
                "soc.informer-dead" => L.T("КВЕСТ: ИНФОРМАТОР СВОБОДЫ ПОСЛЕ ЕГО СМЕРТИ"),
                _ => state.Title ?? state.TaskId,
            };
            var (mark, status, detail) = state.Reason switch
            {
                "dead-without-flag" => ("⚠", SaveDoctorStatus.Warning,
                    L.T("NPC МЁРТВ, НО ФЛАГ {0} НЕ ВЫДАН: ЗАДАНИЕ ЗАВИСНЕТ. ИСПРАВЛЕНИЕ ДОБАВИТ ФЛАГ.", state.MissingInfoPortion ?? string.Empty)),
                "alive" => ("✓", SaveDoctorStatus.Ok, L.T("NPC ЖИВ.")),
                "flag-set" => ("✓", SaveDoctorStatus.Ok, L.T("ФЛАГ УЖЕ ВЫДАН.")),
                "too-late" => ("×", SaveDoctorStatus.Error, L.T("NPC МЁРТВ БЕЗ ФЛАГА, НО СЮЖЕТ УЖЕ ПОШЁЛ ДАЛЬШЕ: ДОБАВЛЕНИЕ ФЛАГА НЕ ПОМОЖЕТ. НУЖЕН БОЛЕЕ РАННИЙ СЕЙВ.")),
                _ => ("?", SaveDoctorStatus.Unknown, L.T("NPC НЕ НАЙДЕН ИЛИ НЕ ЧИТАЕТСЯ; СОСТОЯНИЕ НЕИЗВЕСТНО.")),
            };
            if (state.State == QuestTaskStatus.Broken && state.NeedsPreventingFix)
            {
                detail += " " + L.T("ЧТОБЫ ИГРА УВИДЕЛА ФЛАГ, УСТАНОВИТЕ ИСПРАВЛЕНИЕ ИГРЫ {0}.", state.PreventingFixId ?? string.Empty);
            }
            Checks.Add(new SaveDoctorCheckRow(mark, name, detail, status));
        }

        CanRepairQuests = quests.States.Any(state => state.State == QuestTaskStatus.Broken);
        PreventingFixId = quests.States.FirstOrDefault(state => state.State == QuestTaskStatus.Broken && state.PreventingFixId is not null)?.PreventingFixId;
    }

    public async Task RepairQuestsAsync()
    {
        if (!CanRepairQuests) return;
        var path = SavePath;
        IsAnalyzing = true;
        try
        {
            var receipt = await Task.Run(() =>
            {
                var prepared = QuestDoctor.PrepareRepair(File.ReadAllBytes(path))
                    ?? throw new InvalidOperationException(L.T("НЕТ ПОДТВЕРЖДЁННЫХ СЛОМАННЫХ КВЕСТОВ."));
                return LocalSaveReplacement.ReplaceLocal(path, prepared, _backupDirectory(),
                    readBack => QuestDoctor.VerifyRepair(readBack.Span));
            });
            IsAnalyzing = false;
            await AnalyzeAsync();
            Status = L.T("КВЕСТЫ ИСПРАВЛЕНЫ. Backup: {0}", Path.GetFileName(receipt.BackupPath));
            SaveRepaired?.Invoke(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Status = L.T("Не удалось сохранить: {0}", exception.Message);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private bool CanAnalyze() => !IsAnalyzing && !string.IsNullOrWhiteSpace(SavePath);
}
