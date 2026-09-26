using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Editing;

public sealed record EditPlan
{
    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    public EditPlan(
        string sourceSha256,
        uint? money = null,
        IReadOnlyDictionary<ushort, uint>? stackCounts = null,
        IReadOnlyCollection<ushort>? detachHandles = null,
        IReadOnlyCollection<ItemAddRequest>? adds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        if (!Sha256Pattern.IsMatch(sourceSha256))
        {
            throw new ArgumentException("Source SHA256 must contain 64 hexadecimal characters.", nameof(sourceSha256));
        }

        SourceSha256 = sourceSha256.ToLowerInvariant();
        Money = money;
        StackCounts = new ReadOnlyDictionary<ushort, uint>(
            stackCounts is null
                ? new Dictionary<ushort, uint>()
                : new Dictionary<ushort, uint>(stackCounts));
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
    }

    public string SourceSha256 { get; }

    public uint? Money { get; }

    public IReadOnlyDictionary<ushort, uint> StackCounts { get; }

    public IReadOnlyList<ushort> DetachHandles { get; }

    public IReadOnlyList<ItemAddRequest> Adds { get; }
}
