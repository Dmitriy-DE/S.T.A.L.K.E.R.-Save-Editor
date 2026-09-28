using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed record GameTargetOption(GameTarget Target, string Id, string Title);

public sealed record GameDoctorCheckRow(string Mark, string Name, string Detail, GameDoctorStatus Status);

public sealed class GameDoctorViewModel : ObservableViewModel
{
    private GameTargetOption _selectedTarget;
    private string _gameDirectory = string.Empty;
    private string _status = string.Empty;
    private bool _isAnalyzing;
    private bool _hasReport;
    private Stalker2ModState _s2ModState = Stalker2ModState.InstallationMissing;

    public GameDoctorViewModel()
    {
        Targets = new ObservableCollection<GameTargetOption>(Enum.GetValues<GameTarget>()
            .Select(target =>
            {
                var descriptor = GameTargetCatalog.Get(target);
                return new GameTargetOption(target, descriptor.Id, descriptor.Title);
            }));
        _selectedTarget = Targets[0];
        AnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync(), CanAnalyze);
        DisableS2ModsCommand = new RelayCommand(async () => await ToggleS2ModsAsync(disable: true), CanDisableS2Mods);
        RestoreS2ModsCommand = new RelayCommand(async () => await ToggleS2ModsAsync(disable: false), CanRestoreS2Mods);
    }

    public ObservableCollection<GameTargetOption> Targets { get; }
    public ObservableCollection<GameDoctorCheckRow> Checks { get; } = [];
    public ObservableCollection<string> LooseFiles { get; } = [];
    public RelayCommand AnalyzeCommand { get; }
    public RelayCommand DisableS2ModsCommand { get; }
    public RelayCommand RestoreS2ModsCommand { get; }

    public GameTargetOption SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                InvalidateReport();
                AnalyzeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string GameDirectory
    {
        get => _gameDirectory;
        set
        {
            if (SetProperty(ref _gameDirectory, value))
            {
                InvalidateReport();
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
            if (SetProperty(ref _isAnalyzing, value))
            {
                AnalyzeCommand.NotifyCanExecuteChanged();
                NotifyModActions();
            }
        }
    }

    public bool HasReport
    {
        get => _hasReport;
        private set
        {
            if (SetProperty(ref _hasReport, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowS2ModActions));
                OnPropertyChanged(nameof(ShowDisableS2ModsButton));
                OnPropertyChanged(nameof(ShowRestoreS2ModsButton));
                NotifyModActions();
            }
        }
    }

    public bool ShowEmptyState => !HasReport;

    public async Task AnalyzeAsync()
    {
        if (!CanAnalyze()) return;
        IsAnalyzing = true;
        Status = string.Empty;
        try
        {
            var report = await Task.Run(() => GameDoctor.Analyze(SelectedTarget.Target, GameDirectory));
            Checks.Clear();
            foreach (var check in report.Checks)
            {
                var name = check.Id switch
                {
                    "installation" => L.T("ПАПКА ИГРЫ (РУЧНОЙ ВЫБОР)"),
                    "version" => L.T("ВЕРСИЯ"),
                    "loose-files" => L.T("МОДИФИКАЦИИ"),
                    "companion" => L.T("МОД-КОМПАНЬОН"),
                    "game-fixes" => L.T("ИСПРАВЛЕНИЯ ИГРЫ"),
                    _ => check.Id,
                };
                var mark = check.Status switch
                {
                    GameDoctorStatus.Ok => "✓",
                    GameDoctorStatus.Warning => "⚠",
                    GameDoctorStatus.Error => "×",
                    _ => "?",
                };
                var detail = check.Detail.Length == 0 ? check.Summary : check.Summary + " " + check.Detail;
                Checks.Add(new GameDoctorCheckRow(mark, name, detail, check.Status));
            }
            LooseFiles.Clear();
            foreach (var path in report.LooseFiles) LooseFiles.Add(path);
            _s2ModState = report.Target == GameTarget.Stalker2
                ? Stalker2ModToggle.GetState(report.GameDirectory)
                : Stalker2ModState.InstallationMissing;
            OnPropertyChanged(nameof(S2ModState));
            OnPropertyChanged(nameof(ShowS2ModActions));
            OnPropertyChanged(nameof(ShowDisableS2ModsButton));
            OnPropertyChanged(nameof(ShowRestoreS2ModsButton));
            NotifyModActions();
            HasReport = true;
            Status = string.Join(" · ", report.Checks.Select(check => check.Summary));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Checks.Clear();
            LooseFiles.Clear();
            HasReport = false;
            Status = exception.Message;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    public string S2ModState => _s2ModState.ToString();
    public bool ShowS2ModActions => HasReport && SelectedTarget.Target == GameTarget.Stalker2 &&
        _s2ModState is Stalker2ModState.Enabled or Stalker2ModState.Disabled;
    public bool ShowDisableS2ModsButton => ShowS2ModActions && _s2ModState == Stalker2ModState.Enabled;
    public bool ShowRestoreS2ModsButton => ShowS2ModActions && _s2ModState == Stalker2ModState.Disabled;

    private async Task ToggleS2ModsAsync(bool disable)
    {
        var path = GameDirectory;
        try
        {
            var result = await Task.Run(() => disable ? Stalker2ModToggle.Disable(path) : Stalker2ModToggle.Restore(path));
            await AnalyzeAsync();
            Status = result.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Status = exception.Message;
        }
    }

    private bool CanDisableS2Mods() => !IsAnalyzing && ShowS2ModActions && _s2ModState == Stalker2ModState.Enabled;
    private bool CanRestoreS2Mods() => !IsAnalyzing && ShowS2ModActions && _s2ModState == Stalker2ModState.Disabled;

    private void NotifyModActions()
    {
        DisableS2ModsCommand?.NotifyCanExecuteChanged();
        RestoreS2ModsCommand?.NotifyCanExecuteChanged();
    }

    private void InvalidateReport()
    {
        HasReport = false;
        _s2ModState = Stalker2ModState.InstallationMissing;
        OnPropertyChanged(nameof(S2ModState));
        OnPropertyChanged(nameof(ShowS2ModActions));
        OnPropertyChanged(nameof(ShowDisableS2ModsButton));
        OnPropertyChanged(nameof(ShowRestoreS2ModsButton));
        NotifyModActions();
    }

    private bool CanAnalyze() => !IsAnalyzing && !string.IsNullOrWhiteSpace(GameDirectory);
}
