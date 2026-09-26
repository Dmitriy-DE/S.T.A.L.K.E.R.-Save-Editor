namespace StalkerSaveEditor.Steam;

public sealed record SteamCloudFile(
    string Name,
    int Size,
    long Timestamp,
    bool IsPersisted,
    bool Exists);
