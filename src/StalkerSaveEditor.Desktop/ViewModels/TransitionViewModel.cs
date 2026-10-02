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
        int objectVersion = 0,
        string? sourceLevel = null,
        string? destLevel = null,
        string? destPoint = null)
    {
        Handle = handle;
        Name = name;
        NameReplace = nameReplace;
        ParentId = parentId;
        ObjectVersion = objectVersion;
        SourceLevel = sourceLevel;
        DestLevel = destLevel;
        DestPoint = destPoint;
    }

    /// <summary>"Zaton → Jupiter" when both ends are known, the destination alone otherwise, else the object's name.</summary>
    public string RouteDisplay => (SourceLevel, DestLevel) switch
    {
        ({ Length: > 0 } from, { Length: > 0 } to) => $"{from} → {to}",
        (_, { Length: > 0 } to) => $"→ {to}",
        ({ Length: > 0 } from, _) => $"{from} → …",
        _ => DisplayName,
    };

    /// <summary>The names the save itself uses: the object and the point the player arrives at.</summary>
    public string TechnicalDisplay => string.IsNullOrEmpty(DestPoint) ? DisplayName : $"{DisplayName} · {DestPoint}";

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
