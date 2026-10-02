using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Core.Patching;

[JsonConverter(typeof(JsonStringEnumConverter<GameFixCategory>))]
public enum GameFixCategory
{
    Essential,
    Recommended,
    Optional,
    Community,
    Experimental,
}

[JsonConverter(typeof(JsonStringEnumConverter<GameFixMaturity>))]
public enum GameFixMaturity
{
    Validated,
    Experimental,
    ResearchOnly,
}

[JsonConverter(typeof(JsonStringEnumConverter<GameFixState>))]
public enum GameFixState
{
    NotInstalled,
    Installed,
    Removed,
    Modified,
}

public enum GameFixImplementationType
{
    ExactTextReplacement,
    Structured,
    BinaryPatch,
    Overlay,
}

public enum GameFixVerificationState
{
    Research,
    SyntheticTests,
    RetailFilesVerified,
    InGameVerified,
    IssueReproduced,
}

public enum GameFixSaveCompatibility
{
    Unknown,
    ExistingSaves,
    NewGameRequired,
    Incompatible,
}

public sealed record TextPatchOperation(string RelativePath, string ExpectedText, string ReplacementText)
{
    public string? ExpectedFileSha256 { get; init; }

    /// <summary>Single-byte encoding for the target text; Latin-1 preserves legacy byte-oriented patches.</summary>
    public int CodePage { get; init; } = 28591;

    /// <summary>
    /// The Enhanced Edition file already has this change (GSC fixed it there), so the EE variant of the fix is made
    /// from the other patches only.
    /// </summary>
    public bool RetailOnly { get; init; }
}

/// <summary>
/// Places a whole file (a fix pack's file) at <paramref name="RelativePath"/>. <see cref="ExpectedFileSha256"/> is the
/// original the pack was made against (its effective copy in the game's archives or loose files); null means the file
/// must not exist anywhere in the game (a new file). The bytes come from the content store by <paramref name="ContentSha256"/>.
/// </summary>
public sealed record FileOverlayOperation(string RelativePath, string ContentSha256)
{
    public string? ExpectedFileSha256 { get; init; }
}

public sealed record GameFixDefinition(
    string Id,
    GameTarget Game,
    string Version,
    string Title,
    IReadOnlyList<string> SupportedSteamBuildIds,
    GameFixCategory Category,
    GameFixMaturity Maturity,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> ConflictsWith,
    IReadOnlyList<TextPatchOperation> TextPatches,
    string Source)
{
    public string Problem { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public GameFixImplementationType Implementation { get; init; } = GameFixImplementationType.ExactTextReplacement;
    public bool RequiresNewGame { get; init; }
    public GameFixSaveCompatibility SaveCompatibility { get; init; } = GameFixSaveCompatibility.Unknown;
    public GameFixVerificationState VerificationState { get; init; } = GameFixVerificationState.Research;
    public string DetectionMethod { get; init; } = string.Empty;
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>Whole-file operations (<see cref="GameFixImplementationType.Overlay"/>).</summary>
    public IReadOnlyList<FileOverlayOperation> Overlays
    {
        get => _overlays;
        init => _overlays = value ?? []; // catalogue entries written before overlays existed deserialize as null
    }

    private readonly IReadOnlyList<FileOverlayOperation> _overlays = [];

    /// <summary>Structural all.spawn edits (<see cref="GameFixImplementationType.Structured"/>).</summary>
    public IReadOnlyList<SpawnEditOperation> SpawnEdits
    {
        get => _spawnEdits;
        init => _spawnEdits = value ?? [];
    }

    private readonly IReadOnlyList<SpawnEditOperation> _spawnEdits = [];
}

/// <summary>Why a Game Fix operation stopped; lets the UI and the CLI tell "refused" from "the disk failed".</summary>
public enum GameFixFailure
{
    /// <summary>A precondition did not hold (a file changed, a conflict, an unsupported build); nothing is wrong with the disk.</summary>
    Refused,
    /// <summary>Reading or writing failed; the game files were put back.</summary>
    Io,
    /// <summary>The operation failed and the game files could not all be put back.</summary>
    RollbackIncomplete,
}

/// <summary>
/// A failed install, update, removal or preset. Still an <see cref="IOException"/> for existing callers; the
/// original exception is the inner one.
/// </summary>
public sealed class GameFixOperationException(GameFixFailure failure, string message, Exception innerException)
    : IOException(message, innerException)
{
    public GameFixFailure Failure { get; } = failure;

    internal static GameFixFailure KindOf(Exception cause, int rollbackErrors) =>
        rollbackErrors > 0 ? GameFixFailure.RollbackIncomplete
        : cause is GameFixOperationException known ? known.Failure
        : cause is IOException or UnauthorizedAccessException ? GameFixFailure.Io
        : GameFixFailure.Refused;
}

public sealed record GameFixInstallResult(bool Changed, GameFixState State, IReadOnlyList<string> Files);

public sealed record GameFixUninstallCheck(bool CanUninstall, string? Reason, IReadOnlyList<string> Files);

public sealed record GameFixPresetResult(
    GameFixPreset Preset,
    int SelectedFixCount,
    IReadOnlyList<string> InstalledFixIds,
    IReadOnlyList<string> AlreadyInstalledFixIds)
{
    public bool Changed => InstalledFixIds.Count > 0;
}

public sealed record GameFixInstalledInfo(
    string Id,
    GameTarget Game,
    string Version,
    string Title,
    GameFixCategory Category,
    GameFixMaturity Maturity,
    GameFixState State,
    IReadOnlyList<string> Files);

public sealed record GameFixManagedFileStatus(string FixId, string RelativePath, bool Exists, bool MatchesExpectedHash);
