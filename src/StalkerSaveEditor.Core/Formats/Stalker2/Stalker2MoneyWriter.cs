using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public static class Stalker2MoneyWriter
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
            throw Error("Money-only writer does not accept stack, delete, add or stash-transfer edits.");
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

        var parsed = Stalker2SaveReader.FromBytes(sourceBytes);
        if (parsed.Money == money)
        {
            return new PreparedEdit(plan, sourceBytes);
        }

        var raw = parsed.Raw.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(parsed.Layout.MoneyOffset), money);
        byte[] compressed;
        try
        {
            compressed = KrakenCodec.Compress(raw, level: 5);
        }
        catch (InvalidDataException exception)
        {
            throw new Stalker2FormatException(
                $"S.T.A.L.K.E.R. 2 money edit: Kraken compression failed: {exception.Message}",
                exception);
        }

        var output = Stalker2ContainerWriter.Build(raw, compressed);
        var roundTrip = Stalker2SaveReader.FromBytes(output);
        if (roundTrip.Money != money || !roundTrip.CrcOk)
        {
            throw Error("Money write did not pass its container round-trip.");
        }

        var moneyStart = parsed.Layout.MoneyOffset;
        var moneyEnd = checked(moneyStart + sizeof(uint));
        var roundTripRaw = roundTrip.Raw.Span;
        if (roundTripRaw.Length != raw.Length ||
            !raw.AsSpan(0, moneyStart).SequenceEqual(roundTripRaw[..moneyStart]) ||
            !raw.AsSpan(moneyEnd).SequenceEqual(roundTripRaw[moneyEnd..]))
        {
            throw Error("Money write changed bytes outside the wallet field.");
        }

        return new PreparedEdit(plan, output);
    }

    private static Stalker2FormatException Error(string message) =>
        new($"S.T.A.L.K.E.R. 2 money edit: {message}");
}
