using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Formats.XRay;

internal sealed class XRayRelationRegistry
{
    private const uint MaximumCount = 1_000_000;
    private const int MaximumStringLength = 1 << 20;
    private readonly IReadOnlyList<RelationRow> _rows;

    private XRayRelationRegistry(IReadOnlyList<RelationRow> rows)
    {
        _rows = rows;
    }

    public static bool TryParse(
        ReadOnlySpan<byte> payload,
        bool infoPortionsHaveTimestamp,
        out XRayRelationRegistry? registry)
    {
        registry = null;
        if (payload.Length < sizeof(uint) ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload) > MaximumCount)
        {
            return false;
        }

        try
        {
            registry = Parse(payload, infoPortionsHaveTimestamp);
            return true;
        }
        catch (XRayFormatException)
        {
            return false;
        }
    }

    public static XRayRelationRegistry Parse(
        ReadOnlySpan<byte> payload,
        bool infoPortionsHaveTimestamp)
    {
        var reader = new RegistryReader(payload);
        var infoPortionCount = reader.ReadCount("InfoPortions");
        for (var index = 0; index < infoPortionCount; index++)
        {
            _ = reader.ReadUInt16();
            var valueCount = reader.ReadCount("InfoPortion vector");
            for (var value = 0; value < valueCount; value++)
            {
                reader.ReadZeroTerminatedString();
                if (infoPortionsHaveTimestamp)
                {
                    _ = reader.ReadUInt64();
                }
            }
        }

        var relationCount = reader.ReadCount("relation");
        var rows = new List<RelationRow>(checked((int)relationCount));
        var characterIds = new HashSet<ushort>();
        for (var index = 0; index < relationCount; index++)
        {
            var start = reader.Position;
            var characterId = reader.ReadUInt16();
            if (!characterIds.Add(characterId))
            {
                throw Error($"повторяющийся character id 0x{characterId:X4}");
            }

            var personalCount = reader.ReadCount("personal relation");
            var personalIds = new HashSet<ushort>();
            for (var relation = 0; relation < personalCount; relation++)
            {
                var targetId = reader.ReadUInt16();
                _ = reader.ReadInt32();
                if (!personalIds.Add(targetId))
                {
                    throw Error(
                        $"character 0x{characterId:X4}: повторяющийся personal id 0x{targetId:X4}");
                }
            }

            var communityCountOffset = reader.Position;
            var communityCount = reader.ReadCount("community relation");
            var communities = new List<CommunityRelation>(checked((int)communityCount));
            var communityIds = new HashSet<int>();
            for (var relation = 0; relation < communityCount; relation++)
            {
                var communityId = reader.ReadInt32();
                var valueOffset = reader.Position;
                var value = reader.ReadInt32();
                if (!communityIds.Add(communityId))
                {
                    throw Error($"character 0x{characterId:X4}: повторяющийся community {communityId}");
                }

                communities.Add(new CommunityRelation(communityId, value, valueOffset));
            }

            rows.Add(new RelationRow(
                characterId,
                start,
                reader.Position,
                communityCountOffset,
                Array.AsReadOnly(communities.ToArray())));
        }

        return new XRayRelationRegistry(Array.AsReadOnly(rows.ToArray()));
    }

    public RelationRow? ForCharacter(ushort characterId)
    {
        foreach (var row in _rows)
        {
            if (row.CharacterId == characterId)
            {
                return row;
            }
        }

        return null;
    }

    public static byte[] Patch(
        ReadOnlySpan<byte> payload,
        RelationRow row,
        int communityId,
        int goodwill)
    {
        CommunityRelation? existing = null;
        foreach (var relation in row.Communities)
        {
            if (relation.CommunityId == communityId)
            {
                existing = relation;
                break;
            }
        }

        if (existing is not null)
        {
            var result = payload.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(existing.ValueOffset),
                goodwill);
            return result;
        }

        if (row.Start < 0 ||
            row.CommunityCountOffset < row.Start ||
            row.End < row.CommunityCountOffset ||
            row.End > payload.Length)
        {
            throw Error("границы relation record выходят за chunk");
        }

        var communities = new (int CommunityId, int Goodwill)[row.Communities.Count + 1];
        for (var index = 0; index < row.Communities.Count; index++)
        {
            var relation = row.Communities[index];
            communities[index] = (relation.CommunityId, relation.Goodwill);
        }

        communities[^1] = (communityId, goodwill);
        Array.Sort(
            communities,
            static (left, right) => left.CommunityId.CompareTo(right.CommunityId));
        var prefixLength = row.CommunityCountOffset - row.Start;
        var replacementLength = checked(sizeof(uint) + communities.Length * sizeof(int) * 2);
        var outputLength = checked(
            payload.Length - (row.End - row.Start) + prefixLength + replacementLength);
        var output = GC.AllocateUninitializedArray<byte>(outputLength);

        payload[..row.Start].CopyTo(output);
        var destination = row.Start;
        payload.Slice(row.Start, prefixLength).CopyTo(output.AsSpan(destination));
        destination += prefixLength;
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(destination),
            checked((uint)communities.Length));
        destination += sizeof(uint);
        foreach (var relation in communities)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(destination), relation.CommunityId);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(destination + sizeof(int)), relation.Goodwill);
            destination += sizeof(int) * 2;
        }

        payload[row.End..].CopyTo(output.AsSpan(destination));
        return output;
    }

    private static XRayFormatException Error(string message) =>
        new($"X-Ray relation registry: {message}");

    internal sealed record RelationRow(
        ushort CharacterId,
        int Start,
        int End,
        int CommunityCountOffset,
        IReadOnlyList<CommunityRelation> Communities);

    internal sealed record CommunityRelation(int CommunityId, int Goodwill, int ValueOffset);

    private ref struct RegistryReader
    {
        private readonly ReadOnlySpan<byte> _payload;

        public RegistryReader(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            Position = 0;
        }

        public int Position { get; private set; }

        public ushort ReadUInt16()
        {
            Ensure(sizeof(ushort));
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_payload[Position..]);
            Position += sizeof(ushort);
            return value;
        }

        public uint ReadUInt32()
        {
            Ensure(sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_payload[Position..]);
            Position += sizeof(uint);
            return value;
        }

        public int ReadInt32()
        {
            Ensure(sizeof(int));
            var value = BinaryPrimitives.ReadInt32LittleEndian(_payload[Position..]);
            Position += sizeof(int);
            return value;
        }

        public ulong ReadUInt64()
        {
            Ensure(sizeof(ulong));
            var value = BinaryPrimitives.ReadUInt64LittleEndian(_payload[Position..]);
            Position += sizeof(ulong);
            return value;
        }

        public uint ReadCount(string label)
        {
            var count = ReadUInt32();
            if (count > MaximumCount)
            {
                throw Error($"{label} count={count} слишком велик");
            }

            return count;
        }

        public void ReadZeroTerminatedString()
        {
            var terminator = _payload[Position..].IndexOf((byte)0);
            if (terminator < 0 || terminator > MaximumStringLength)
            {
                throw Error("строка InfoPortion не найдена или слишком длинная");
            }

            Position += terminator + 1;
        }

        private void Ensure(int size)
        {
            if (size < 0 || size > _payload.Length - Position)
            {
                throw Error($"данные обрезаны на смещении 0x{Position:X}");
            }
        }
    }
}
