using StalkerSaveEditor.Desktop.Services;
using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>One row of «Проверить окружение».</summary>
public sealed record EnvironmentCheckRow(string Status, string Title, string Detail, string Hint)
{
    public bool HasHint => Hint.Length > 0;
}

/// <summary>
/// Settings → «Диагностика»: Environment Doctor, the crash left by the previous run and the
/// support bundle (redacted log + crash + environment) the user can attach to an issue.
/// </summary>
public sealed class DiagnosticsViewModel : ObservableViewModel
{
    private readonly Func<IReadOnlyList<EnvironmentCheck>> _runChecks;
    private string? _pendingCrash;
    private string _status = string.Empty;
    private bool _isChecking;
    private IReadOnlyList<EnvironmentCheck> _lastChecks = [];

    private readonly Func<byte[], Task<string>>? _upload;
    private readonly Action<DateTime>? _onSent;
    private readonly Func<DateTime?> _lastSent;

    public DiagnosticsViewModel(
        Func<IReadOnlyList<EnvironmentCheck>>? runChecks = null,
        string? pendingCrash = null,
        Func<byte[], Task<string>>? upload = null,
        Action<DateTime>? onSent = null,
        Func<DateTime?>? lastSent = null)
    {
        _runChecks = runChecks ?? (() => EnvironmentDoctor.Run());
        _pendingCrash = pendingCrash;
        _upload = upload;
        _onSent = onSent;
        _lastSent = lastSent ?? (() => null);
        RunChecksCommand = new RelayCommand(async () => await RunChecksAsync(), () => !IsChecking);
        DismissCrashCommand = new RelayCommand(DismissCrash);
        SendNowCommand = new RelayCommand(async () => await SendAsync(automatic: false), () => _upload is not null);
    }

    public RelayCommand SendNowCommand { get; }

    private byte[] Bundle(DateTime? since = null) => DiagnosticsBundle.Create(
        _lastChecks.Count == 0 ? null : EnvironmentDoctor.Format(_lastChecks),
        DiagnosticsUploader.Stalker2ModLogs(),
        since);

    /// <summary>Uploads the redacted bundle; the automatic daily report is silent and carries only what the log gained since the last one.</summary>
    public async Task<string?> SendAsync(bool automatic)
    {
        if (_upload is null) return null;
        try
        {
            var id = await _upload(Bundle(automatic ? _lastSent() : null));
            _onSent?.Invoke(DateTime.UtcNow);
            AppLog.Info("report sent " + id);
            if (!automatic) Status = L.T("Отчёт отправлен, номер: {0}", id);
            return id;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
        {
            AppLog.Warn("report not sent", exception);
            if (!automatic) Status = L.T("Отчёт не отправлен: {0}", exception.Message);
            return null;
        }
    }

    public ObservableCollection<EnvironmentCheckRow> Checks { get; } = [];
    public RelayCommand RunChecksCommand { get; }
    public RelayCommand DismissCrashCommand { get; }

    public string? PendingCrash => _pendingCrash;
    public bool HasPendingCrash => _pendingCrash is not null;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value)) RunChecksCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task RunChecksAsync()
    {
        IsChecking = true;
        Status = L.T("Проверяю…");
        try
        {
            _lastChecks = await Task.Run(_runChecks);
            Checks.Clear();
            foreach (var check in _lastChecks) Checks.Add(ToRow(check));
            var failed = _lastChecks.Count(check => check.Status == CheckStatus.Fail);
            var warned = _lastChecks.Count(check => check.Status == CheckStatus.Warn);
            Status = failed + warned == 0
                ? L.T("Всё в порядке.")
                : L.T("Ошибок: {0}, предупреждений: {1}.", failed, warned);
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Writes the support bundle; returns the path or null (status explains why).</summary>
    public string? ExportBundle(string destination)
    {
        try
        {
            if (Directory.Exists(destination)) throw new IOException(L.T("Указана папка, а не файл."));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            File.WriteAllBytes(destination, Bundle());
            var path = destination;
            Status = L.T("Отчёт сохранён: ") + path;
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = L.T("Не удалось сохранить отчёт: ") + exception.Message;
            return null;
        }
    }

    public void DismissCrash()
    {
        CrashReporter.Dismiss();
        _pendingCrash = null;
        OnPropertyChanged(nameof(PendingCrash));
        OnPropertyChanged(nameof(HasPendingCrash));
    }

    internal static EnvironmentCheckRow ToRow(EnvironmentCheck check) => new(
        check.Status switch { CheckStatus.Ok => "OK", CheckStatus.Warn => "!", _ => "×" },
        $"{check.Group} · {check.Name}",
        check.Detail,
        check.Hint);
}
