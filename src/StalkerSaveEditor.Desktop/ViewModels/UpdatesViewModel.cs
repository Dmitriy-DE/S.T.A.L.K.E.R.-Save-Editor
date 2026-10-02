using System.IO;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class UpdatesViewModel : ObservableViewModel
{
    private readonly IUpdateServiceAdapter _adapter;

    private string _latestVersion = string.Empty;
    private UpdateState _state = UpdateState.Current;
    private bool _isChecking;
    private bool _isDownloading;
    private bool _isInstalling;
    private double _progressPercentage;
    private string _progressStageText = string.Empty;
    private string _statusMessage = string.Empty;
    private string? _errorMessage;
    private bool _showNotificationBanner;
    private UpdateArtifact? _availableArtifact;
    private string? _downloadedFilePath;

    public UpdatesViewModel(IUpdateServiceAdapter? adapter = null)
    {
        _adapter = adapter ?? HostPlatform.CreateUpdateService?.Invoke() ?? new UnavailableUpdateServiceAdapter();
        CurrentVersion = _adapter.CurrentVersion;

        CheckUpdatesCommand = new RelayCommand(async () => await CheckAsync(silent: false), () => CanCheck);
        DownloadCommand = new RelayCommand(async () => await DownloadAsync(), () => CanDownload);
        InstallCommand = new RelayCommand(async () => await InstallAsync(), () => CanInstall);
        DismissBannerCommand = new RelayCommand(DismissBanner);
    }

    public string CurrentVersion { get; }

    public string LatestVersion
    {
        get => _latestVersion;
        private set
        {
            if (SetProperty(ref _latestVersion, value))
            {
                OnPropertyChanged(nameof(NotificationBannerText));
            }
        }
    }

    public string NotificationBannerText => string.IsNullOrWhiteSpace(LatestVersion)
        ? L.T("Доступна новая версия приложения!")
        : L.T("Доступна новая версия приложения ({0})!", LatestVersion);

    public UpdateState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(StateBadgeText));
                OnPropertyChanged(nameof(StateBadgeColor));
                OnPropertyChanged(nameof(CanDownload));
                DownloadCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsUpdateAvailable => State == UpdateState.Available;

    public string StateBadgeText => State switch
    {
        UpdateState.Current => L.T("У вас актуальная версия"),
        UpdateState.Available => L.T("Доступно обновление"),
        UpdateState.Unavailable => L.T("Обновление недоступно"),
        UpdateState.Invalid => L.T("Ошибка проверки манифеста"),
        _ => L.T("Статус неизвестен")
    };

    public string StateBadgeColor => State switch
    {
        UpdateState.Current => "#4E7A4A",
        UpdateState.Available => "#C9A038",
        UpdateState.Invalid => "#8E3A2E",
        _ => "#555555"
    };

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public bool IsBusy => IsChecking || IsDownloading || IsInstalling;

    public double ProgressPercentage
    {
        get => _progressPercentage;
        private set => SetProperty(ref _progressPercentage, value);
    }

    public string ProgressStageText
    {
        get => _progressStageText;
        private set => SetProperty(ref _progressStageText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool ShowNotificationBanner
    {
        get => _showNotificationBanner;
        set => SetProperty(ref _showNotificationBanner, value);
    }

    public UpdateArtifact? AvailableArtifact
    {
        get => _availableArtifact;
        private set
        {
            if (SetProperty(ref _availableArtifact, value))
            {
                OnPropertyChanged(nameof(CanDownload));
                DownloadCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? DownloadedFilePath
    {
        get => _downloadedFilePath;
        private set
        {
            if (SetProperty(ref _downloadedFilePath, value))
            {
                OnPropertyChanged(nameof(IsDownloaded));
                OnPropertyChanged(nameof(CanInstall));
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsDownloaded => !string.IsNullOrEmpty(DownloadedFilePath) && File.Exists(DownloadedFilePath);

    public bool CanCheck => !IsBusy;
    public bool CanDownload => IsUpdateAvailable && AvailableArtifact is not null && !IsBusy;
    public bool CanInstall => IsDownloaded && AvailableArtifact is not null && !IsBusy;

    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand DismissBannerCommand { get; }

    public async Task CheckAsync(bool silent = false)
    {
        if (IsBusy) return;
        IsChecking = true;
        if (!silent)
        {
            StatusMessage = L.T("Проверка наличия обновлений...");
            ErrorMessage = null;
        }

        try
        {
            var result = await _adapter.CheckAsync();
            State = result.State;

            if (result.Manifest is not null)
            {
                LatestVersion = result.Manifest.Version;
            }

            AvailableArtifact = result.Artifact;

            if (result.State == UpdateState.Available)
            {
                ShowNotificationBanner = true;
                StatusMessage = L.T("Доступна новая версия {0}!", LatestVersion);
            }
            else if (result.State == UpdateState.Current)
            {
                ShowNotificationBanner = false;
                StatusMessage = L.T("Установлена последняя версия приложения.");
            }
            else
            {
                if (!silent)
                {
                    ErrorMessage = result.Error ?? L.T("Не удалось проверить обновления.");
                    StatusMessage = L.T("Проверка завершилась с ошибкой.");
                }
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                ErrorMessage = ex.Message;
                StatusMessage = L.T("Ошибка подключения к серверу обновлений.");
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    public async Task DownloadAsync()
    {
        var artifact = AvailableArtifact;
        if (artifact is null || IsBusy) return;

        IsDownloading = true;
        ErrorMessage = null;
        StatusMessage = L.T("Скачивание пакета обновления...");
        ProgressPercentage = 0;

        var progress = new Progress<UpdateProgress>(p =>
        {
            ProgressStageText = p.Message;
            if (p.TotalBytes.HasValue && p.TotalBytes.Value > 0 && p.CompletedBytes.HasValue)
            {
                ProgressPercentage = Math.Clamp((double)p.CompletedBytes.Value / p.TotalBytes.Value * 100, 0, 100);
            }
        });

        try
        {
            var path = await _adapter.DownloadAsync(artifact, progress);
            DownloadedFilePath = path;
            StatusMessage = L.T("Пакет обновления скачан: {0}", Path.GetFileName(path));
            ProgressPercentage = 100;
        }
        catch (Exception ex)
        {
            ErrorMessage = L.T("Ошибка скачивания: {0}", ex.Message);
            StatusMessage = L.T("Не удалось завершить скачивание.");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    public async Task InstallAsync()
    {
        var artifact = AvailableArtifact;
        var path = DownloadedFilePath;
        if (artifact is null || string.IsNullOrEmpty(path) || !File.Exists(path) || IsBusy) return;

        IsInstalling = true;
        ErrorMessage = null;
        StatusMessage = L.T("Установка обновления...");

        var progress = new Progress<UpdateProgress>(p =>
        {
            ProgressStageText = p.Message;
        });

        try
        {
            var result = await _adapter.InstallAsync(path, artifact, progress);
            if (result.State == UpdateInstallState.Succeeded || result.State == UpdateInstallState.OpenedExternally)
            {
                StatusMessage = L.T("Обновление запущено: {0}", result.Message);
            }
            else
            {
                ErrorMessage = L.T("Установка не удалась ({0}): {1}", result.State, result.Message);
                StatusMessage = L.T("Ошибка установки обновления.");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = L.T("Ошибка запуска установки: {0}", ex.Message);
            StatusMessage = L.T("Не удалось запустить установку.");
        }
        finally
        {
            IsInstalling = false;
        }
    }

    public void DismissBanner()
    {
        ShowNotificationBanner = false;
    }

    private void UpdateCommandStates()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanInstall));
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
    }
}
