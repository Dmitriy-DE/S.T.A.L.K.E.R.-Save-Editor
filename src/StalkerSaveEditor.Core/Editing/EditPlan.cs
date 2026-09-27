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
        IReadOnlyCollection<StashPutRequest>? stashPuts = null)
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
    }

    public string SourceSha256 { get; }

    public uint? Money { get; }

    public IReadOnlyDictionary<uint, uint> StackCounts { get; }

    public IReadOnlyList<ushort> DetachHandles { get; }

    public IReadOnlyList<ItemAddRequest> Adds { get; }

    public IReadOnlyList<ushort> StashTakes { get; }

    public IReadOnlyList<StashPutRequest> StashPuts { get; }
}
