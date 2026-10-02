using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static partial class XRayTrilogyReader
{
    private static List<ObjectRecord> ParseObjects(XRayChunk chunk)
    {
        var reader = new SpanReader(chunk.Data.Span, "OBJECT chunk");
        var count = reader.ReadUInt32();
        if (count == 0 || count > MaximumObjects)
        {
            throw Error($"OBJECT count={count} вне диапазона");
        }

        var dataOffset = checked(chunk.Offset + 8);
        var records = new List<ObjectRecord>(checked((int)count));
        var seenIds = new HashSet<ushort>();
        for (var index = 0; index < count; index++)
        {
            var recordRelativeOffset = reader.Position;
            var spawnSize = reader.ReadUInt16();
            var spawnRelativeOffset = reader.Position;
            var spawnPacket = reader.ReadBytes(spawnSize);
            var spawn = ParseSpawn(spawnPacket, dataOffset + spawnRelativeOffset);
            var updateSize = reader.ReadUInt16();
            var updateRelativeOffset = reader.Position;
            var updatePacket = reader.ReadBytes(updateSize);
            if (updatePacket.Length < sizeof(ushort) ||
                BinaryPrimitives.ReadUInt16LittleEndian(updatePacket) != UpdateMessage)
            {
                throw Error($"объект '{spawn.Name}': UPDATE не начинается с M_UPDATE");
            }

            if (!seenIds.Add(spawn.ObjectId))
            {
                throw Error($"повторяющийся object id 0x{spawn.ObjectId:X4}");
            }

            records.Add(new ObjectRecord(
                spawn.Name,
                spawn.NameReplace,
                spawn.ObjectId,
                spawn.ParentId,
                spawn.Version,
                spawn.StateOffset,
                spawn.StateLength,
                dataOffset + updateRelativeOffset,
                updateSize,
                dataOffset + recordRelativeOffset,
                reader.Position - recordRelativeOffset,
                spawn.ClientDataOffset,
                spawn.ClientDataLength));
        }

        if (reader.Remaining != 0)
        {
            throw Error($"OBJECT chunk имеет {reader.Remaining} лишних байт после {count} объектов");
        }

        return records;
    }

    private static SpawnRecord ParseSpawn(ReadOnlySpan<byte> packet, int packetOffset)
    {
        var reader = new SpanReader(packet, "SPAWN packet");
        if (reader.ReadUInt16() != SpawnMessage)
        {
            throw Error("объект начинается не с M_SPAWN");
        }

        var name = reader.ReadZeroTerminatedString();
        var nameReplace = reader.ReadZeroTerminatedString();
        reader.Skip(2); // game id, respawn point
        reader.Skip(6 * sizeof(float)); // position and angles
        reader.Skip(sizeof(ushort)); // respawn time
        var objectId = reader.ReadUInt16();
        var parentId = reader.ReadUInt16();
        reader.Skip(sizeof(ushort)); // phantom id
        var flags = reader.ReadUInt16();
        if ((flags & SpawnHasVersion) == 0)
        {
            throw Error($"объект '{name}': отсутствует M_SPAWN_VERSION");
        }

        var version = reader.ReadUInt16();
        if (version < 112)
        {
            throw Error($"объект '{name}': spawn version {version} ниже безопасно поддерживаемого 112");
        }

        if (version > 120)
        {
            reader.Skip(sizeof(ushort)); // game type
        }

        if (version > 69)
        {
            reader.Skip(sizeof(ushort)); // script version
        }

        int? clientDataOffset = null;
        var clientDataLength = 0;
        if (version > 70)
        {
            clientDataLength = version > 93 ? reader.ReadUInt16() : reader.ReadByte();
            clientDataOffset = checked(packetOffset + reader.Position);
            reader.Skip(clientDataLength);
        }

        if (version > 79)
        {
            reader.Skip(sizeof(ushort)); // spawn id
        }

        var stateSize = reader.ReadUInt16();
        if (stateSize < sizeof(ushort))
        {
            throw Error($"объект '{name}': STATE size={stateSize} меньше 2");
        }

        var stateLength = stateSize - sizeof(ushort);
        if (reader.Remaining != stateLength)
        {
            throw Error($"объект '{name}': STATE выходит за SPAWN packet или после него есть лишние байты");
        }

        var stateOffset = checked(packetOffset + reader.Position);
        reader.Skip(stateLength);
        return new SpawnRecord(
            name,
            nameReplace,
            objectId,
            parentId,
            version,
            stateOffset,
            stateLength,
            clientDataOffset,
            clientDataLength);
    }

    private static XRayRegistryObject ToRegistryObject(ObjectRecord record) => new(
            record.Name,
            record.NameReplace,
            record.ObjectId,
            record.ParentId,
            record.Version,
            record.RecordOffset,
            record.RecordLength,
            record.StateOffset,
            record.StateLength,
            record.UpdateOffset,
            record.UpdateLength,
            record.ClientDataOffset,
            record.ClientDataLength);

    /// <summary>Reads the shared creature prefix (health, killer, death time) of a registry object's STATE; null when it is not laid out as a creature.</summary>
    internal static XRayCreatureVitals? ReadCreatureVitals(ReadOnlySpan<byte> raw, XRayRegistryObject item)
    {
        if (item.Version <= 18 || item.StateLength <= 0 || item.StateOffset < 0 || item.StateOffset + item.StateLength > raw.Length)
        {
            return null;
        }

        try
        {
            var version = item.Version;
            var reader = new SpanReader(raw.Slice(item.StateOffset, item.StateLength), "creature STATE");
            if (version < 105)
            {
                return null;
            }

            SkipTraderState(ref reader, version);
            ReadDynamicVisualState(ref reader, version);
            reader.Skip(3);
            var healthOffset = checked(item.StateOffset + reader.Position);
            var health = reader.ReadSingle();
            if (version < 32)
            {
                _ = reader.ReadZeroTerminatedString();
            }

            if (version > 87)
            {
                SkipUInt16Vector(ref reader);
                SkipUInt16Vector(ref reader);
            }

            ushort? killer = null;
            if (version > 94)
            {
                killer = reader.ReadUInt16();
            }

            ulong? deathTime = null;
            if (version > 115)
            {
                deathTime = reader.ReadUInt64();
            }

            return float.IsFinite(health) && health >= -1f && health <= 1f
                ? new XRayCreatureVitals(item.ObjectId, item.Name, health, killer, deathTime) { HealthOffset = healthOffset }
                : null;
        }
        catch (XRayFormatException)
        {
            return null;
        }
    }

    // Human stalkers serialise the trader block before the visual/creature block (the actor does the opposite).
    private static void SkipTraderState(ref SpanReader reader, ushort version)
    {
        reader.Skip(sizeof(uint)); // money
        _ = reader.ReadZeroTerminatedString(); // specific character
        reader.Skip(sizeof(uint)); // trader flags
        _ = reader.ReadZeroTerminatedString(); // character profile
        reader.Skip(sizeof(int) * 3); // community, rank, reputation
        _ = reader.ReadZeroTerminatedString(); // display name
        if (version > 124)
        {
            reader.Skip(2);
        }
    }

    private static ActorState ParseActorState(ReadOnlySpan<byte> state, ushort version, int stateOffset)
    {
        var reader = new SpanReader(state, "actor STATE");
        ReadDynamicVisualState(ref reader, version);
        reader.Skip(3); // team, squad, group
        float? health = null;
        if (version > 18)
        {
            var value = reader.ReadSingle();
            health = value is >= 0 and <= 1 ? value : null;
        }

        if (version < 32)
        {
            _ = reader.ReadZeroTerminatedString();
        }

        if (version > 87)
        {
            SkipUInt16Vector(ref reader);
            SkipUInt16Vector(ref reader);
        }

        if (version > 94)
        {
            reader.Skip(sizeof(ushort)); // killer id
        }

        if (version > 115)
        {
            reader.Skip(sizeof(ulong)); // game death time
        }

        if (version > 19 && version < 108)
        {
            reader.Skip(sizeof(uint)); // legacy events
        }

        if (version <= 62)
        {
            throw Error($"actor spawn version {version}: money field не сериализуется");
        }

        var moneyOffset = checked(stateOffset + reader.Position);
        var money = reader.ReadUInt32();
        if (version > 75 && version < 98)
        {
            reader.Skip(sizeof(int)); // legacy specific-character index
        }
        else if (version >= 98)
        {
            _ = reader.ReadZeroTerminatedString();
        }

        if (version > 77)
        {
            reader.Skip(sizeof(uint)); // trader flags
        }

        if (version > 81 && version < 96)
        {
            reader.Skip(sizeof(int)); // legacy character profile index
        }
        else if (version > 95)
        {
            _ = reader.ReadZeroTerminatedString();
        }

        int? faction = null;
        int? factionOffset = null;
        if (version > 85)
        {
            factionOffset = checked(stateOffset + reader.Position);
            faction = reader.ReadInt32();
        }

        int? rank = null;
        int? reputation = null;
        if (version > 86)
        {
            rank = reader.ReadInt32();
            reputation = reader.ReadInt32();
        }

        string? name = null;
        if (version > 104)
        {
            // Player-visible text: X-Ray writes it in cp1251 (the Python oracle decodes the same way).
            name = reader.ReadDisplayString();
            if (name.Length == 0)
            {
                name = null;
            }
        }

        if (version > 124)
        {
            reader.Skip(2); // deadbody can take, closed
        }

        return new ActorState(money, moneyOffset, faction, factionOffset, health, rank, reputation, name);
    }

    private static void ReadDynamicVisualState(ref SpanReader reader, ushort version)
    {
        if (version >= 1)
        {
            if (version > 24)
            {
                if (version < 83)
                {
                    reader.Skip(sizeof(float));
                }
            }
            else
            {
                reader.Skip(sizeof(byte));
            }

            if (version < 4)
            {
                reader.Skip(sizeof(ushort));
            }

            reader.Skip(sizeof(ushort)); // graph id
            reader.Skip(sizeof(float)); // distance
        }

        if (version >= 4)
        {
            reader.Skip(sizeof(uint)); // direct control
        }

        if (version >= 8)
        {
            reader.Skip(sizeof(uint)); // node id
        }

        if (version is > 22 and <= 79)
        {
            reader.Skip(sizeof(ushort)); // legacy spawn id
        }

        if (version is > 23 and < 84)
        {
            _ = reader.ReadZeroTerminatedString(); // legacy spawn control
        }

        if (version > 49)
        {
            reader.Skip(sizeof(uint)); // ALife flags
        }

        if (version > 57)
        {
            _ = reader.ReadZeroTerminatedString(); // ini string
        }

        if (version > 61)
        {
            reader.Skip(sizeof(uint)); // story id
        }

        if (version > 111)
        {
            reader.Skip(sizeof(uint)); // spawn story id
        }

        if (version > 31)
        {
            _ = reader.ReadZeroTerminatedString(); // visual name
            if (version > 103)
            {
                reader.Skip(sizeof(byte)); // visual flags
            }
        }
    }
}
