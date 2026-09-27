using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayUpgradeWriter
{
    private const int MaximumUpgradeCount = 1_000_000;
    private const int MaximumUpgradeKeyBytes = 1 << 20;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan, UpgradeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);
        if (plan.Upgrades.Count == 0)
        {
            throw Error("EditPlan must include at least one item upgrade vector.");
        }

        if (plan.Money is not null || plan.StackCounts.Count > 0 || plan.DetachHandles.Count > 0 ||
            plan.Adds.Count > 0 || plan.StashTakes.Count > 0 || plan.StashPuts.Count > 0)
        {
            throw Error("Upgrade-only writer does not accept money, stack, delete, add, or stash-transfer edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Sha256(sourceBytes);
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var initial = ReadSupported(sourceBytes);
        var formatId = initial.FormatId;
        if (!SupportsUpgrades(formatId))
        {
            throw Error($"Upgrade editing is not supported for {formatId}.");
        }

        var catalogRelease = BaseReleaseId(formatId);
        var catalogMatches = string.Equals(catalog.ReleaseId, formatId, StringComparison.Ordinal) ||
            string.Equals(catalog.ReleaseId, catalogRelease, StringComparison.Ordinal);
        if (!catalogMatches || catalog.Upgrades.Count == 0)
        {
            throw Error($"Upgrade catalog '{catalog.ReleaseId}' does not match {formatId}.");
        }

        var working = sourceBytes;
        foreach (var (handle, desired) in plan.Upgrades)
        {
            var current = ReadSupported(working);
            EnsureFormat(current, formatId);
            var record = current.RegistryObjects.FirstOrDefault(item => item.ObjectId == handle)
                ?? throw Error($"Object 0x{handle:X4} does not exist.");
            if (record.ParentId != current.ActorId)
            {
                throw Error($"Object 0x{handle:X4} is not owned by the actor inventory.");
            }

            if (record.Version <= 123 ||
                !XRayTrilogyReader.TryReadUpgrades(
                    current.Container.Raw.Span,
                    record.Version,
                    record.StateOffset,
                    record.StateLength,
                    out var existing,
                    out var vectorOffset,
                    out var vectorLength))
            {
                throw Error($"Object 0x{handle:X4} has no confirmed upgrade vector.");
            }

            ValidateRequestedKeys(desired, existing, catalog, record.Name);
            if (existing.SequenceEqual(desired, StringComparer.Ordinal))
            {
                continue;
            }

            var raw = current.Container.Raw.Span;
            var replacement = ReplaceUpgradeVector(raw, record, vectorOffset, vectorLength, desired, handle);
            working = XRayAddWriter.ReplaceRecord(current.Container, record, replacement);
        }

        var roundTrip = ReadSupported(working);
        EnsureFormat(roundTrip, formatId);
        foreach (var (handle, desired) in plan.Upgrades)
        {
            var item = roundTrip.Inventory.FirstOrDefault(candidate => candidate.Handle == handle);
            if (item is null || item.Upgrades is null ||
                !item.Upgrades.SequenceEqual(desired, StringComparer.Ordinal))
            {
                throw Error($"Upgrade write round-trip did not match object 0x{handle:X4}.");
            }
        }

        return new PreparedEdit(plan, working);
    }

    private static byte[] ReplaceUpgradeVector(
        ReadOnlySpan<byte> raw,
        XRayRegistryObject item,
        int vectorOffset,
        int vectorLength,
        IReadOnlyList<string> desired,
        ushort handle)
    {
        var recordStart = item.RecordOffset;
        var recordEnd = checked(recordStart + item.RecordLength);
        if (recordStart < 0 || recordEnd > raw.Length || item.RecordLength < 4)
        {
            throw Error($"Object 0x{handle:X4} has invalid registry record boundaries.");
        }

        var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(raw[recordStart..]);
        var spawnStart = checked(recordStart + sizeof(ushort));
        var spawnEnd = checked(spawnStart + spawnLength);
        if (spawnLength == 0 || spawnEnd + sizeof(ushort) > recordEnd)
        {
            throw Error($"Object 0x{handle:X4} has an invalid SPAWN boundary.");
        }

        var updateLength = BinaryPrimitives.ReadUInt16LittleEndian(raw[spawnEnd..]);
        var updateStart = checked(spawnEnd + sizeof(ushort));
        if (checked(updateStart + updateLength) != recordEnd ||
            vectorOffset < spawnStart || vectorLength <= 0 || checked(vectorOffset + vectorLength) > spawnEnd)
        {
            throw Error($"Object 0x{handle:X4} has invalid upgrade or UPDATE boundaries.");
        }

        var stateSizeOffset = checked(item.StateOffset - sizeof(ushort));
        if (stateSizeOffset < spawnStart || stateSizeOffset + sizeof(ushort) > spawnEnd)
        {
            throw Error($"Object 0x{handle:X4} has an invalid STATE size field.");
        }

        var encoded = EncodeVector(desired, handle);
        var delta = checked(encoded.Length - vectorLength);
        var currentStateSize = BinaryPrimitives.ReadUInt16LittleEndian(raw[stateSizeOffset..]);
        var newStateSize = checked(currentStateSize + delta);
        var newSpawnLength = checked(spawnLength + delta);
        if (newStateSize is < 2 or > ushort.MaxValue || newSpawnLength is < 1 or > ushort.MaxValue)
        {
            throw Error($"Object 0x{handle:X4} exceeds its serialized STATE or SPAWN size field.");
        }

        var newSpawn = new byte[newSpawnLength];
        var vectorStartInSpawn = vectorOffset - spawnStart;
        var vectorEndInSpawn = checked(vectorStartInSpawn + vectorLength);
        var stateSizeOffsetInSpawn = stateSizeOffset - spawnStart;
        raw.Slice(spawnStart, vectorStartInSpawn).CopyTo(newSpawn);
        encoded.CopyTo(newSpawn.AsSpan(vectorStartInSpawn));
        raw.Slice(spawnStart + vectorEndInSpawn, spawnLength - vectorEndInSpawn)
            .CopyTo(newSpawn.AsSpan(vectorStartInSpawn + encoded.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(newSpawn.AsSpan(stateSizeOffsetInSpawn), checked((ushort)newStateSize));

        var replacement = new byte[checked(sizeof(ushort) + newSpawn.Length + sizeof(ushort) + updateLength)];
        BinaryPrimitives.WriteUInt16LittleEndian(replacement, checked((ushort)newSpawn.Length));
        newSpawn.CopyTo(replacement, sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(replacement.AsSpan(sizeof(ushort) + newSpawn.Length), updateLength);
        raw.Slice(updateStart, updateLength).CopyTo(
            replacement.AsSpan(sizeof(ushort) + newSpawn.Length + sizeof(ushort)));
        return replacement;
    }

    private static byte[] EncodeVector(IReadOnlyList<string> values, ushort handle)
    {
        if (values.Count > MaximumUpgradeCount)
        {
            throw Error($"Object 0x{handle:X4} has too many upgrades.");
        }

        using var output = new MemoryStream();
        Span<byte> count = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(count, checked((uint)values.Count));
        output.Write(count);
        foreach (var value in values)
        {
            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
            }
            catch (EncoderFallbackException exception)
            {
                throw Error($"Object 0x{handle:X4} contains an invalid UTF-8 upgrade key: {exception.Message}");
            }

            if (byteCount > MaximumUpgradeKeyBytes)
            {
                throw Error($"Object 0x{handle:X4} contains an oversized upgrade key.");
            }

            if (output.Length + byteCount + 1 > ushort.MaxValue)
            {
                throw Error($"Object 0x{handle:X4} upgrade vector exceeds its serialized size field.");
            }

            var encoded = StrictUtf8.GetBytes(value);
            output.Write(encoded);
            output.WriteByte(0);
        }

        return output.ToArray();
    }

    private static void ValidateRequestedKeys(
        IReadOnlyList<string> desired,
        IReadOnlyList<string> existing,
        UpgradeCatalog catalog,
        string itemKey)
    {
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);
        foreach (var key in desired)
        {
            if (existingSet.Contains(key))
            {
                continue;
            }

            var definition = catalog.Resolve(key)
                ?? throw Error($"Upgrade '{key}' is absent from the upgrade catalog.");
            if (!definition.AppliesTo(itemKey))
            {
                throw Error($"Upgrade '{key}' does not apply to item '{itemKey}'.");
            }
        }
    }

    private static bool SupportsUpgrades(string formatId) => formatId is
        "stalker-cs" or "stalker-cop" or "stalker-cs-ee" or "stalker-cop-ee";

    private static string BaseReleaseId(string formatId) => formatId.EndsWith("-ee", StringComparison.Ordinal)
        ? formatId[..^3]
        : formatId;

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

    private static void EnsureFormat(XRayTrilogySave parsed, string expectedFormat)
    {
        if (!string.Equals(parsed.FormatId, expectedFormat, StringComparison.Ordinal))
        {
            throw Error("The X-Ray format changed during upgrade editing.");
        }
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static XRayFormatException Error(string message) => new($"X-Ray upgrade edit: {message}");
}
