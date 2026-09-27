namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class TransitionViewModel(
    string sourceLevel,
    string destLevel,
    string destPoint,
    float? posX,
    float? posY,
    float? posZ,
    bool? silent) : ObservableViewModel
{
    public string SourceLevel { get; } = sourceLevel;
    public string DestLevel { get; } = destLevel;
    public string DestPoint { get; } = string.IsNullOrWhiteSpace(destPoint) ? "—" : destPoint;
    public float? PosX { get; } = posX;
    public float? PosY { get; } = posY;
    public float? PosZ { get; } = posZ;
    public bool? Silent { get; } = silent;

    public string CoordinatesDisplay => (PosX.HasValue && PosY.HasValue && PosZ.HasValue)
        ? $"{PosX.Value:F1}, {PosY.Value:F1}, {PosZ.Value:F1}"
        : "—";

    public string SilentDisplay => Silent.HasValue ? (Silent.Value ? "Да" : "Нет") : "—";
}
