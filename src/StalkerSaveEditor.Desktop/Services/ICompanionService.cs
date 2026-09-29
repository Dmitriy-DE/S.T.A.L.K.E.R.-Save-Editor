namespace StalkerSaveEditor.Desktop.Services;

public enum CompanionState
{
    NotInstalled,
    Installed,
    Active,
    Error
}

public sealed record CompanionStatus(
    CompanionState State,
    string Version,
    DateTime? LastPing,
    string GamePath,
    string? ErrorMessage = null);

public sealed record CompanionHotkey(
    string Action,
    string Key,
    string Description);

public sealed record CompanionInspectionResult(bool Success, string Info, string Inventory, string Message);

public sealed record CompanionActionResult(bool Success, string Message);

public interface ICompanionService
{
    bool SupportsLiveProtocol { get; }

    Task<CompanionStatus> GetStatusAsync(string gameReleaseId, CancellationToken ct = default);
    Task<bool> InstallAsync(string gameReleaseId, CancellationToken ct = default);
    Task<bool> UninstallAsync(string gameReleaseId, CancellationToken ct = default);
    Task<TimeSpan?> PingAsync(string gameReleaseId, CancellationToken ct = default);
    Task<IReadOnlyList<CompanionHotkey>> GetHotkeysAsync(string gameReleaseId, CancellationToken ct = default);
    Task<CompanionInspectionResult> InspectAsync(string gameReleaseId, CancellationToken ct = default);
    Task<CompanionActionResult> GiveItemAsync(string gameReleaseId, string section, int count = 1, CancellationToken ct = default);
}
