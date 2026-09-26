using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayTrilogyReader
{
    private const int MaximumObjects = 1_000_000;
    private const int MaximumVectorLength = 1_000_000;
    private const int MaximumStringLength = 1 << 20;
    private const ushort SpawnMessage = 1;
    private const ushort UpdateMessage = 0;
    private const ushort SpawnHasVersion = 1 << 5;
    private static readonly FormatDefinition[] OriginalFormats =
    [
        new("stalker-soc", 3, 3, [118], [], []),
        new("stalker-cs", 5, 5, [122, 123, 124], [], []),
        new("stalker-cop", 6, 6, [128], [], []),
    ];
    private static readonly FormatDefinition[] EnhancedFormats =
    [
        new("stalker-soc-ee", 3, 51, [118], [], []),
        new("stalker-cs-ee", 6, 54, [128], ["marsh"u8.ToArray()], ["zaton"u8.ToArray()]),
        new("stalker-cop-ee", 6, 54, [128], ["zaton"u8.ToArray()], ["marsh"u8.ToArray()]),
    ];

    public static XRayTrilogySave FromBytes(ReadOnlySpan<byte> data) => Read(data, enhanced: false);

    internal static XRayTrilogySave FromEnhancedBytes(ReadOnlySpan<byte> data) => Read(data, enhanced: true);

    private static XRayTrilogySave Read(ReadOnlySpan<byte> data, bool enhanced)
    {
        var container = XRayContainer.FromBytes(data);
        var chunks = container.Chunks;
        var alife = GetRequiredChunk(chunks, 0).Data.Span;
        if (alife.Length != sizeof(uint))
        {
            throw Error("ALIFE header chunk должен содержать ровно u32 version");
        }

        var alifeVersion = BinaryPrimitives.ReadUInt32LittleEndian(alife);

        var timeChunk = GetRequiredChunk(chunks, 5).Data.Span;
        if (timeChunk.Length < 16)
        {
            throw Error("GAME_TIME chunk короче 16 байт");
        }

        var gameTime = BinaryPrimitives.ReadUInt64LittleEndian(timeChunk);
        var timeFactor = ReadSingle(timeChunk[8..]);
        var normalTimeFactor = ReadSingle(timeChunk[12..]);
        if (!float.IsFinite(timeFactor) || !float.IsFinite(normalTimeFactor))
        {
            throw Error("GAME_TIME содержит нечисловой time factor");
        }

        var objectChunk = GetRequiredChunk(chunks, 2);
        var format = GetSupportedFormat(
            container.Version,
            alifeVersion,
            objectChunk.Data.Span,
            enhanced);
        var records = ParseObjects(container.Raw.Span, objectChunk);
        var actors = records.Where(record => string.Equals(
            record.Name, "actor", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (actors.Length != 1)
        {
            throw Error($"найдено actor объектов: {actors.Length}, ожидался ровно один");
        }

        var actor = actors[0];
        if (!format.ActorVersions.Contains(actor.Version))
        {
            var expected = string.Join(", ", format.ActorVersions.Order());
            throw Error(
                $"actor spawn version {actor.Version} не подтверждён для {format.Id}; " +
                $"ожидалось {expected}");
        }

        var raw = container.Raw.Span;
        var actorState = ParseActorState(
            raw.Slice(actor.StateOffset, actor.StateLength),
            actor.Version,
            actor.StateOffset);
        var items = new List<XRayInventoryItem>();
        foreach (var record in records)
        {
            if (record.ParentId != actor.ObjectId || record.ObjectId == actor.ObjectId)
            {
                continue;
            }

            var (kindCode, category) = CategoryForName(record.Name);
            var isAmmo = record.Name.StartsWith("ammo_", StringComparison.OrdinalIgnoreCase);
            ushort? count = null;
            int? stackStateCountOffset = null;
            int? stackUpdateCountOffset = null;
            var editableCount = false;
            if (isAmmo && TryReadAmmoCount(
                raw,
                record,
                out var parsedCount,
                out var parsedStateCountOffset,
                out var parsedUpdateCountOffset))
            {
                count = parsedCount;
                stackStateCountOffset = parsedStateCountOffset;
                stackUpdateCountOffset = parsedUpdateCountOffset;
                editableCount = true;
            }

            IReadOnlyList<string>? upgrades = null;
            if (record.Version > 123 && TryReadUpgrades(raw, record, out var parsedUpgrades))
            {
                upgrades = Array.AsReadOnly(parsedUpgrades);
            }

            items.Add(new XRayInventoryItem(
                record.ObjectId,
                record.ParentId,
                record.Name,
                kindCode,
                category,
                count,
                editableCount,
                upgrades,
                stackStateCountOffset,
                stackUpdateCountOffset));
        }

        items.Sort(static (left, right) => left.Handle.CompareTo(right.Handle));
        return new XRayTrilogySave(
            format.Id,
            container.Version,
            container,
            actor.Version,
            actor.ObjectId,
            actorState.Money,
            actorState.MoneyOffset,
            actorState.PlayerFactionIndex,
            actorState.Health,
            actorState.Rank,
            actorState.Reputation,
            actorState.Name,
            gameTime,
            timeFactor,
            normalTimeFactor,
            Array.AsReadOnly(items.ToArray()));
    }

    private static FormatDefinition GetSupportedFormat(
        uint containerVersion,
        uint alifeVersion,
        ReadOnlySpan<byte> objectData,
        bool enhanced)
    {
        var formats = enhanced ? EnhancedFormats : OriginalFormats;
        foreach (var format in formats)
        {
            if (format.ContainerVersion != containerVersion || format.AlifeVersion != alifeVersion)
            {
                continue;
            }

            var hasRequiredMarkers = true;
            foreach (var marker in format.RequiredObjectMarkers)
            {
                if (objectData.IndexOf(marker) < 0)
                {
                    hasRequiredMarkers = false;
                    break;
                }
            }

            var hasForbiddenMarkers = false;
            foreach (var marker in format.ForbiddenObjectMarkers)
            {
                if (objectData.IndexOf(marker) >= 0)
                {
                    hasForbiddenMarkers = true;
                    break;
                }
            }

            if (hasRequiredMarkers && !hasForbiddenMarkers)
            {
                return format;
            }
        }

        var target = enhanced ? "Enhanced Edition" : "original trilogy";
        throw Error(
            $"container version {containerVersion}, ALIFE version {alifeVersion}, " +
            $"and OBJECT markers do not identify a supported {target} save");
    }

    private static XRayChunk GetRequiredChunk(IReadOnlyList<XRayChunk> chunks, uint type)
    {
        XRayChunk? match = null;
        foreach (var chunk in chunks)
        {
            if (chunk.Type != type)
            {
                continue;
            }

            if (match is not null)
            {
                throw Error($"chunk type={type} встречается больше одного раза");
            }

            match = chunk;
        }

        return match ?? throw Error($"обязательный chunk type={type} отсутствует");
    }

    private static IReadOnlyList<ObjectRecord> ParseObjects(
        ReadOnlySpan<byte> raw,
        XRayChunk chunk)
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
                spawn.ObjectId,
                spawn.ParentId,
                spawn.Version,
                spawn.StateOffset,
                spawn.StateLength,
                dataOffset + updateRelativeOffset,
                updateSize));
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
        _ = reader.ReadZeroTerminatedString();
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

        if (version > 70)
        {
            var clientLength = version > 93 ? reader.ReadUInt16() : reader.ReadByte();
            reader.Skip(clientLength);
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
        return new SpawnRecord(name, objectId, parentId, version, stateOffset, stateLength);
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
        if (version > 85)
        {
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
            name = reader.ReadZeroTerminatedString();
            if (name.Length == 0)
            {
                name = null;
            }
        }

        if (version > 124)
        {
            reader.Skip(2); // deadbody can take, closed
        }

        return new ActorState(money, moneyOffset, faction, health, rank, reputation, name);
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

    private static bool TryReadAmmoCount(
        ReadOnlySpan<byte> raw,
        ObjectRecord record,
        out ushort count,
        out int stateCountOffset,
        out int updateCountOffset)
    {
        count = 0;
        stateCountOffset = -1;
        updateCountOffset = -1;
        try
        {
            var reader = new SpanReader(raw.Slice(record.StateOffset, record.StateLength), "ammo STATE");
            ReadDynamicVisualState(ref reader, record.Version);
            if (record.Version > 52)
            {
                reader.Skip(sizeof(float)); // condition
            }

            if (record.Version > 123)
            {
                SkipStringVector(ref reader);
            }

            count = reader.ReadUInt16();
            stateCountOffset = checked(record.StateOffset + reader.Position - sizeof(ushort));
            if (record.UpdateLength < 5)
            {
                return false;
            }

            updateCountOffset = record.UpdateOffset + record.UpdateLength - sizeof(ushort);
            _ = BinaryPrimitives.ReadUInt16LittleEndian(raw[updateCountOffset..]);
            return true;
        }
        catch (XRayFormatException)
        {
            return false;
        }
    }

    private static bool TryReadUpgrades(ReadOnlySpan<byte> raw, ObjectRecord record, out string[] upgrades)
    {
        upgrades = [];
        try
        {
            var reader = new SpanReader(raw.Slice(record.StateOffset, record.StateLength), "inventory STATE");
            ReadDynamicVisualState(ref reader, record.Version);
            if (record.Version > 52)
            {
                reader.Skip(sizeof(float)); // condition
            }

            upgrades = ReadStringVector(ref reader);
            return true;
        }
        catch (XRayFormatException)
        {
            return false;
        }
    }

    private static void SkipUInt16Vector(ref SpanReader reader)
    {
        var count = reader.ReadUInt32();
        if (count > MaximumVectorLength)
        {
            throw Error($"vector count={count} слишком велик");
        }

        reader.Skip(checked((int)count * sizeof(ushort)));
    }

    private static void SkipStringVector(ref SpanReader reader) => _ = ReadStringVector(ref reader);

    private static string[] ReadStringVector(ref SpanReader reader)
    {
        var count = reader.ReadUInt32();
        if (count > MaximumVectorLength)
        {
            throw Error($"upgrades count={count} слишком велик");
        }

        var values = new string[checked((int)count)];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = reader.ReadZeroTerminatedString();
        }

        return values;
    }

    private static (int KindCode, string Category) CategoryForName(string name)
    {
        var key = name.ToLowerInvariant();
        if (key.StartsWith("ammo_", StringComparison.Ordinal)) return (5, "Патроны");
        if (key.StartsWith("wpn_", StringComparison.Ordinal) || key.StartsWith("weapon_", StringComparison.Ordinal)) return (0, "Оружие");
        if (key.StartsWith("outfit_", StringComparison.Ordinal) || key.StartsWith("scientific_", StringComparison.Ordinal) ||
            key.StartsWith("helm_", StringComparison.Ordinal) || key.StartsWith("armor_", StringComparison.Ordinal) ||
            key.EndsWith("_outfit", StringComparison.Ordinal) || key.EndsWith("_helmet", StringComparison.Ordinal) ||
            key.EndsWith("_helm", StringComparison.Ordinal) || key.EndsWith("_armor", StringComparison.Ordinal))
        {
            return (1, "Броня/экипировка");
        }

        if (key.StartsWith("af_", StringComparison.Ordinal) || key.StartsWith("artifact_", StringComparison.Ordinal)) return (2, "Артефакт");
        if (key.StartsWith("device_", StringComparison.Ordinal) || key.StartsWith("detector_", StringComparison.Ordinal)) return (8, "Устройство");
        if (key.StartsWith("grenade", StringComparison.Ordinal) || key.StartsWith("rgd", StringComparison.Ordinal) || key.StartsWith("f1_", StringComparison.Ordinal)) return (7, "Гранаты/стак");
        if (key.StartsWith("medkit", StringComparison.Ordinal) || key.StartsWith("bandage", StringComparison.Ordinal) ||
            key.StartsWith("antirad", StringComparison.Ordinal) || key.StartsWith("drug_", StringComparison.Ordinal) ||
            key.StartsWith("food_", StringComparison.Ordinal) || key.StartsWith("bread", StringComparison.Ordinal) ||
            key.StartsWith("kolbasa", StringComparison.Ordinal) || key.StartsWith("vodka", StringComparison.Ordinal) ||
            key.StartsWith("energy", StringComparison.Ordinal))
        {
            return (4, "Расходник");
        }

        return (8, "Разное");
    }

    private static float ReadSingle(ReadOnlySpan<byte> data) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));

    private static XRayFormatException Error(string message) => new($"X-Ray save: {message}");

    private sealed record FormatDefinition(
        string Id,
        uint ContainerVersion,
        uint AlifeVersion,
        ushort[] ActorVersions,
        byte[][] RequiredObjectMarkers,
        byte[][] ForbiddenObjectMarkers);

    private sealed record ObjectRecord(
        string Name,
        ushort ObjectId,
        ushort ParentId,
        ushort Version,
        int StateOffset,
        int StateLength,
        int UpdateOffset,
        int UpdateLength);

    private sealed record SpawnRecord(
        string Name,
        ushort ObjectId,
        ushort ParentId,
        ushort Version,
        int StateOffset,
        int StateLength);

    private sealed record ActorState(
        uint Money,
        int MoneyOffset,
        int? PlayerFactionIndex,
        float? Health,
        int? Rank,
        int? Reputation,
        string? Name);

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly string _label;

        public SpanReader(ReadOnlySpan<byte> data, string label)
        {
            _data = data;
            _label = label;
            Position = 0;
        }

        public int Position { get; private set; }

        public int Remaining => _data.Length - Position;

        public byte ReadByte()
        {
            Ensure(sizeof(byte));
            return _data[Position++];
        }

        public ushort ReadUInt16()
        {
            Ensure(sizeof(ushort));
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
            Position += sizeof(ushort);
            return value;
        }

        public uint ReadUInt32()
        {
            Ensure(sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
            Position += sizeof(uint);
            return value;
        }

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public float ReadSingle()
        {
            Ensure(sizeof(float));
            var value = XRayTrilogyReader.ReadSingle(_data[Position..]);
            Position += sizeof(float);
            return value;
        }

        public byte[] ReadBytes(int length)
        {
            Ensure(length);
            var bytes = _data.Slice(Position, length).ToArray();
            Position += length;
            return bytes;
        }

        public string ReadZeroTerminatedString()
        {
            var available = Math.Min(Remaining, MaximumStringLength + 1);
            var terminator = _data.Slice(Position, available).IndexOf((byte)0);
            if (terminator < 0)
            {
                throw Error($"{_label}: zero-terminated string отсутствует или слишком длинная");
            }

            var value = Encoding.UTF8.GetString(_data.Slice(Position, terminator));
            Position += terminator + 1;
            return value;
        }

        public void Skip(int length)
        {
            Ensure(length);
            Position += length;
        }

        private void Ensure(int length)
        {
            if (length < 0 || length > Remaining)
            {
                throw Error($"{_label}: ожидалось {length} байт, осталось {Remaining}");
            }
        }
    }
}
