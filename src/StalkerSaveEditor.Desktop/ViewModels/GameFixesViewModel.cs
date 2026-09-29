using System.Collections.ObjectModel;
using StalkerSaveEditor.Core.Diagnostics;
using StalkerSaveEditor.Core.Patching;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed record GameFixCategoryRow(string Name, GameFixCategory Category, int Count);

public sealed record GameFixEntry(GameFixDefinition Definition, GameFixState State, string? InstalledVersion = null)
{
    public string Id => Definition.Id;
    public string Title => Definition.Id switch
    {
        "cop.prp.crow-counter-guard" => L.T("Защитить счётчик ворон от повторного уменьшения"),
        "cop.prp.x8-burer-health-guard" => L.T("Устранить восстановление здоровья буреров за пределами X8"),
        "cop.prp.jupiter-scanner-task-guard" => L.T("Запретить установку сканеров Юпитера до начала задания"),
        "cop.prp.altered-insulator-door-gate" => L.T("Открывать дверь изменённого изолятора только по заданию и с артефактом"),
        "cop.prp.knife-hit-reach" => L.T("Исправить дальность и радиус второго удара ножа"),
        "cop.prp.sky-stretching-fix" => L.T("Исправить растяжение неба в шейдерах R2 и R3"),
        _ => Definition.Title,
    };
    public string Problem => Definition.Id switch
    {
        "cop.prp.crow-counter-guard" => L.T("Повторная очистка сетевого объекта может уменьшить счётчик ворон уже удалённой птицы."),
        "cop.prp.x8-burer-health-guard" => L.T("Попадание по буреру снаружи ограничителя лаборатории X8 полностью восстанавливает его здоровье."),
        "cop.prp.jupiter-scanner-task-guard" => L.T("Два дополнительных поля позволяют установить сканер до запуска соответствующего задания."),
        "cop.prp.altered-insulator-door-gate" => L.T("Зонный триггер открывает дверь без проверки задания и наличия половины артефакта."),
        "cop.prp.knife-hit-reach" => L.T("Вторая зона удара ножа короче и уже первой зоны."),
        "cop.prp.sky-stretching-fix" => L.T("Вершинные шейдеры неба одинаково масштабируют вертикальную координату."),
        _ => Definition.Problem,
    };
    public string Description => Definition.Id switch
    {
        "cop.prp.crow-counter-guard" => L.T("Удалять запись только пока ворона зарегистрирована и не допускать отрицательного счётчика."),
        "cop.prp.x8-burer-health-guard" => L.T("Удалить три обработчика попаданий, которые восстанавливают здоровье буреров при атаке извне X8."),
        "cop.prp.jupiter-scanner-task-guard" => L.T("Добавить проверку начала задания только для четвёртого и пятого полей установки."),
        "cop.prp.altered-insulator-door-gate" => L.T("Проверять начало задания и наличие половины артефакта перед открытием двери."),
        "cop.prp.knife-hit-reach" => L.T("Изменить только расстояние и радиус второй зоны удара по исправлению PRP."),
        "cop.prp.sky-stretching-fix" => L.T("Заменить только преобразование позиции неба в шейдерах DirectX 9 (R2) и DirectX 10 (R3)."),
        _ => Definition.Description,
    };
    public string Source => Definition.Id.StartsWith("cop.prp.", StringComparison.Ordinal)
        ? L.T("Источник: Pripyat Reclamation Patch v1.2")
        : Definition.Source;
    public string Builds => string.Join(", ", Definition.SupportedSteamBuildIds);
    public string CategoryName => GameFixesViewModel.CategoryName(Definition.Category);
    public string MaturityName => Definition.Maturity switch
    {
        GameFixMaturity.Validated => L.T("ПРОВЕРЕНО"),
        GameFixMaturity.Experimental => L.T("ЭКСПЕРИМЕНТАЛЬНО"),
        _ => L.T("ИССЛЕДОВАНИЕ"),
    };
    public string StateName => State switch
    {
        _ when UpdateAvailable => L.T("ОБНОВЛЕНИЕ ДОСТУПНО"),
        GameFixState.Installed => L.T("УСТАНОВЛЕНО"),
        GameFixState.Modified => L.T("ФАЙЛ ИЗМЕНЁН ПОСЛЕ УСТАНОВКИ"),
        GameFixState.Removed => L.T("УДАЛЕНО"),
        _ => L.T("НЕ УСТАНОВЛЕНО"),
    };
    public bool UpdateAvailable => State == GameFixState.Installed && InstalledVersion is not null &&
        !string.Equals(InstalledVersion, Definition.Version, StringComparison.Ordinal);
}

public sealed class GameFixesViewModel : ObservableViewModel
{
    private GameTargetOption _selectedTarget;
    private GameFixEntry? _selectedFix;
    private string _gameDirectory = string.Empty;
    private string _status = string.Empty;
    private string _compatibilityStatus = string.Empty;
    private string? _checkedDirectory;
    private GameTarget? _checkedTarget;
    private string? _steamBuildId;
    private bool _installationMarkerValid;
    private bool _isBusy;

    public GameFixesViewModel()
    {
        Targets = new ObservableCollection<GameTargetOption>(Enum.GetValues<GameTarget>()
            .Select(target =>
            {
                var descriptor = GameTargetCatalog.Get(target);
                return new GameTargetOption(target, descriptor.Id, descriptor.Title);
            }));
        _selectedTarget = Targets.FirstOrDefault(target => GameFixCatalog.ForGame(target.Target).Count > 0) ?? Targets[0];
        Categories = new ObservableCollection<GameFixCategoryRow>();
        Fixes = new ObservableCollection<GameFixEntry>();
        CheckInstallationCommand = new RelayCommand(async () => await CheckInstallationAsync(), CanCheckInstallation);
        ApplyEssentialPresetCommand = new RelayCommand(async () => await ApplyPresetAsync(GameFixPreset.EssentialOnly), () => CanApplyPreset(GameFixPreset.EssentialOnly));
        ApplyRecommendedPresetCommand = new RelayCommand(async () => await ApplyPresetAsync(GameFixPreset.Recommended), () => CanApplyPreset(GameFixPreset.Recommended));
        ApplyAllSafePresetCommand = new RelayCommand(async () => await ApplyPresetAsync(GameFixPreset.AllSafeFixes), () => CanApplyPreset(GameFixPreset.AllSafeFixes));
        InstallCommand = new RelayCommand(async () => await InstallAsync(), CanInstall);
        UpdateCommand = new RelayCommand(async () => await UpdateAsync(), CanUpdate);
        RemoveCommand = new RelayCommand(async () => await RemoveAsync(), CanRemove);
        RefreshCatalogue();
    }

    public ObservableCollection<GameTargetOption> Targets { get; }
    public ObservableCollection<GameFixCategoryRow> Categories { get; }
    public ObservableCollection<GameFixEntry> Fixes { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand CheckInstallationCommand { get; }
    public RelayCommand ApplyEssentialPresetCommand { get; }
    public RelayCommand ApplyRecommendedPresetCommand { get; }
    public RelayCommand ApplyAllSafePresetCommand { get; }

    public GameTargetOption SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                InvalidateInspection();
                RefreshCatalogue();
                RefreshFixStates();
            }
        }
    }

    public GameFixEntry? SelectedFix
    {
        get => _selectedFix;
        set
        {
            if (SetProperty(ref _selectedFix, value))
            {
                NotifySelection();
                UpdateCompatibilityStatus();
            }
        }
    }

    public string GameDirectory
    {
        get => _gameDirectory;
        set
        {
            if (SetProperty(ref _gameDirectory, value ?? string.Empty))
            {
                InvalidateInspection();
                RefreshFixStates();
                NotifyCommands();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string CompatibilityStatus
    {
        get => _compatibilityStatus;
        private set => SetProperty(ref _compatibilityStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) NotifyCommands();
        }
    }

    public bool HasSelectedFix => SelectedFix is not null;
    public string CatalogueStatus
    {
        get
        {
            var count = GameFixCatalog.ForGame(SelectedTarget.Target).Count;
            return count == 0
                ? L.T("НЕТ ПРОВЕРЕННЫХ ИСПРАВЛЕНИЙ ДЛЯ ЭТОЙ ВЕРСИИ.")
                : L.T("ИСПРАВЛЕНИЙ В КАТАЛОГЕ: {0}", count);
        }
    }

    public string PresetStatus => L.T("РЕКОМЕНДУЕМЫЙ ПРЕСЕТ ВКЛЮЧАЕТ ОБЯЗАТЕЛЬНЫЕ И РЕКОМЕНДУЕМЫЕ; ИЗМЕНЕНИЯ ТОЛЬКО ПО ЯВНОЙ КОМАНДЕ.");
    public string PresetChangeStatus
    {
        get
        {
            var previous = GameFixCatalog.PreviousPresetCount(SelectedTarget.Target, GameFixPreset.Recommended);
            var current = GameFixCatalog.ForPreset(SelectedTarget.Target, GameFixPreset.Recommended).Count;
            return current <= previous
                ? string.Empty
                : L.T("НОВЫЕ ИСПРАВЛЕНИЯ В ПРЕСЕТЕ: {0} → {1} (каталог {2} → {3})",
                    previous,
                    current,
                    GameFixCatalog.PreviousDatasetVersion,
                    GameFixCatalog.DatasetVersion);
        }
    }
    public string SelectedTitle => SelectedFix?.Title ?? L.T("ВЫБЕРИТЕ ИСПРАВЛЕНИЕ");
    public string SelectedId => SelectedFix?.Id ?? string.Empty;
    public string SelectedProblem => SelectedFix?.Problem ?? string.Empty;
    public string SelectedDescription => SelectedFix?.Description ?? string.Empty;
    public string SelectedSource => SelectedFix?.Source ?? string.Empty;
    public string SelectedBuilds => SelectedFix?.Builds ?? string.Empty;
    public string SelectedClassification => SelectedFix is null
        ? string.Empty
        : $"{SelectedFix.CategoryName} · {SelectedFix.MaturityName} · {SelectedFix.StateName}";
    public bool ShowUpdateButton => SelectedFix?.UpdateAvailable == true;
    public string SelectedFiles => SelectedFix is null
        ? string.Empty
        : string.Join(Environment.NewLine, SelectedFix.Definition.TextPatches.Select(patch => patch.RelativePath));

    public async Task CheckInstallationAsync()
    {
        if (!CanCheckInstallation()) return;
        IsBusy = true;
        Status = string.Empty;
        try
        {
            var target = SelectedTarget.Target;
            var directory = Path.GetFullPath(GameDirectory);
            var report = await Task.Run(() => GameDoctor.Analyze(target, directory));
            _checkedTarget = target;
            _checkedDirectory = directory;
            _steamBuildId = report.SteamBuildId;
            _installationMarkerValid = report.Checks.Any(check => check.Id == "installation" && check.Status == GameDoctorStatus.Ok);
            UpdateCompatibilityStatus();
            RefreshFixStates(SelectedFix?.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            InvalidateInspection();
            CompatibilityStatus = L.T("ОШИБКА: {0}", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public static string CategoryName(GameFixCategory category) => category switch
    {
        GameFixCategory.Essential => L.T("ОБЯЗАТЕЛЬНЫЕ"),
        GameFixCategory.Recommended => L.T("РЕКОМЕНДУЕМЫЕ"),
        GameFixCategory.Optional => L.T("НЕОБЯЗАТЕЛЬНЫЕ"),
        GameFixCategory.Community => L.T("СООБЩЕСТВО"),
        GameFixCategory.Experimental => L.T("ЭКСПЕРИМЕНТАЛЬНЫЕ"),
        _ => category.ToString(),
    };

    public async Task InstallAsync()
    {
        if (!CanInstall() || SelectedFix is null) return;
        var entry = SelectedFix;
        IsBusy = true;
        Status = string.Empty;
        try
        {
            var result = await Task.Run(() => new GameFixEngine().Install(entry.Definition, GameDirectory));
            RefreshFixStates(entry.Id);
            Status = result.Changed
                ? L.T("УСТАНОВЛЕНО: {0}", entry.Id)
                : L.T("УЖЕ УСТАНОВЛЕНО: {0}", entry.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("ОШИБКА: {0}", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ApplyRecommendedPresetAsync() => ApplyPresetAsync(GameFixPreset.Recommended);

    private async Task ApplyPresetAsync(GameFixPreset preset)
    {
        if (!CanApplyPreset(preset)) return;
        IsBusy = true;
        Status = string.Empty;
        try
        {
            var result = await Task.Run(() => new GameFixEngine().ApplyPreset(preset: preset, game: SelectedTarget.Target, gameDirectory: GameDirectory));
            RefreshFixStates();
            Status = result.SelectedFixCount == 0
                ? L.T("ПРЕСЕТ НЕ СОДЕРЖИТ ПРОВЕРЕННЫХ ИСПРАВЛЕНИЙ ДЛЯ ЭТОЙ ИГРЫ.")
                : L.T("ПРЕСЕТ {0}: УСТАНОВЛЕНО {1}; УЖЕ АКТУАЛЬНЫХ {2}.", PresetName(preset), result.InstalledFixIds.Count, result.AlreadyInstalledFixIds.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("ОШИБКА: {0}", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RemoveAsync()
    {
        if (!CanRemove() || SelectedFix is null) return;
        var entry = SelectedFix;
        IsBusy = true;
        Status = string.Empty;
        try
        {
            var result = await Task.Run(() => new GameFixEngine().Uninstall(entry.Definition, GameDirectory));
            RefreshFixStates(entry.Id);
            Status = result.Changed
                ? L.T("УДАЛЕНО И ВОССТАНОВЛЕНО: {0}", entry.Id)
                : L.T("ИСПРАВЛЕНИЕ НЕ БЫЛО УСТАНОВЛЕНО: {0}", entry.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("ОШИБКА: {0}", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UpdateAsync()
    {
        if (!CanUpdate() || SelectedFix is null) return;
        var entry = SelectedFix;
        IsBusy = true;
        Status = string.Empty;
        try
        {
            var result = await Task.Run(() => new GameFixEngine().Update(entry.Definition, GameDirectory));
            RefreshFixStates(entry.Id);
            Status = result.Changed
                ? L.T("ОБНОВЛЕНО: {0}", entry.Id)
                : L.T("УЖЕ АКТУАЛЬНО: {0}", entry.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = L.T("ОШИБКА: {0}", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInstall() => !IsBusy && SelectedFix is not null && Directory.Exists(GameDirectory) &&
        _installationMarkerValid && _checkedTarget == SelectedTarget.Target &&
        _checkedDirectory is not null && PathEquals(_checkedDirectory, GameDirectory) &&
        _steamBuildId is not null && SelectedFix.Definition.SupportedSteamBuildIds.Contains(_steamBuildId, StringComparer.Ordinal) &&
        SelectedFix.State is GameFixState.NotInstalled or GameFixState.Removed;

    private bool CanApplyPreset(GameFixPreset preset)
    {
        if (IsBusy || !Directory.Exists(GameDirectory) || !_installationMarkerValid ||
            _checkedTarget != SelectedTarget.Target || _checkedDirectory is null || !PathEquals(_checkedDirectory, GameDirectory))
            return false;
        var fixes = GameFixCatalog.ForPreset(SelectedTarget.Target, preset);
        if (fixes.Count == 0) return true;
        return _steamBuildId is not null && fixes.All(definition =>
            definition.SupportedSteamBuildIds.Contains(_steamBuildId, StringComparer.Ordinal));
    }

    private bool CanRemove() => !IsBusy && SelectedFix is not null && Directory.Exists(GameDirectory) &&
        SelectedFix.State is GameFixState.Installed or GameFixState.Modified;

    private bool CanUpdate() => !IsBusy && SelectedFix?.UpdateAvailable == true && Directory.Exists(GameDirectory) &&
        _installationMarkerValid && _checkedTarget == SelectedTarget.Target &&
        _checkedDirectory is not null && PathEquals(_checkedDirectory, GameDirectory) &&
        _steamBuildId is not null && SelectedFix.Definition.SupportedSteamBuildIds.Contains(_steamBuildId, StringComparer.Ordinal);

    private bool CanCheckInstallation() => !IsBusy && Directory.Exists(GameDirectory);

    private void RefreshCatalogue()
    {
        var selectedId = SelectedFix?.Id;
        var counts = GameFixCatalog.CategoryCounts(SelectedTarget.Target);
        Categories.Clear();
        foreach (var category in Enum.GetValues<GameFixCategory>())
        {
            Categories.Add(new GameFixCategoryRow(CategoryName(category), category, counts[category]));
        }

        Fixes.Clear();
        foreach (var definition in GameFixCatalog.ForGame(SelectedTarget.Target))
        {
            Fixes.Add(new GameFixEntry(definition, GameFixState.NotInstalled));
        }
        SelectedFix = Fixes.FirstOrDefault(entry => entry.Id == selectedId) ?? Fixes.FirstOrDefault();
        OnPropertyChanged(nameof(CatalogueStatus));
        OnPropertyChanged(nameof(PresetStatus));
        OnPropertyChanged(nameof(PresetChangeStatus));
    }

    private void RefreshFixStates(string? selectedId = null)
    {
        IReadOnlyList<GameFixInstalledInfo> installed = [];
        if (Directory.Exists(GameDirectory))
        {
            try
            {
                installed = new GameFixEngine().ListInstalled(GameDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                Status = L.T("ОШИБКА: {0}", exception.Message);
            }
        }

        var priorSelection = selectedId ?? SelectedFix?.Id;
        for (var index = 0; index < Fixes.Count; index++)
        {
            var definition = Fixes[index].Definition;
            var installedFix = installed.FirstOrDefault(item => item.Id == definition.Id);
            Fixes[index] = new GameFixEntry(definition,
                installedFix?.State ?? GameFixState.NotInstalled,
                installedFix?.Version);
        }
        SelectedFix = Fixes.FirstOrDefault(entry => entry.Id == priorSelection) ?? Fixes.FirstOrDefault();
        NotifyCommands();
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelectedFix));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedId));
        OnPropertyChanged(nameof(SelectedProblem));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SelectedBuilds));
        OnPropertyChanged(nameof(SelectedClassification));
        OnPropertyChanged(nameof(SelectedFiles));
        OnPropertyChanged(nameof(ShowUpdateButton));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        CheckInstallationCommand.NotifyCanExecuteChanged();
        ApplyEssentialPresetCommand.NotifyCanExecuteChanged();
        ApplyRecommendedPresetCommand.NotifyCanExecuteChanged();
        ApplyAllSafePresetCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    private void InvalidateInspection()
    {
        _checkedDirectory = null;
        _checkedTarget = null;
        _steamBuildId = null;
        _installationMarkerValid = false;
        CompatibilityStatus = string.Empty;
    }

    private void UpdateCompatibilityStatus()
    {
        if (_checkedDirectory is null || _checkedTarget != SelectedTarget.Target || !PathEquals(_checkedDirectory, GameDirectory))
        {
            CompatibilityStatus = Directory.Exists(GameDirectory) ? L.T("СНАЧАЛА ПРОВЕРЬТЕ УСТАНОВКУ И ВЕРСИЮ.") : string.Empty;
        }
        else if (!_installationMarkerValid)
        {
            CompatibilityStatus = L.T("ПАПКА НЕ ПОХОЖА НА ВЫБРАННУЮ УСТАНОВКУ ИГРЫ.");
        }
        else if (_steamBuildId is null)
        {
            CompatibilityStatus = L.T("ВЕРСИЯ STEAM НЕ ОПРЕДЕЛЕНА; УСТАНОВКА ИСПРАВЛЕНИЙ С ЗАЩИТОЙ ПО СБОРКЕ НЕДОСТУПНА.");
        }
        else if (SelectedFix is null)
        {
            CompatibilityStatus = L.T("НАЙДЕНА СБОРКА STEAM: {0}.", _steamBuildId);
        }
        else if (SelectedFix.Definition.SupportedSteamBuildIds.Contains(_steamBuildId, StringComparer.Ordinal))
        {
            CompatibilityStatus = L.T("СБОРКА STEAM {0} ПОДДЕРЖИВАЕТ ВЫБРАННОЕ ИСПРАВЛЕНИЕ.", _steamBuildId);
        }
        else
        {
            CompatibilityStatus = L.T("СБОРКА STEAM {0} НЕ ПОДДЕРЖИВАЕТ ВЫБРАННОЕ ИСПРАВЛЕНИЕ.", _steamBuildId);
        }
        NotifyCommands();
    }

    private static string PresetName(GameFixPreset preset) => preset switch
    {
        GameFixPreset.EssentialOnly => L.T("ОБЯЗАТЕЛЬНЫЕ"),
        GameFixPreset.Recommended => L.T("РЕКОМЕНДУЕМЫЕ"),
        GameFixPreset.AllSafeFixes => L.T("ВСЕ БЕЗОПАСНЫЕ"),
        _ => preset.ToString(),
    };

    private static bool PathEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
