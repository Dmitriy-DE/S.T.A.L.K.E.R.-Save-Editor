using System.Text.Json;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class BackupRecordViewModel(LocalSaveBackupRecord record) : ObservableViewModel
{
    public LocalSaveBackupRecord Record { get; } = record;

    public string JournalPath => Record.JournalPath;
    public string BackupPath => Record.BackupPath;
    public string CreatedAt => Record.CreatedAt;
    public string SourcePath => Record.SourcePath;
    public string SourceName => Path.GetFileName(Record.SourcePath);
    public string SourceSha256 => Record.SourceSha256;
    public string? OutputPath => Record.OutputPath;
    public string? OutputName => Record.OutputPath is not null ? Path.GetFileName(Record.OutputPath) : null;
    public string? ActualSha256 => Record.ActualSha256;
    public BackupVerificationStatus Status => Record.Status;
    public string? Error => Record.Error;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public string StatusDisplay => Status switch
    {
        BackupVerificationStatus.Verified => L.T("Проверен"),
        BackupVerificationStatus.Missing => L.T("Отсутствует"),
        BackupVerificationStatus.Corrupt => L.T("Повреждён"),
        _ => Status.ToString(),
    };

    public string StatusBadgeColor => Status switch
    {
        BackupVerificationStatus.Verified => "#7BCB62",
        _ => "#D85A45",
    };

    public bool CanRestore => Status == BackupVerificationStatus.Verified;

    public bool CanRestoreCopy => CanRestore;

    public bool CanRestoreInPlace => CanRestore && HasInPlaceRestoreOperation();

    public string RestoreDisabledReason => CanRestore
        ? string.Empty
        : Status switch
        {
            BackupVerificationStatus.Missing => L.T("Файл резервной копии отсутствует; восстановление отключено."),
            BackupVerificationStatus.Corrupt => L.T("Резервная копия повреждена; восстановление отключено."),
            _ => L.T("Резервная копия не прошла проверку; восстановление отключено."),
        };

    public string RestoreInPlaceDisabledReason => !CanRestore
        ? RestoreDisabledReason
        : CanRestoreInPlace
            ? string.Empty
            : L.T("Эту резервную копию можно восстановить только в отдельную копию.");

    public string RestoreCopyDisabledReason => RestoreDisabledReason;

    private bool HasInPlaceRestoreOperation()
    {
        if (Record.Operation.ValueKind != JsonValueKind.Object ||
            !Record.Operation.TryGetProperty("mode", out var mode) ||
            mode.ValueKind != JsonValueKind.String ||
            mode.GetString() is not ("replace" or "restore") ||
            string.IsNullOrWhiteSpace(Record.OutputPath))
        {
            return false;
        }

        try
        {
            var sourcePath = Path.GetFullPath(Record.SourcePath);
            var outputPath = Path.GetFullPath(Record.OutputPath);
            return OperatingSystem.IsWindows()
                ? string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase)
                : string.Equals(sourcePath, outputPath, StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    public string ShortDateDisplay
    {
        get
        {
            if (DateTimeOffset.TryParse(CreatedAt, out var dto))
            {
                return dto.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            }
            return CreatedAt;
        }
    }
}
