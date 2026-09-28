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

public interface ICompanionService
{
    Task<CompanionStatus> GetStatusAsync(string gameReleaseId, CancellationToken ct = default);
    Task<bool> InstallAsync(string gameReleaseId, CancellationToken ct = default);
    Task<bool> UninstallAsync(string gameReleaseId, CancellationToken ct = default);
    Task<TimeSpan?> PingAsync(string gameReleaseId, CancellationToken ct = default);
    Task<IReadOnlyList<CompanionHotkey>> GetHotkeysAsync(string gameReleaseId, CancellationToken ct = default);
}
