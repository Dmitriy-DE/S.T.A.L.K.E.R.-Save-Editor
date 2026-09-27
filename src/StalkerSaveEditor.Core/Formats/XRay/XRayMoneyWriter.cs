using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayMoneyWriter
{
    private const uint MaximumMoney = 2_000_000_000;

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Money is not { } money)
        {
            throw Error("EditPlan must include a money value.");
        }

        if (plan.EditKinds != EditKind.Money)
        {
            throw Error("Money-only writer does not accept stack, delete, add, or stash-transfer edits.");
        }

        if (money > MaximumMoney)
        {
            throw Error($"Money must be in the range 0..{MaximumMoney}.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = ReadSupported(sourceBytes);
        var raw = parsed.Container.Raw.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(parsed.MoneyOffset), money);
        var output = parsed.Container.Build(raw);
        var roundTrip = ReadSupported(output);
        if (roundTrip.FormatId != parsed.FormatId || roundTrip.Money != money)
        {
            throw Error("Money write did not pass its format round-trip.");
        }

        var roundTripRaw = roundTrip.Container.Raw.Span;
        var moneyStart = parsed.MoneyOffset;
        var moneyEnd = checked(moneyStart + sizeof(uint));
        if (roundTripRaw.Length != raw.Length ||
            !raw.AsSpan(0, moneyStart).SequenceEqual(roundTripRaw[..moneyStart]) ||
            !raw.AsSpan(moneyEnd).SequenceEqual(roundTripRaw[moneyEnd..]))
        {
            throw Error("Money write changed bytes outside the actor money field.");
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

    private static XRayFormatException Error(string message) => new($"X-Ray money edit: {message}");
}
