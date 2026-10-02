using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class CloudViewModel : ObservableViewModel
{
    private readonly ICloudServiceAdapter _cloudService;
    private readonly Func<string> _backupDirectoryProvider;
    private readonly Func<IReadOnlyList<LocalSaveReference>> _localSaveFilesProvider;
    private CloudWriteIntent? _writeIntent;
    private readonly Action<string>? _onSaveDownloaded;

    private int _selectedAppId; // 0 = All
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
        Func<IReadOnlyList<LocalSaveReference>>? localSaveFilesProvider = null,
        Action<string>? onSaveDownloaded = null)
    {
        _cloudService = cloudService ?? new CloudServiceAdapter();
        _backupDirectoryProvider = backupDirectoryProvider ?? (() => StalkerSaveEditor.Core.Diagnostics.AppPaths.Backups);
        _localSaveFilesProvider = localSaveFilesProvider ?? (() => Array.Empty<LocalSaveReference>());
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
        ?? (IsSteamAvailable ? L.T("Steam доступен") : L.T("Steam не запущен или недоступен."));

    public int SelectedAppId
    {
        get => _selectedAppId;
        set
        {
            if (SetProperty(ref _selectedAppId, value))
            {
                BackgroundTask.Run(RefreshAsync(), "cloud refresh");
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
                // A pending write confirmation belongs to the row it was requested for; it never follows the selection.
                CancelWrite();
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
        StatusMessage = L.T("Запрос списка сохранений Steam Cloud...");

        try
        {
            CloudSaves.Clear();
            CancelWrite();
            var localSaves = _localSaveFilesProvider();
            var appIds = SelectedAppId == 0
                ? new[] { 4500, 20510, 41700, 1643320 }
                : new[] { SelectedAppId };

            foreach (var appId in appIds)
            {
                // Only saves of the same game can be the local side of that game's cloud files.
                var release = Services.CloudServiceAdapter.GetReleaseId(appId);
                var localPaths = localSaves.Where(save => string.Equals(save.ReleaseId, release, StringComparison.Ordinal)).Select(save => save.Path).ToArray();
                var files = await _cloudService.ListCloudFilesAsync(appId, localPaths);
                foreach (var f in files)
                {
                    CloudSaves.Add(new CloudSaveItemViewModel(f));
                }
            }

            StatusMessage = CloudSaves.Count == 0
                ? L.T("В Steam Cloud не найдено сохранений для выбранных игр.")
                : L.T("Найдено {0} сохранений в облаке.", CloudSaves.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("Ошибка загрузки списка: {0}", ex.Message);
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
        StatusMessage = L.T("Скачивание {0} из Steam Cloud...", selected.FileName);

        try
        {
            var bytes = await _cloudService.ReadCloudFileAsync(selected.AppId, selected.RemotePath);
            var downloadedFormat = Core.Editing.EditService.DetectFormat(bytes)
                ?? throw new InvalidDataException(L.T("Файл из облака — не сохранение S.T.A.L.K.E.R. или повреждён; локальный файл не тронут."));
            if (!string.Equals(downloadedFormat, selected.Model.ReleaseId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(L.T("Файл из облака — сохранение другой игры ({0}); локальный файл не тронут.", downloadedFormat));
            }

            var backupDir = _backupDirectoryProvider();
            string targetPath;
            if (!string.IsNullOrEmpty(selected.Model.LocalFilePath) && File.Exists(selected.Model.LocalFilePath))
            {
                // The local save is replaced like an edit: journaled backup (restorable in «Бэкапы»), atomic swap, read-back.
                targetPath = selected.Model.LocalFilePath;
                // The file being replaced must be a save of the same game (or unreadable): a name match is not enough.
                var localFormat = Core.Editing.EditService.DetectFormat(await File.ReadAllBytesAsync(targetPath));
                if (localFormat is not null && !string.Equals(localFormat, downloadedFormat, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(L.T("Локальный файл {0} — сохранение другой игры ({1}); он не тронут.", Path.GetFileName(targetPath), localFormat));
                }
                var receipt = await Task.Run(() => Core.Backups.LocalSaveReplacement.ReplaceWithBytes(
                    targetPath,
                    bytes,
                    backupDir,
                    readBack => _ = Core.Editing.EditService.DetectFormat(readBack.Span)
                        ?? throw new InvalidDataException("The replaced save is not readable.")));
                LastBackupPath = receipt.BackupPath;
            }
            else
            {
                var downloads = Path.Combine(backupDir, "cloud_downloads");
                Directory.CreateDirectory(downloads);
                targetPath = Path.Combine(downloads, Path.GetFileName(selected.FileName));
                var temporary = targetPath + ".part";
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, targetPath, overwrite: true);
            }

            StatusMessage = L.T("Файл {0} успешно скачан: {1}", selected.FileName, targetPath);
            _onSaveDownloaded?.Invoke(targetPath);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("Ошибка скачивания: {0}", ex.Message);
        }
        finally
        {
            IsWriting = false;
        }
    }

    public void RequestWrite()
    {
        if (!CanRequestWrite || SelectedCloudSave is not { Model.LocalFilePath: { } localPath } selected) return;
        WriteConfirmationChecked = false;
        try
        {
            // What is confirmed is this cloud file and these local bytes, not "whatever is selected later".
            var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(localPath)));
            _writeIntent = new CloudWriteIntent(selected.AppId, selected.RemotePath, selected.FileName, localPath, sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = L.T("Ошибка записи: {0}", exception.Message);
            return;
        }
        ShowWriteConfirmDialog = true;
    }

    public void CancelWrite()
    {
        _writeIntent = null;
        ShowWriteConfirmDialog = false;
        WriteConfirmationChecked = false;
    }

    public async Task ConfirmWriteAsync()
    {
        var intent = _writeIntent;
        var confirmed = WriteConfirmationChecked;
        CancelWrite();
        if (intent is null || !confirmed) return;

        IsWriting = true;
        StatusMessage = L.T("Запись {0} в Steam Cloud (RemoteStorage)...", intent.FileName);

        try
        {
            var data = await File.ReadAllBytesAsync(intent.LocalPath);
            if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)), intent.LocalSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(L.T("Локальный файл изменился после запроса записи; подтвердите запись ещё раз."));
            }
            var backupDir = _backupDirectoryProvider();

            var result = await _cloudService.WriteCloudFileAsync(
                intent.AppId,
                intent.RemotePath,
                data,
                backupDir);

            LastWriteStatus = result.Status;
            LastWriteReason = result.Message;
            LastBackupPath = result.BackupPath;

            var writeMessage = result.Status switch
            {
                CloudWriteStatus.Verified => L.T("Записано и проверено: {0}", result.Message),
                CloudWriteStatus.Uncertain => L.T("Результат записи не подтверждён (повтор не выполняется): {0}", result.Message),
                _ => L.T("Запись отменена: {0}", result.Message),
            };

            await RefreshAsync();
            StatusMessage = writeMessage;
        }
        catch (Exception ex)
        {
            LastWriteStatus = CloudWriteStatus.Aborted;
            LastWriteReason = ex.Message;
            StatusMessage = L.T("Ошибка записи: {0}", ex.Message);
        }
        finally
        {
            IsWriting = false;
        }
    }
}

/// <summary>A local save and the game it belongs to, as the library read it.</summary>
public sealed record LocalSaveReference(string Path, string ReleaseId);

/// <summary>The exact cloud write the user was asked to confirm: target, source file and the bytes it had then.</summary>
internal sealed record CloudWriteIntent(int AppId, string RemotePath, string FileName, string LocalPath, string LocalSha256);
