using System.Collections.ObjectModel;
using System.Text.Json;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Core.Storage;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed record ToolkitAuditRow(string RelativePath, string Classification, string Detail, string? OwnerId, bool CanCleanup);

public sealed class ToolkitConfigSettingRow : ObservableViewModel
{
    private string _valueInput = string.Empty;

    internal ToolkitConfigSettingRow(UserLtxSettingInfo setting)
    {
        Key = setting.Key;
        Meaning = MeaningFor(setting.Key);
        CurrentValue = setting.CurrentValue;
        OriginalValue = setting.OriginalValue;
        ChangedByToolkit = setting.ChangedByToolkit;
        HasConflict = setting.HasConflict;
    }

    public string Key { get; }
    public string Meaning { get; }
    public string? CurrentValue { get; }
    public string? OriginalValue { get; }
    public bool ChangedByToolkit { get; }
    public bool HasConflict { get; }
    public bool CanRestoreDefault => ChangedByToolkit && !HasConflict;
    public bool CanApplyValue => !HasConflict && !string.IsNullOrWhiteSpace(ValueInput);
    public string CurrentDisplay => CurrentValue ?? L.T("Не задано явно; действует значение игры.");
    public string DefaultDisplay => ChangedByToolkit
        ? OriginalValue ?? L.T("Значение по умолчанию игры")
        : L.T("Исходное значение не записано: настройка не менялась инструментом.");
    public string StateDisplay => HasConflict
        ? L.T("КОНФЛИКТ: значение изменено вне инструмента")
        : ChangedByToolkit ? L.T("ИЗМЕНЕНО ИНСТРУМЕНТОМ") : L.T("НЕ ИЗМЕНЕНО ИНСТРУМЕНТОМ");

    public string ValueInput
    {
        get => _valueInput;
        set
        {
            if (SetProperty(ref _valueInput, value ?? string.Empty)) OnPropertyChanged(nameof(CanApplyValue));
        }
    }

    private static string MeaningFor(string key) => key switch
    {
        "g_fov" => L.T("Поле зрения камеры"),
        "hud_fov" => L.T("Поле зрения HUD"),
        "mouse_sens" => L.T("Чувствительность мыши"),
        "hud_crosshair" => L.T("Прицел HUD"),
        "hud_crosshair_dist" => L.T("Дистанция отображения прицела"),
        "hud_info" => L.T("Информация HUD"),
        "hud_weapon" => L.T("Оружие в HUD"),
        "cl_dynamiccrosshair" => L.T("Динамический прицел"),
        _ => key,
    };
}

/// <summary>Snapshot, profile, explicit user.ltx editor and evidence-based install-audit actions.</summary>
public sealed class ToolkitEnvironmentViewModel : ObservableViewModel
{
    private readonly Func<(GameTarget Target, string Directory)> _selection;
    private readonly ToolkitSnapshotService _snapshots;
    private readonly ToolkitProfileService _profiles;
    private readonly string _configStateDirectory;
    private ToolkitSnapshotInfo? _selectedSnapshot;
    private ToolkitProfile? _selectedProfile;
    private string _profileName = string.Empty;
    private string _userLtxPath = string.Empty;
    private string _status = string.Empty;
    private string _auditStatus = string.Empty;
    private bool _isBusy;

    public ToolkitEnvironmentViewModel(
        Func<(GameTarget Target, string Directory)> selection,
        ToolkitSnapshotService? snapshots = null,
        ToolkitProfileService? profiles = null,
        string? configStateDirectory = null)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _snapshots = snapshots ?? new ToolkitSnapshotService();
        _profiles = profiles ?? new ToolkitProfileService();
        _configStateDirectory = Path.GetFullPath(configStateDirectory ?? AppPaths.ToolkitConfig);
        RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
        CreateSnapshotCommand = new RelayCommand(async () => await CreateSnapshotAsync(), CanCreateSnapshot);
        RestoreSnapshotCommand = new RelayCommand(async () => await RestoreSnapshotAsync(), CanRestoreSnapshot);
        DeleteSnapshotCommand = new RelayCommand(DeleteSnapshot, () => !IsBusy && SelectedSnapshot is not null);
        SaveProfileCommand = new RelayCommand(SaveProfile, CanSaveProfile);
        ApplyProfileCommand = new RelayCommand(async () => await ApplyProfileAsync(), CanApplyProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => !IsBusy && SelectedProfile is not null);
        LoadConfigCommand = new RelayCommand(LoadConfig, () => !IsBusy && SupportsXRayToolkit && Directory.Exists(CurrentDirectory));
        AuditCommand = new RelayCommand(Audit, () => !IsBusy && Directory.Exists(CurrentDirectory));
        Refresh();
    }

    public ObservableCollection<ToolkitSnapshotInfo> Snapshots { get; } = [];
    public ObservableCollection<ToolkitProfile> Profiles { get; } = [];
    public ObservableCollection<ToolkitConfigSettingRow> ConfigSettings { get; } = [];
    public ObservableCollection<ToolkitAuditRow> AuditRows { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CreateSnapshotCommand { get; }
    public RelayCommand RestoreSnapshotCommand { get; }
    public RelayCommand DeleteSnapshotCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand ApplyProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand LoadConfigCommand { get; }
    public RelayCommand AuditCommand { get; }

    public string CurrentDirectory => _selection().Directory;
    public string CurrentGameTitle => GameTargetCatalog.Get(_selection().Target).Title;
    public string CurrentInstallationDisplay => L.T("Управляемая установка: {0} · {1}", CurrentGameTitle,
        CurrentDirectory.Length == 0 ? L.T("не выбрана") : CurrentDirectory);
    public bool HasInstallation => Directory.Exists(CurrentDirectory);
    private bool SupportsXRayToolkit => GameTargetCatalog.Get(_selection().Target).IsXRay;
    public bool HasSnapshots => Snapshots.Count > 0;
    public bool HasProfiles => Profiles.Count > 0;
    public bool HasConfigSettings => ConfigSettings.Count > 0;
    public bool HasAuditRows => AuditRows.Count > 0;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            NotifyCommands();
        }
    }

    public ToolkitSnapshotInfo? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (!SetProperty(ref _selectedSnapshot, value)) return;
            OnPropertyChanged(nameof(SelectedSnapshotDetails));
            NotifyCommands();
        }
    }

    public ToolkitProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value)) return;
            OnPropertyChanged(nameof(SelectedProfileDetails));
            NotifyCommands();
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value ?? string.Empty)) SaveProfileCommand.NotifyCanExecuteChanged();
        }
    }

    public string UserLtxPath
    {
        get => _userLtxPath;
        set
        {
            if (SetProperty(ref _userLtxPath, value ?? string.Empty)) NotifyCommands();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string AuditStatus
    {
        get => _auditStatus;
        private set => SetProperty(ref _auditStatus, value);
    }

    public string SelectedSnapshotDetails => SelectedSnapshot is null ? string.Empty :
        string.Join(Environment.NewLine, SelectedSnapshot.Files.Select(file =>
            L.T("{0}: {1} · SHA-256 {2}", file.Provider, file.RelativePath, file.Sha256)));

    public string SelectedProfileDetails
    {
        get
        {
            if (SelectedProfile is null) return string.Empty;
            var lines = new List<string> { SelectedProfile.CompanionInstalled ? L.T("мод установлен") : L.T("мод не установлен") };
            lines.AddRange(SelectedProfile.FixIds);
            lines.AddRange(SelectedProfile.UserLtxOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + " " + pair.Value));
            return string.Join(Environment.NewLine, lines);
        }
    }

    public void Refresh()
    {
        try
        {
            var selectedSnapshotId = SelectedSnapshot?.Id;
            Snapshots.Clear();
            foreach (var snapshot in _snapshots.List()) Snapshots.Add(snapshot);
            SelectedSnapshot = Snapshots.FirstOrDefault(snapshot => snapshot.Id == selectedSnapshotId) ?? Snapshots.FirstOrDefault();

            var selectedProfileId = SelectedProfile?.Id;
            Profiles.Clear();
            foreach (var profile in _profiles.List()) Profiles.Add(profile);
            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedProfileId) ?? Profiles.FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            Status = L.T("Не удалось прочитать управляемые данные: {0}", exception.Message);
        }
        OnPropertyChanged(nameof(HasSnapshots));
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(CurrentDirectory));
        OnPropertyChanged(nameof(CurrentGameTitle));
        OnPropertyChanged(nameof(CurrentInstallationDisplay));
        OnPropertyChanged(nameof(HasInstallation));
        NotifyCommands();
    }

    public void LoadConfig()
    {
        if (!Directory.Exists(CurrentDirectory)) return;
        try
        {
            var path = UserLtxPath.Trim();
            if (path.Length == 0)
            {
                var appData = CompanionAppDataRootResolver.Resolve(CurrentDirectory);
                path = Path.Combine(appData, "user.ltx");
            }
            if (!File.Exists(path)) throw new FileNotFoundException("The selected installation's user.ltx was not found; choose an existing file.", path);
            var inspection = ManagedUserLtxSettings.Inspect(path, _configStateDirectory);
            UserLtxPath = inspection.FilePath;
            ConfigSettings.Clear();
            foreach (var setting in inspection.Settings) ConfigSettings.Add(new ToolkitConfigSettingRow(setting));
            Status = inspection.HasConflict
                ? L.T("Настройки содержат конфликт: {0}", inspection.Conflict ?? string.Empty)
                : L.T("Загружено настроек user.ltx: {0}", ConfigSettings.Count);
            OnPropertyChanged(nameof(HasConfigSettings));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or CompanionProtocolException)
        {
            Status = L.T("Не удалось прочитать user.ltx: {0}", exception.Message);
        }
    }

    public async Task CreateSnapshotAsync()
    {
        if (!CanCreateSnapshot()) return;
        IsBusy = true;
        try
        {
            var context = _selection();
            var userLtx = string.IsNullOrWhiteSpace(UserLtxPath) ? null : UserLtxPath.Trim();
            var snapshot = await Task.Run(() => _snapshots.Create(context.Target, context.Directory, userLtx));
            Status = L.T("Создан снимок: {0}", snapshot.Id);
            Refresh();
            SelectedSnapshot = Snapshots.FirstOrDefault(item => item.Id == snapshot.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("Не удалось создать снимок: {0}", exception.Message);
        }
        finally { IsBusy = false; }
    }

    public async Task RestoreSnapshotAsync()
    {
        if (!CanRestoreSnapshot() || SelectedSnapshot is null) return;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _snapshots.Restore(SelectedSnapshot.Id));
            Status = result.Message;
            Refresh();
            SelectedSnapshot = Snapshots.FirstOrDefault(item => item.Id == SelectedSnapshot?.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("Не удалось восстановить снимок: {0}", exception.Message);
        }
        finally { IsBusy = false; }
    }

    public void DeleteSnapshot()
    {
        if (IsBusy || SelectedSnapshot is null) return;
        try
        {
            var id = SelectedSnapshot.Id;
            _snapshots.Delete(id);
            Status = L.T("Снимок удалён: {0}", id);
            Refresh();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            Status = L.T("Не удалось удалить снимок: {0}", exception.Message);
        }
    }

    public void SaveProfile()
    {
        if (!CanSaveProfile()) return;
        try
        {
            var context = _selection();
            var path = ResolveExistingUserLtxPath();
            if (path is not null) UserLtxPath = path;
            var profile = _profiles.SaveCurrent(ProfileName, context.Target, context.Directory, path);
            Status = L.T("Профиль сохранён: {0}", profile.Name);
            Refresh();
            SelectedProfile = Profiles.FirstOrDefault(item => item.Id == profile.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("Не удалось сохранить профиль: {0}", exception.Message);
        }
    }

    public async Task ApplyProfileAsync()
    {
        if (!CanApplyProfile() || SelectedProfile is null) return;
        IsBusy = true;
        try
        {
            var context = _selection();
            var path = string.IsNullOrWhiteSpace(UserLtxPath) ? null : UserLtxPath.Trim();
            var result = await Task.Run(() => _profiles.Apply(SelectedProfile.Id, context.Target, context.Directory, path));
            Status = result.Message;
            Refresh();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("Не удалось применить профиль: {0}", exception.Message);
        }
        finally { IsBusy = false; }
    }

    public void DeleteProfile()
    {
        if (IsBusy || SelectedProfile is null) return;
        try
        {
            var id = SelectedProfile.Id;
            _profiles.Delete(id);
            Status = L.T("Профиль удалён.");
            Refresh();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            Status = L.T("Не удалось удалить профиль: {0}", exception.Message);
        }
    }

    public void ApplyConfig(ToolkitConfigSettingRow? row)
    {
        if (IsBusy || row is null || string.IsNullOrWhiteSpace(UserLtxPath)) return;
        try
        {
            ManagedUserLtxSettings.SetOverrides(UserLtxPath, _configStateDirectory,
                new Dictionary<string, string> { [row.Key] = row.ValueInput });
            Status = L.T("Настройка обновлена: {0}", row.Key);
            LoadConfig();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        {
            Status = L.T("Не удалось изменить настройку: {0}", exception.Message);
        }
    }

    public void RestoreConfigDefault(ToolkitConfigSettingRow? row)
    {
        if (IsBusy || row is null || !row.CanRestoreDefault || string.IsNullOrWhiteSpace(UserLtxPath)) return;
        try
        {
            ManagedUserLtxSettings.RestoreDefault(UserLtxPath, _configStateDirectory, row.Key);
            Status = L.T("Восстановлено значение игры: {0}", row.Key);
            LoadConfig();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        {
            Status = L.T("Не удалось восстановить настройку: {0}", exception.Message);
        }
    }

    public void Audit()
    {
        if (!Directory.Exists(CurrentDirectory)) return;
        try
        {
            var context = _selection();
            var report = ToolkitInstallAudit.Analyze(context.Target, context.Directory);
            AuditRows.Clear();
            foreach (var file in report.Entries)
            {
                var classification = file.Classification switch
                {
                    ToolkitAuditClassification.Vanilla => L.T("ВАНИЛЬНЫЙ ФАЙЛ"),
                    ToolkitAuditClassification.Unknown => L.T("НЕИЗВЕСТНО"),
                    ToolkitAuditClassification.OrphanedToolkitOwned => L.T("УСТАРЕВШИЙ ФИКС ИЗ КАТАЛОГА"),
                    ToolkitAuditClassification.OrphanedStateNeedsReview => L.T("СОСТОЯНИЕ БЕЗ МАНИФЕСТА"),
                    _ => file.Status == GameDoctorStatus.Ok ? L.T("УПРАВЛЯЕТСЯ ИНСТРУМЕНТОМ") : L.T("ТРЕБУЕТ ПРОВЕРКИ"),
                };
                var detail = file.Classification switch
                {
                    ToolkitAuditClassification.Vanilla => L.T("SHA-256 совпадает с известным источником retail для обнаруженной Steam-сборки."),
                    ToolkitAuditClassification.OrphanedToolkitOwned when file.CanCleanup => L.T("ID фикса отсутствует в текущем каталоге. Текущие файлы и резервные копии совпадают с манифестом провайдера; очистка восстановит записанные исходные байты."),
                    ToolkitAuditClassification.OrphanedToolkitOwned => L.T("Фикс устарел, но проверка владения, файлов или резервных копий не пройдена. Автоматическая очистка отключена."),
                    ToolkitAuditClassification.OrphanedStateNeedsReview => L.T("Состояние Toolkit не имеет проверяемого манифеста; файлы оставлены без изменений."),
                    ToolkitAuditClassification.Unknown => L.T("Источник и совместимость файла неизвестны; автоматически он не изменяется."),
                    _ when file.Status == GameDoctorStatus.Ok => L.T("Файл совпадает с записанным хэшем манифеста."),
                    _ => L.T("Файл отсутствует, изменён или имеет несколько владельцев; требуется проверка."),
                };
                AuditRows.Add(new ToolkitAuditRow(file.RelativePath, classification, detail, file.OwnerId, file.CanCleanup));
            }
            AuditStatus = L.T("Проверено файлов: {0}. Безопасных устаревших фиксов: {1}. Ванильными отмечены только файлы с известным хэшем точной сборки; прочие остаются неизвестными.",
                AuditRows.Count, report.SafeCleanupCandidateCount);
            OnPropertyChanged(nameof(HasAuditRows));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            AuditStatus = L.T("Не удалось проверить установку: {0}", exception.Message);
        }
    }

    public async Task CleanupOrphanAsync(ToolkitAuditRow? row)
    {
        if (IsBusy || row is null || !row.CanCleanup || string.IsNullOrWhiteSpace(row.OwnerId) || !Directory.Exists(CurrentDirectory)) return;
        IsBusy = true;
        try
        {
            var context = _selection();
            var result = await Task.Run(() => ToolkitInstallAudit.Cleanup(context.Target, context.Directory, row.OwnerId));
            Status = L.T("Устаревший фикс снят; восстановлено файлов: {0}.", result.Files.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            Status = L.T("Не удалось очистить фикс: {0}", exception.Message);
        }
        finally
        {
            IsBusy = false;
            Audit();
        }
    }

    private bool CanCreateSnapshot() => !IsBusy && SupportsXRayToolkit && HasInstallation;
    private bool CanRestoreSnapshot() => !IsBusy && SelectedSnapshot is not null && PathMatches(SelectedSnapshot.GameDirectory, CurrentDirectory) && SelectedSnapshot.Target == _selection().Target;
    private bool CanSaveProfile() => !IsBusy && SupportsXRayToolkit && HasInstallation && !string.IsNullOrWhiteSpace(ProfileName);
    private bool CanApplyProfile() => !IsBusy && SupportsXRayToolkit && SelectedProfile is not null && HasInstallation && SelectedProfile.Target == _selection().Target &&
        (SelectedProfile.UserLtxOverrides.Count == 0 || !string.IsNullOrWhiteSpace(UserLtxPath) || SelectedProfile.UserLtxPath is not null);

    private string? ResolveExistingUserLtxPath()
    {
        if (!string.IsNullOrWhiteSpace(UserLtxPath)) return UserLtxPath.Trim();
        if (!Directory.Exists(CurrentDirectory)) return null;
        try
        {
            var path = Path.Combine(CompanionAppDataRootResolver.Resolve(CurrentDirectory), "user.ltx");
            return File.Exists(path) ? path : null;
        }
        catch (Exception exception) when (exception is CompanionProtocolException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreateSnapshotCommand.NotifyCanExecuteChanged();
        RestoreSnapshotCommand.NotifyCanExecuteChanged();
        DeleteSnapshotCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
        ApplyProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        LoadConfigCommand.NotifyCanExecuteChanged();
        AuditCommand.NotifyCanExecuteChanged();
    }

    private static bool PathMatches(string left, string right)
    {
        try
        {
            return string.Equals(SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(left)), SaveSlotDiscovery.ResolveLinks(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException) { return false; }
    }
}
