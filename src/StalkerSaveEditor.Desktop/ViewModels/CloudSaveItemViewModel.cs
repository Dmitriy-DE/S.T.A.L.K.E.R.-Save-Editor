using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class CloudSaveItemViewModel : ObservableViewModel
{
    public CloudSaveItemViewModel(CloudFileModel model)
    {
        Model = model;
    }

    public CloudFileModel Model { get; }

    public string FileName => Model.FileName;
    public string RemotePath => Model.RemotePath;
    public int AppId => Model.AppId;
    public string ReleaseId => Model.ReleaseId;

    public string GameName => Model.AppId switch
    {
        4500 => "Тень Чернобыля",
        20510 => "Чистое Небо",
        41700 => "Зов Припяти",
        1643320 => "S.T.A.L.K.E.R. 2",
        _ => "X-Ray"
    };

    public string SizeText => $"{Model.FileSizeBytes / 1024.0:F1} КБ";

    public string TimestampText => Model.RemoteTimestampUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    public CloudComparison Comparison => Model.Comparison;

    public string ComparisonBadgeText => Model.Comparison switch
    {
        CloudComparison.Identical => "Совпадает",
        CloudComparison.RemoteNewer => "Облачный новее",
        CloudComparison.LocalNewer => "Локальный новее",
        CloudComparison.RemoteOnly => "Только в облаке",
        CloudComparison.LocalOnly => "Только локально",
        _ => "Неизвестно"
    };

    public string ComparisonBadgeColor => Model.Comparison switch
    {
        CloudComparison.Identical => "#4E7A4A",
        CloudComparison.RemoteNewer => "#4A7A9E",
        CloudComparison.LocalNewer => "#C9A038",
        CloudComparison.RemoteOnly => "#777777",
        _ => "#555555"
    };

    public bool HasLocalFile => !string.IsNullOrEmpty(Model.LocalFilePath) && System.IO.File.Exists(Model.LocalFilePath);
}
