using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class CloudViewModel : ObservableViewModel
{
    private readonly ICloudServiceAdapter _cloudService;
    private readonly Func<string> _backupDirectoryProvider;
    private readonly Func<IReadOnlyList<string>> _localSaveFilesProvider;
    private readonly Action<string>? _onSaveDownloaded;

    private int _selectedAppId = 0; // 0 = All
    private CloudSaveItemViewModel? _selectedCloudSave;
    private bool _isLoading;
    private bool _isWriting;
    private string _statusMessage = string.Empty;
    private CloudWriteStatus? _lastWriteStatus;
    private string? _lastWriteReason;
    private string? _lastBackupPath;

    // Explicit confirmation dialog state
    private bool _showWriteConfirmDialog;
    private bool _writeConfirmationChecked;

    public CloudViewModel(
        ICloudServiceAdapter? cloudService = null,
        Func<string>? backupDirectoryProvider = null,
        Func<IReadOnlyList<string>>? localSaveFilesProvider = null,
        Action<string>? onSaveDownloaded = null)
    {
        _cloudService = cloudService ?? new CloudServiceAdapter();
        _backupDirectoryProvider = backupDirectoryProvider ?? (() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StalkerSaveEditor", "backups"));
        _localSaveFilesProvider = localSaveFilesProvider ?? (() => Array.Empty<string>());
        _onSaveDownloaded = onSaveDownloaded;

        CloudSaves = new ObservableCollection<CloudSaveItemViewModel>();

        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        DownloadSelectedCommand = new RelayCommand(async () => await DownloadSelectedAsync(), () => CanDownload);
        RequestWriteCommand = new RelayCommand(RequestWrite, () => CanRequestWrite);
        ConfirmWriteCommand = new RelayCommand(async () => await ConfirmWriteAsync(), () => CanConfirmWrite);
        CancelWriteCommand = new RelayCommand(CancelWrite);
    }

    public ObservableCollection<CloudSaveItemViewModel> CloudSaves { get; }

    public bool IsSteamAvailable => _cloudService.IsSteamAvailable;

    public string SteamStatusMessage => _cloudService.SteamStatusMessage
        ?? (IsSteamAvailable ? "Steam доступен" : "Steam не запущен или недоступен.");

    public int SelectedAppId
    {
        get => _selectedAppId;
        set
        {
            if (SetProperty(ref _selectedAppId, value))
            {
                _ = RefreshAsync();
            }
        }
    }

    public CloudSaveItemViewModel? SelectedCloudSave
    {
        get => _selectedCloudSave;
        set
        {
            if (SetProperty(ref _selectedCloudSave, value))
            {
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanRequestWrite));
                DownloadSelectedCommand.NotifyCanExecuteChanged();
                RequestWriteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsWriting
    {
        get => _isWriting;
        set
        {
            if (SetProperty(ref _isWriting, value))
            {
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanRequestWrite));
                OnPropertyChanged(nameof(CanConfirmWrite));
                DownloadSelectedCommand.NotifyCanExecuteChanged();
                RequestWriteCommand.NotifyCanExecuteChanged();
                ConfirmWriteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public CloudWriteStatus? LastWriteStatus
    {
        get => _lastWriteStatus;
        set
        {
            if (SetProperty(ref _lastWriteStatus, value))
            {
                OnPropertyChanged(nameof(IsWriteVerified));
                OnPropertyChanged(nameof(IsWriteUncertain));
                OnPropertyChanged(nameof(IsWriteAborted));
            }
        }
    }

    public bool IsWriteVerified => LastWriteStatus == CloudWriteStatus.Verified;
    public bool IsWriteUncertain => LastWriteStatus == CloudWriteStatus.Uncertain;
    public bool IsWriteAborted => LastWriteStatus == CloudWriteStatus.Aborted;

    public string? LastWriteReason
    {
        get => _lastWriteReason;
        set => SetProperty(ref _lastWriteReason, value);
    }

    public string? LastBackupPath
    {
        get => _lastBackupPath;
        set => SetProperty(ref _lastBackupPath, value);
    }

    public bool ShowWriteConfirmDialog
    {
        get => _showWriteConfirmDialog;
        set => SetProperty(ref _showWriteConfirmDialog, value);
    }

    public bool WriteConfirmationChecked
    {
        get => _writeConfirmationChecked;
        set
        {
            if (SetProperty(ref _writeConfirmationChecked, value))
            {
                OnPropertyChanged(nameof(CanConfirmWrite));
                ConfirmWriteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanDownload => SelectedCloudSave is not null && !IsLoading && !IsWriting;

    public bool CanRequestWrite => SelectedCloudSave is not null &&
                                  SelectedCloudSave.HasLocalFile &&
                                  !IsLoading &&
                                  !IsWriting &&
                                  IsSteamAvailable &&
                                  SelectedCloudSave.AppId != 1643320; // S2 cloud write is safety gated

    public bool CanConfirmWrite => ShowWriteConfirmDialog && WriteConfirmationChecked && !IsWriting;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand DownloadSelectedCommand { get; }
    public RelayCommand RequestWriteCommand { get; }
    public RelayCommand ConfirmWriteCommand { get; }
    public RelayCommand CancelWriteCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Запрос списка сохранений Steam Cloud...";

        try
        {
            CloudSaves.Clear();
            var localPaths = _localSaveFilesProvider();
            var appIds = SelectedAppId == 0
                ? new[] { 4500, 20510, 41700, 1643320 }
                : new[] { SelectedAppId };

            foreach (var appId in appIds)
            {
                var files = await _cloudService.ListCloudFilesAsync(appId, localPaths);
                foreach (var f in files)
                {
                    CloudSaves.Add(new CloudSaveItemViewModel(f));
                }
            }

            StatusMessage = CloudSaves.Count == 0
                ? "В Steam Cloud не найдено сохранений для выбранных игр."
                : $"Найдено {CloudSaves.Count} сохранений в облаке.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки списка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsSteamAvailable));
            OnPropertyChanged(nameof(SteamStatusMessage));
        }
    }

    public async Task DownloadSelectedAsync()
    {
        var selected = SelectedCloudSave;
        if (selected is null || IsWriting) return;

        IsWriting = true;
        StatusMessage = $"Скачивание {selected.FileName} из Steam Cloud...";

        try
        {
            var bytes = await _cloudService.ReadCloudFileAsync(selected.AppId, selected.RemotePath);
            var backupDir = _backupDirectoryProvider();
            Directory.CreateDirectory(backupDir);

            // Determine target download location
            string targetPath;
            if (!string.IsNullOrEmpty(selected.Model.LocalFilePath))
            {
                targetPath = selected.Model.LocalFilePath;
            }
            else
            {
                var fallbackDir = Path.Combine(backupDir, "cloud_downloads");
                Directory.CreateDirectory(fallbackDir);
                targetPath = Path.Combine(fallbackDir, selected.FileName);
            }

            // Create safety backup of existing local file before overwriting
            if (File.Exists(targetPath))
            {
                var safetyBackup = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(targetPath)}_precloud_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                File.Copy(targetPath, safetyBackup, overwrite: true);
                LastBackupPath = safetyBackup;
            }

            await File.WriteAllBytesAsync(targetPath, bytes);
            StatusMessage = $"Файл {selected.FileName} успешно скачан: {targetPath}";
            _onSaveDownloaded?.Invoke(targetPath);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка скачивания: {ex.Message}";
        }
        finally
        {
            IsWriting = false;
        }
    }

    public void RequestWrite()
    {
        if (!CanRequestWrite) return;
        WriteConfirmationChecked = false;
        ShowWriteConfirmDialog = true;
    }

    public void CancelWrite()
    {
        ShowWriteConfirmDialog = false;
        WriteConfirmationChecked = false;
    }

    public async Task ConfirmWriteAsync()
    {
        var selected = SelectedCloudSave;
        if (selected is null || !WriteConfirmationChecked || !selected.HasLocalFile)
        {
            ShowWriteConfirmDialog = false;
            return;
        }

        ShowWriteConfirmDialog = false;
        IsWriting = true;
        StatusMessage = $"Запись {selected.FileName} в Steam Cloud (RemoteStorage)...";

        try
        {
            var localPath = selected.Model.LocalFilePath!;
            var data = await File.ReadAllBytesAsync(localPath);
            var backupDir = _backupDirectoryProvider();

            var result = await _cloudService.WriteCloudFileAsync(
                selected.AppId,
                selected.RemotePath,
                data,
                backupDir);

            LastWriteStatus = result.Status;
            LastWriteReason = result.Message;
            LastBackupPath = result.BackupPath;

            var writeMessage = result.Status == CloudWriteStatus.Verified
                ? $"Verified: {result.Message}"
                : (result.Status == CloudWriteStatus.Uncertain
                    ? $"Uncertain: {result.Message}"
                    : $"Aborted: {result.Message}");

            await RefreshAsync();
            StatusMessage = writeMessage;
        }
        catch (Exception ex)
        {
            LastWriteStatus = CloudWriteStatus.Aborted;
            LastWriteReason = ex.Message;
            StatusMessage = $"Ошибка записи: {ex.Message}";
        }
        finally
        {
            IsWriting = false;
        }
    }
}
