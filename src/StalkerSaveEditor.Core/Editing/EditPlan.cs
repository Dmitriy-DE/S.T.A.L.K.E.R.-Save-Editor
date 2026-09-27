using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Editing;

public sealed record EditPlan
{
    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    public EditPlan(
        string sourceSha256,
        uint? money = null,
        IReadOnlyDictionary<uint, uint>? stackCounts = null,
        IReadOnlyCollection<ushort>? detachHandles = null,
        IReadOnlyCollection<ItemAddRequest>? adds = null,
        IReadOnlyCollection<ushort>? stashTakes = null,
        IReadOnlyCollection<StashPutRequest>? stashPuts = null,
        IReadOnlyDictionary<ushort, IReadOnlyList<string>>? upgrades = null,
        string? playerFaction = null,
        IReadOnlyDictionary<string, int>? factionRelations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        if (!Sha256Pattern.IsMatch(sourceSha256))
        {
            throw new ArgumentException("Source SHA256 must contain 64 hexadecimal characters.", nameof(sourceSha256));
        }

        SourceSha256 = sourceSha256.ToLowerInvariant();
        Money = money;
        StackCounts = new ReadOnlyDictionary<uint, uint>(
            stackCounts is null
                ? new Dictionary<uint, uint>()
                : new Dictionary<uint, uint>(stackCounts));
        var handles = detachHandles?.ToArray() ?? [];
        if (handles.Distinct().Count() != handles.Length)
        {
            throw new ArgumentException("Detach handles must be unique.", nameof(detachHandles));
        }

        DetachHandles = Array.AsReadOnly(handles);

        var additions = adds?.ToArray() ?? [];
        if (additions.Any(request => request is null))
        {
            throw new ArgumentException("Add requests must not contain null values.", nameof(adds));
        }

        Adds = Array.AsReadOnly(additions);

        var stashTakeHandles = stashTakes?.ToArray() ?? [];
        if (stashTakeHandles.Any(handle => handle is 0 or ushort.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(stashTakes), "Stash item handles must be in the range 1…65534.");
        }

        if (stashTakeHandles.Distinct().Count() != stashTakeHandles.Length)
        {
            throw new ArgumentException("Stash take handles must be unique.", nameof(stashTakes));
        }

        StashTakes = Array.AsReadOnly(stashTakeHandles);
        var stashPutRequests = stashPuts?.ToArray() ?? [];
        if (stashPutRequests.Any(request => request is null))
        {
            throw new ArgumentException("Stash put requests must not contain null values.", nameof(stashPuts));
        }

        if (stashPutRequests.Select(request => request.ObjectId).Distinct().Count() != stashPutRequests.Length)
        {
            throw new ArgumentException("Stash put object ids must be unique.", nameof(stashPuts));
        }

        if (stashTakeHandles.Intersect(stashPutRequests.Select(request => request.ObjectId)).Any())
        {
            throw new ArgumentException("An item cannot be taken from and put into a stash in one edit plan.");
        }

        if (handles.Intersect(stashTakeHandles).Any() ||
            handles.Intersect(stashPutRequests.Select(request => request.ObjectId)).Any())
        {
            throw new ArgumentException("An item cannot be removed and moved to or from a stash in one edit plan.");
        }

        StashPuts = Array.AsReadOnly(stashPutRequests);

        var upgradeEdits = new Dictionary<ushort, IReadOnlyList<string>>();
        if (upgrades is not null)
        {
            foreach (var (handle, requestedKeys) in upgrades)
            {
                if (requestedKeys is null)
                {
                    throw new ArgumentException("Upgrade vectors must not be null.", nameof(upgrades));
                }

                var keys = requestedKeys.ToArray();
                if (keys.Any(key => string.IsNullOrEmpty(key) || key.Contains('\0')))
                {
                    throw new ArgumentException("Upgrade keys must be non-empty and must not contain NUL.", nameof(upgrades));
                }

                if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
                {
                    throw new ArgumentException("Upgrade keys must be unique per item.", nameof(upgrades));
                }

                upgradeEdits.Add(handle, Array.AsReadOnly(keys));
            }
        }

        Upgrades = new ReadOnlyDictionary<ushort, IReadOnlyList<string>>(upgradeEdits);
        if (playerFaction is not null && string.IsNullOrWhiteSpace(playerFaction))
        {
            throw new ArgumentException("Player faction key must not be empty.", nameof(playerFaction));
        }

        PlayerFaction = playerFaction;
        var relationValues = factionRelations is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(factionRelations, StringComparer.Ordinal);
        if (relationValues.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Faction relation keys must not be empty.", nameof(factionRelations));
        }

        FactionRelations = new ReadOnlyDictionary<string, int>(relationValues);
        EditKinds = DetermineEditKinds();
    }

    public string SourceSha256 { get; }

    public uint? Money { get; }

    public IReadOnlyDictionary<uint, uint> StackCounts { get; }

    public IReadOnlyList<ushort> DetachHandles { get; }

    public IReadOnlyList<ItemAddRequest> Adds { get; }

    public IReadOnlyList<ushort> StashTakes { get; }

    public IReadOnlyList<StashPutRequest> StashPuts { get; }

    public IReadOnlyDictionary<ushort, IReadOnlyList<string>> Upgrades { get; }

    public string? PlayerFaction { get; }

    public IReadOnlyDictionary<string, int> FactionRelations { get; }

    public EditKind EditKinds { get; }

    private EditKind DetermineEditKinds()
    {
        var kinds = EditKind.None;
        if (Money is not null)
        {
            kinds |= EditKind.Money;
        }

        if (StackCounts.Count > 0)
        {
            kinds |= EditKind.StackCounts;
        }

        if (DetachHandles.Count > 0)
        {
            kinds |= EditKind.Delete;
        }

        if (Adds.Count > 0)
        {
            kinds |= EditKind.Add;
        }

        if (StashTakes.Count > 0 || StashPuts.Count > 0)
        {
            kinds |= EditKind.XRayStashTransfer;
        }

        if (Upgrades.Count > 0)
        {
            kinds |= EditKind.Upgrades;
        }

        if (PlayerFaction is not null || FactionRelations.Count > 0)
        {
            kinds |= EditKind.Faction;
        }

        return kinds;
    }
}
