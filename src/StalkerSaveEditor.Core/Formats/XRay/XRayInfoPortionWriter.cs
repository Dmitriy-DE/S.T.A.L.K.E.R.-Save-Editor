using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.XRay;

/// <summary>
/// Adds info portions (story and quest flags) to the actor in an original-trilogy save — the repair for
/// a quest the game left open because a flag was never given (e.g. an NPC died offline). The vector
/// sits in the registry chunk next to the relations, whose layout is already written byte-exactly; SoC
/// and CS store a receive time with each flag (the save's game time is used), CoP does not.
/// </summary>
public static class XRayInfoPortionWriter
{
    public static PreparedEdit AddActorInfo(ReadOnlySpan<byte> source, IReadOnlyCollection<string> infoPortions)
    {
        ArgumentNullException.ThrowIfNull(infoPortions);
        if (infoPortions.Count == 0 || infoPortions.Any(name => string.IsNullOrWhiteSpace(name) || name.Any(c => c is '\0' or > '\u007f')))
        {
            throw new XRayFormatException("Info portions must be non-empty ASCII names.");
        }

        var sourceBytes = source.ToArray();
        var parsed = XRayTrilogyReader.FromBytes(sourceBytes);
        if (parsed.FormatId is not ("stalker-soc" or "stalker-cs" or "stalker-cop"))
        {
            throw new XRayFormatException($"Adding info portions is not enabled for {parsed.FormatId}.");
        }

        var registry = parsed.RelationRegistry
            ?? throw new XRayFormatException("The registry chunk of this save is not readable.");
        var row = registry.InfoFor(parsed.ActorId);
        var known = row?.Names ?? [];
        var hasTimestamps = parsed.FormatId != "stalker-cop";
        var missing = infoPortions.Distinct(StringComparer.Ordinal).Where(name => !known.Contains(name, StringComparer.Ordinal)).ToArray();
        if (missing.Length == 0)
        {
            throw new XRayFormatException("The actor already knows every requested info portion.");
        }

        var chunk = XRayFactionWriter.GetSingleRelationChunk(parsed.Container);
        var payload = chunk.Data.Span;
        var added = new List<byte>();
        foreach (var name in missing)
        {
            added.AddRange(Encoding.ASCII.GetBytes(name));
            added.Add(0);
            if (hasTimestamps)
            {
                var time = new byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(time, parsed.GameTime);
                added.AddRange(time);
            }
        }

        byte[] updated;
        if (row is not null)
        {
            // Append to the actor's vector and raise its count.
            updated = Insert(payload, row.EndOffset, added);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(payload[row.CountOffset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(updated.AsSpan(row.CountOffset), count + (uint)missing.Length);
        }
        else
        {
            // No vector yet: a new map entry (actor id, count, names) at the end of the info-portion map.
            var entry = new List<byte>();
            var header = new byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(header, parsed.ActorId);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), (uint)missing.Length);
            entry.AddRange(header);
            entry.AddRange(added);
            updated = Insert(payload, registry.InfoSectionEnd, entry);
            var rows = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            BinaryPrimitives.WriteUInt32LittleEndian(updated, rows + 1);
        }

        var output = parsed.Container.Build(XRayFactionWriter.ReplaceChunkData(parsed.Container, chunk, updated));
        var after = XRayTrilogyReader.FromBytes(output);
        if (after.FormatId != parsed.FormatId ||
            !missing.All(name => after.ActorKnownInfo.Contains(name, StringComparer.Ordinal)) ||
            after.ActorKnownInfo.Count != known.Count + missing.Length ||
            after.Inventory.Count != parsed.Inventory.Count ||
            after.Money != parsed.Money)
        {
            throw new XRayFormatException("Adding info portions did not pass its read-back.");
        }

        return PreparedEdit.Replacing(Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(), output);
    }

    private static byte[] Insert(ReadOnlySpan<byte> payload, int offset, List<byte> bytes)
    {
        var updated = new byte[payload.Length + bytes.Count];
        payload[..offset].CopyTo(updated);
        bytes.CopyTo(updated, offset);
        payload[offset..].CopyTo(updated.AsSpan(offset + bytes.Count));
        return updated;
    }
}
