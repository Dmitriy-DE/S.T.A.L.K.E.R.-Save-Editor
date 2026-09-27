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
    public BackupVerificationStatus Status => Record.Status;
    public string? Error => Record.Error;

    public string StatusDisplay => Status switch
    {
        BackupVerificationStatus.Verified => "Проверен",
        BackupVerificationStatus.Missing => "Отсутствует",
        BackupVerificationStatus.Corrupt => "Повреждён",
        _ => Status.ToString(),
    };

    public string StatusBadgeColor => Status switch
    {
        BackupVerificationStatus.Verified => "#7BCB62",
        _ => "#D85A45",
    };

    public bool CanRestore => Status == BackupVerificationStatus.Verified;

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
