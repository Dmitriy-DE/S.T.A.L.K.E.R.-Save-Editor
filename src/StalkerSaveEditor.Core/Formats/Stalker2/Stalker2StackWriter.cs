using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public static class Stalker2StackWriter
{
    private const int StackCountOffset = 19;
    private const int StackWeightOffset = 24;
    private const double MaximumTotalWeight = 10_000_000;

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.StackCounts.Count == 0)
        {
            throw Error("EditPlan must include at least one stack count.");
        }

        if (plan.Money is not null || plan.DetachHandles.Count > 0 || plan.Adds.Count > 0)
        {
            throw Error("Stack-only writer does not accept money, delete or add edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = Stalker2SaveReader.FromBytes(sourceBytes);
        if (!CapabilityRegistry.Get(parsed.ReleaseId, "edit_stacks").Writable)
        {
            throw Error($"Stack editing is not enabled for {parsed.ReleaseId}.");
        }

        var sourceRaw = parsed.Raw.ToArray();
        var raw = sourceRaw.ToArray();
        foreach (var (handle, count) in plan.StackCounts)
        {
            if (count == 0)
            {
                throw Error($"Stack count for 0x{handle:X8} must be greater than zero.");
            }

            var item = parsed.Inventory.FirstOrDefault(candidate => candidate.Handle == handle);
            if (item is null || !item.EditableCount || item.CountMax < 1 ||
                count > (uint)item.CountMax)
            {
                throw Error($"Object 0x{handle:X8} is not a confirmed editable S2 stack or exceeds its max_stack.");
            }

            var countOffset = checked(item.RecordOffset + StackCountOffset);
            var weightOffset = checked(item.RecordOffset + StackWeightOffset);
            if (countOffset < 0 || weightOffset < 0 ||
                raw.Length - countOffset < sizeof(uint) ||
                raw.Length - weightOffset < sizeof(float))
            {
                throw Error($"Object 0x{handle:X8} has truncated stack state.");
            }

            var totalWeight = (double)item.TotalWeight / item.Count * count;
            if (!double.IsFinite(totalWeight) || totalWeight > MaximumTotalWeight)
            {
                throw Error($"Object 0x{handle:X8} exceeds the safe total-weight range.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(countOffset), count);
            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(weightOffset), (float)totalWeight);
        }

        var compressed = Compress(raw);
        var output = Stalker2ContainerWriter.Build(raw, compressed);
        var roundTrip = Stalker2SaveReader.FromBytes(output);
        if (roundTrip.FormatId != parsed.FormatId || !roundTrip.CrcOk ||
            !raw.AsSpan().SequenceEqual(roundTrip.Raw.Span))
        {
            throw Error("Stack write did not pass its format, CRC and raw-byte round-trip.");
        }

        foreach (var (handle, count) in plan.StackCounts)
        {
            var item = roundTrip.Inventory.FirstOrDefault(candidate => candidate.Handle == handle);
            if (item is null || !item.EditableCount || item.Count != count)
            {
                throw Error($"Stack write round-trip did not preserve count for 0x{handle:X8}.");
            }
        }

        return new PreparedEdit(plan, output);
    }

    private static byte[] Compress(ReadOnlySpan<byte> raw)
    {
        try
        {
            return KrakenCodec.Compress(raw, level: 5);
        }
        catch (InvalidDataException exception)
        {
            throw new Stalker2FormatException(
                $"S.T.A.L.K.E.R. 2 stack edit: Kraken compression failed: {exception.Message}",
                exception);
        }
    }

    private static Stalker2FormatException Error(string message) =>
        new($"S.T.A.L.K.E.R. 2 stack edit: {message}");
}
