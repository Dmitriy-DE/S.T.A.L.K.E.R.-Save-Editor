using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayDurabilityWriter
{
    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Durability.Count == 0)
        {
            throw Error("EditPlan must include at least one durability value.");
        }

        if (plan.EditKinds != EditKind.Durability)
        {
            throw Error("Durability-only writer does not accept money, stack, add, delete, or stash-transfer edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Sha256(sourceBytes);
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = ReadSupported(sourceBytes);
        var raw = parsed.Container.Raw.ToArray();
        foreach (var (handle, condition) in plan.Durability)
        {
            var item = handle <= ushort.MaxValue
                ? parsed.Inventory.FirstOrDefault(candidate => candidate.Handle == handle)
                : null;
            if (item is null || !item.ConditionEditable ||
                item.ConditionStateOffset is not { } stateOffset || item.Condition is null)
            {
                throw Error($"Object 0x{handle:X4} is not a confirmed editable equipment condition.");
            }

            WriteSingle(raw, stateOffset, (float)condition);
            if (item.ConditionUpdateOffset is { } updateOffset)
            {
                raw[updateOffset] = EncodeUpdateCondition(condition);
            }

            if (item.ClientConditionOffset is { } clientOffset)
            {
                WriteSingle(raw, clientOffset, (float)condition);
            }
        }

        var output = parsed.Container.Build(raw);
        var roundTrip = ReadSupported(output);
        if (!string.Equals(roundTrip.FormatId, parsed.FormatId, StringComparison.Ordinal) ||
            !raw.AsSpan().SequenceEqual(roundTrip.Container.Raw.Span))
        {
            throw Error("Durability write did not pass its format and raw-byte round-trip.");
        }

        foreach (var (handle, condition) in plan.Durability)
        {
            var item = handle <= ushort.MaxValue
                ? roundTrip.Inventory.FirstOrDefault(candidate => candidate.Handle == handle)
                : null;
            if (item is null || item.Condition is not { } actual ||
                Math.Abs(actual - condition) > 1e-6d)
            {
                throw Error($"Durability round-trip for object 0x{handle:X4} did not match.");
            }

            if (item.ConditionUpdateOffset is { } updateOffset)
            {
                var expected = EncodeUpdateCondition(condition);
                if (roundTrip.Container.Raw.Span[updateOffset] != expected)
                {
                    throw Error($"Durability UPDATE mirror for object 0x{handle:X4} did not match.");
                }
            }

            if (item.ClientConditionOffset is { } clientOffset)
            {
                var clientCondition = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(roundTrip.Container.Raw.Span[clientOffset..]));
                if (!float.IsFinite(clientCondition) || Math.Abs(clientCondition - condition) > 1e-6d)
                {
                    throw Error($"Durability client-data mirror for object 0x{handle:X4} did not match.");
                }
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

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], BitConverter.SingleToInt32Bits(value));

    private static byte EncodeUpdateCondition(double value) =>
        (byte)Math.Clamp((int)Math.Floor(value * 255d + 0.5d), 0, 255);

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static XRayFormatException Error(string message) => new($"X-Ray durability edit: {message}");
}
