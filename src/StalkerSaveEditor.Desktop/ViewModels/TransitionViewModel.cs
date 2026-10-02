using StalkerSaveEditor.Desktop.Services;
namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class TransitionViewModel : ObservableViewModel
{
    public ushort Handle { get; }
    public string Name { get; }
    public string NameReplace { get; }
    public ushort ParentId { get; }
    public int ObjectVersion { get; }

    public string? SourceLevel { get; }
    public string? DestLevel { get; }
    public string? DestPoint { get; }
    public float? PosX { get; }
    public float? PosY { get; }
    public float? PosZ { get; }
    public bool? Silent { get; }

    public TransitionViewModel(
        ushort handle,
        string name,
        string nameReplace,
        ushort parentId = 0,
        int objectVersion = 0)
    {
        Handle = handle;
        Name = name;
        NameReplace = nameReplace;
        ParentId = parentId;
        ObjectVersion = objectVersion;
    }

    public TransitionViewModel(
        string sourceLevel,
        string destLevel,
        string destPoint = "",
        float? posX = null,
        float? posY = null,
        float? posZ = null,
        bool? silent = null)
    {
        SourceLevel = sourceLevel;
        DestLevel = destLevel;
        DestPoint = string.IsNullOrWhiteSpace(destPoint) ? "—" : destPoint;
        PosX = posX;
        PosY = posY;
        PosZ = posZ;
        Silent = silent;
        Name = "level_changer";
        NameReplace = $"{sourceLevel} -> {destLevel}";
    }

    public string DisplayName => !string.IsNullOrWhiteSpace(NameReplace)
        ? NameReplace
        : (!string.IsNullOrWhiteSpace(Name) ? Name : L.T("Объект 0x{0:X4}", Handle));

    public string HandleDisplay => Handle != 0 ? $"0x{Handle:X4} ({Handle})" : "—";
    public string TypeDisplay => string.IsNullOrWhiteSpace(Name) ? "level_changer" : Name;
    public string ParentDisplay => ParentId == 0 ? L.T("0 (мир)") : $"0x{ParentId:X4} ({ParentId})";
    public string VersionDisplay => ObjectVersion != 0 ? $"v{ObjectVersion}" : "—";
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Bound by the view.")]
    public string StatusDisplay => L.T("Только чтение");
}
