using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public static class Stalker2DurabilityWriter
{
    private const uint MaximumMoney = 2_000_000_000;
    private const int StackCountOffset = 19;
    private const int StackWeightOffset = 24;
    private const double MaximumTotalWeight = 10_000_000;

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Durability.Count == 0)
        {
            throw Error("EditPlan must include at least one durability value.");
        }

        const EditKind supportedKinds = EditKind.Money | EditKind.StackCounts | EditKind.Durability;
        if ((plan.EditKinds & ~supportedKinds) != EditKind.None)
        {
            throw Error("S2 durability writer does not accept structural or stash-transfer edit kinds.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Sha256(sourceBytes);
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = Stalker2SaveReader.FromBytes(sourceBytes);
        if (!CapabilityRegistry.Get(parsed.ReleaseId, "edit_durability").Writable)
        {
            throw Error($"Durability editing is not enabled for {parsed.ReleaseId}.");
        }

        if (plan.Money is { } money)
        {
            if (money > MaximumMoney || !CapabilityRegistry.Get(parsed.ReleaseId, "edit_money").Writable)
            {
                throw Error($"Money edit is not permitted for {parsed.ReleaseId} or exceeds {MaximumMoney}.");
            }
        }

        if (plan.StackCounts.Count > 0 && !CapabilityRegistry.Get(parsed.ReleaseId, "edit_stacks").Writable)
        {
            throw Error($"Stack editing is not enabled for {parsed.ReleaseId}.");
        }

        var sourceRaw = parsed.Raw.ToArray();
        var raw = sourceRaw.ToArray();
        var allowedRanges = new List<(int Start, int Length)>();
        if (plan.Money is { } targetMoney)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(parsed.Layout.MoneyOffset), targetMoney);
            allowedRanges.Add((parsed.Layout.MoneyOffset, sizeof(uint)));
        }

        foreach (var (handle, count) in plan.StackCounts)
        {
            var item = parsed.Inventory.FirstOrDefault(value => value.Handle == handle);
            if (count == 0 || item is null || !item.EditableCount || count > (uint)item.CountMax)
            {
                throw Error($"Object 0x{handle:X8} is not a confirmed editable S2 stack or exceeds its max_stack.");
            }

            var countOffset = checked(item.RecordOffset + StackCountOffset);
            var weightOffset = checked(item.RecordOffset + StackWeightOffset);
            if (raw.Length - countOffset < sizeof(uint) || raw.Length - weightOffset < sizeof(float))
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
            allowedRanges.Add((countOffset, sizeof(uint)));
            allowedRanges.Add((weightOffset, sizeof(float)));
        }

        var targets = new Dictionary<uint, float>();
        foreach (var (handle, condition) in plan.Durability)
        {
            var item = parsed.Inventory.FirstOrDefault(value => value.Handle == handle);
            if (item is null || !item.ConditionEditable || item.ConditionOffset is not { } conditionOffset)
            {
                throw Error($"S2 condition for handle 0x{handle:X8} is not confirmed by the reader.");
            }

            var target = (float)condition;
            if (!double.IsFinite(condition) || condition is < 0 or > 1 || !float.IsFinite(target))
            {
                throw Error($"S2 condition for handle 0x{handle:X8} must be finite and in the range 0..1.");
            }

            var confirmed = item.KindCode switch
            {
                1 when Stalker2ItemState.IsArmorName(item.DisplayName) &&
                    Stalker2ItemState.HasEquipmentShape(raw, handle, item.RecordOffset, item.KindCode) =>
                    Stalker2ItemState.ReadArmorCondition(raw, handle, item.RecordOffset, item.KindCode)?.ValueOffset,
                0 when parsed.NameTables is not null =>
                    Stalker2ItemState.ReadWeaponCondition(
                        raw,
                        handle,
                        item.RecordOffset,
                        item.RecordEndGuess,
                        item.KindCode,
                        parsed.NameTables)?.ValueOffset,
                _ => null,
            };
            if (confirmed != conditionOffset || raw.Length - conditionOffset < sizeof(float))
            {
                throw Error($"S2 condition anchor for handle 0x{handle:X8} changed or is ambiguous.");
            }

            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(conditionOffset), target);
            targets.Add(handle, target);
            allowedRanges.Add((conditionOffset, sizeof(float)));
        }

        EnsureOnlyConfirmedRangesChanged(sourceRaw, raw, allowedRanges);
        if (raw.AsSpan().SequenceEqual(sourceRaw))
        {
            return new PreparedEdit(plan, sourceBytes);
        }

        byte[] compressed;
        try
        {
            compressed = KrakenCodec.Compress(raw, level: 5);
        }
        catch (InvalidDataException exception)
        {
            throw new Stalker2FormatException(
                $"S.T.A.L.K.E.R. 2 durability edit: Kraken compression failed: {exception.Message}",
                exception);
        }

        var output = Stalker2ContainerWriter.Build(raw, compressed);
        var roundTrip = Stalker2SaveReader.FromBytes(output);
        if (!roundTrip.CrcOk || !raw.AsSpan().SequenceEqual(roundTrip.Raw.Span))
        {
            throw Error("Durability write did not pass its CRC and raw-byte round-trip.");
        }

        if (plan.Money is { } expectedMoney && roundTrip.Money != expectedMoney)
        {
            throw Error("Money did not match after the combined edit round-trip.");
        }

        foreach (var (handle, count) in plan.StackCounts)
        {
            var item = roundTrip.Inventory.FirstOrDefault(value => value.Handle == handle);
            if (item is null || !item.EditableCount || item.Count != count)
            {
                throw Error($"Stack write round-trip did not preserve count for 0x{handle:X8}.");
            }
        }

        foreach (var (handle, condition) in targets)
        {
            var item = roundTrip.Inventory.FirstOrDefault(value => value.Handle == handle);
            if (item is null || !item.ConditionEditable || item.Condition is not { } actual ||
                Math.Abs(actual - condition) > 1e-6f)
            {
                throw Error($"Durability write round-trip did not preserve condition for 0x{handle:X8}.");
            }
        }

        return new PreparedEdit(plan, output);
    }

    private static void EnsureOnlyConfirmedRangesChanged(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> output,
        IReadOnlyList<(int Start, int Length)> allowedRanges)
    {
        // Everything outside the confirmed ranges must be identical: compared block by block (vectorised), not byte
        // by byte, since this runs over the whole unpacked save.
        var position = 0;
        foreach (var (start, length) in allowedRanges.OrderBy(range => range.Start))
        {
            if (start > position) EnsureEqual(source, output, position, start);
            position = Math.Max(position, start + length);
        }

        if (position < source.Length) EnsureEqual(source, output, position, source.Length);
    }

    private static void EnsureEqual(ReadOnlySpan<byte> source, ReadOnlySpan<byte> output, int start, int end)
    {
        var left = source[start..end];
        var right = output[start..end];
        if (left.SequenceEqual(right)) return;
        throw Error($"S2 durability edit changed unconfirmed raw byte at offset {start + left.CommonPrefixLength(right)}.");
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static Stalker2FormatException Error(string message) =>
        new($"S.T.A.L.K.E.R. 2 durability edit: {message}");
}
