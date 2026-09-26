using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayStackWriter
{
    private const uint MaximumStackCount = ushort.MaxValue;

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.StackCounts.Count == 0)
        {
            throw Error("EditPlan must include at least one stack count.");
        }

        if (plan.Money is not null || plan.DetachHandles.Count > 0)
        {
            throw Error("Stack-only writer does not accept money or delete edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = ReadSupported(sourceBytes);
        if (!CapabilityRegistry.Get(parsed.FormatId, "edit_stacks").Writable)
        {
            throw Error($"Stack editing is not enabled for {parsed.FormatId}.");
        }

        var raw = parsed.Container.Raw.ToArray();
        foreach (var (handle, count) in plan.StackCounts)
        {
            if (count is 0 or > MaximumStackCount)
            {
                throw Error($"Ammo count for 0x{handle:X4} must be in the range 1..{MaximumStackCount}.");
            }

            var item = parsed.Inventory.FirstOrDefault(candidate => candidate.Handle == handle);
            if (item is null || !item.EditableCount ||
                item.StackStateCountOffset is not { } stateOffset ||
                item.StackUpdateCountOffset is not { } updateOffset)
            {
                throw Error($"Object 0x{handle:X4} is not a confirmed editable ammo stack.");
            }

            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(stateOffset), checked((ushort)count));
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(updateOffset), checked((ushort)count));
        }

        var output = parsed.Container.Build(raw);
        var roundTrip = ReadSupported(output);
        if (!string.Equals(roundTrip.FormatId, parsed.FormatId, StringComparison.Ordinal) ||
            !raw.AsSpan().SequenceEqual(roundTrip.Container.Raw.Span))
        {
            throw Error("Stack write did not pass its format and raw-byte round-trip.");
        }

        foreach (var (handle, count) in plan.StackCounts)
        {
            var item = roundTrip.Inventory.FirstOrDefault(candidate => candidate.Handle == handle);
            if (item is null || item.Count != count ||
                item.StackUpdateCountOffset is not { } updateOffset ||
                BinaryPrimitives.ReadUInt16LittleEndian(roundTrip.Container.Raw.Span[updateOffset..]) != count)
            {
                throw Error($"Stack write round-trip did not preserve both counts for 0x{handle:X4}.");
            }
        }

        return new PreparedEdit(plan, output);
    }

    private static XRayTrilogySave ReadSupported(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException originalFailure)
        {
            try
            {
                return XRayEnhancedReader.FromBytes(data);
            }
            catch (XRayFormatException)
            {
                throw originalFailure;
            }
        }
    }

    private static XRayFormatException Error(string message) => new($"X-Ray stack edit: {message}");
}
