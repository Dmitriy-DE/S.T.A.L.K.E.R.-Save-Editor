using StalkerSaveEditor.Desktop.Services;
using StalkerSaveEditor.Core.Backups;

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

    public string RestoreDisabledReason => CanRestore
        ? string.Empty
        : Status switch
        {
            BackupVerificationStatus.Missing => L.T("Файл резервной копии отсутствует; восстановление отключено."),
            BackupVerificationStatus.Corrupt => L.T("Резервная копия повреждена; восстановление отключено."),
            _ => L.T("Резервная копия не прошла проверку; восстановление отключено."),
        };

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
