using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static partial class XRayTrilogyReader
{
    private static bool TryReadAmmoCount(
        ReadOnlySpan<byte> raw,
        ObjectRecord record,
        out ushort count,
        out int stateCountOffset,
        out int updateCountOffset) => TryReadAmmoCount(
            raw,
            record.Version,
            record.StateOffset,
            record.StateLength,
            record.UpdateOffset,
            record.UpdateLength,
            out count,
            out stateCountOffset,
            out updateCountOffset);

    internal static bool TryReadAmmoCount(
        ReadOnlySpan<byte> raw,
        ushort version,
        int stateOffset,
        int stateLength,
        int updateOffset,
        int updateLength,
        out ushort count,
        out int stateCountOffset,
        out int updateCountOffset)
    {
        count = 0;
        stateCountOffset = -1;
        updateCountOffset = -1;
        try
        {
            var reader = new SpanReader(raw.Slice(stateOffset, stateLength), "ammo STATE");
            ReadDynamicVisualState(ref reader, version);
            if (version > 52)
            {
                reader.Skip(sizeof(float)); // condition
            }

            if (version > 123)
            {
                SkipStringVector(ref reader);
            }

            count = reader.ReadUInt16();
            stateCountOffset = checked(stateOffset + reader.Position - sizeof(ushort));
            if (updateLength < 5)
            {
                return false;
            }

            updateCountOffset = updateOffset + updateLength - sizeof(ushort);
            _ = BinaryPrimitives.ReadUInt16LittleEndian(raw[updateCountOffset..]);
            return true;
        }
        catch (XRayFormatException)
        {
            return false;
        }
    }

    private static bool TryReadUpgrades(
        ReadOnlySpan<byte> raw,
        ObjectRecord record,
        out string[] upgrades,
        out int upgradesOffset,
        out int upgradesLength) => TryReadUpgrades(
            raw,
            record.Version,
            record.StateOffset,
            record.StateLength,
            out upgrades,
            out upgradesOffset,
            out upgradesLength);

    internal static bool TryReadUpgrades(
        ReadOnlySpan<byte> raw,
        ushort version,
        int stateOffset,
        int stateLength,
        out string[] upgrades,
        out int upgradesOffset,
        out int upgradesLength)
    {
        upgrades = [];
        upgradesOffset = -1;
        upgradesLength = 0;
        try
        {
            var reader = new SpanReader(raw.Slice(stateOffset, stateLength), "inventory STATE");
            ReadDynamicVisualState(ref reader, version);
            if (version > 52)
            {
                reader.Skip(sizeof(float)); // condition
            }

            var start = reader.Position;
            upgrades = ReadStringVector(ref reader);
            upgradesOffset = checked(stateOffset + start);
            upgradesLength = reader.Position - start;
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
        string NameReplace,
        ushort ObjectId,
        ushort ParentId,
        ushort Version,
        int StateOffset,
        int StateLength,
        int UpdateOffset,
        int UpdateLength,
        int RecordOffset,
        int RecordLength,
        int? ClientDataOffset,
        int ClientDataLength);

    private sealed record SpawnRecord(
        string Name,
        string NameReplace,
        ushort ObjectId,
        ushort ParentId,
        ushort Version,
        int StateOffset,
        int StateLength,
        int? ClientDataOffset,
        int ClientDataLength);

    private sealed record ActorState(
        uint Money,
        int MoneyOffset,
        int? PlayerFactionIndex,
        int? PlayerFactionOffset,
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

        public ulong ReadUInt64()
        {
            Ensure(sizeof(ulong));
            var value = BinaryPrimitives.ReadUInt64LittleEndian(_data[Position..]);
            Position += sizeof(ulong);
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

        /// <summary>Zero-terminated player-visible text: strict UTF-8, otherwise cp1251.</summary>
        public string ReadDisplayString()
        {
            var available = Math.Min(Remaining, MaximumStringLength + 1);
            var terminator = _data.Slice(Position, available).IndexOf((byte)0);
            if (terminator < 0)
            {
                throw Error($"{_label}: zero-terminated string отсутствует или слишком длинная");
            }

            var value = Content.LtxDocument.Decode(_data.Slice(Position, terminator));
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
