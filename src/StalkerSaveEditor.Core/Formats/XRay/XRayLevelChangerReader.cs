using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.XRay;

public readonly record struct XRayVector3(float X, float Y, float Z);

public sealed record XRayLevelChangerStateSuffix(
    int ObjectVersion,
    ushort? DestGameVertexId,
    uint? DestLevelVertexId,
    XRayVector3? DestPosition,
    XRayVector3? DestDirection,
    string DestLevelName,
    string DestLevelPointName,
    bool? Silent,
    int ConsumedBytes);

public static class XRayLevelChangerReader
{
    private const int MaximumStringLength = 1 << 20;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Encoding Windows1251 = CreateWindows1251();

    public static XRayLevelChangerStateSuffix ParseStateSuffix(
        ReadOnlySpan<byte> packetSuffix,
        int objectVersion)
    {
        if (objectVersion is < 0 or > ushort.MaxValue)
        {
            throw Error("invalid object version");
        }

        var reader = new SuffixReader(packetSuffix);
        ushort? gameVertexId;
        uint? levelVertexId;
        XRayVector3? position;
        XRayVector3? direction;
        if (objectVersion < 34)
        {
            reader.ReadUInt32();
            reader.ReadUInt32();
            gameVertexId = null;
            levelVertexId = null;
            position = null;
            direction = null;
        }
        else
        {
            gameVertexId = reader.ReadUInt16();
            levelVertexId = reader.ReadUInt32();
            position = new XRayVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            direction = objectVersion <= 53
                ? new XRayVector3(0, reader.ReadSingle(), 0)
                : new XRayVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        var levelName = reader.ReadTextString();
        var levelPointName = reader.ReadTextString();
        bool? silent = objectVersion > 116 ? reader.ReadByte() != 0 : null;
        return new XRayLevelChangerStateSuffix(
            objectVersion,
            gameVertexId,
            levelVertexId,
            position,
            direction,
            levelName,
            levelPointName,
            silent,
            reader.Position);
    }

    private static Encoding CreateWindows1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            1251,
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);
    }

    private static XRayFormatException Error(string message) =>
        new($"X-Ray level changer STATE suffix: {message}.");

    private ref struct SuffixReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        internal readonly int Position => _position;

        internal ushort ReadUInt16()
        {
            Require(sizeof(ushort));
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_position..]);
            _position += sizeof(ushort);
            return value;
        }

        internal uint ReadUInt32()
        {
            Require(sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[_position..]);
            _position += sizeof(uint);
            return value;
        }

        internal float ReadSingle()
        {
            Require(sizeof(float));
            var value = BinaryPrimitives.ReadSingleLittleEndian(_data[_position..]);
            _position += sizeof(float);
            return value;
        }

        internal byte ReadByte()
        {
            Require(sizeof(byte));
            return _data[_position++];
        }

        internal string ReadTextString()
        {
            var remaining = _data[_position..];
            var terminatorOffset = remaining.IndexOf((byte)0);
            if (terminatorOffset < 0 || terminatorOffset > MaximumStringLength)
            {
                throw Error("stringZ is missing or too long");
            }

            var bytes = remaining[..terminatorOffset];
            _position += terminatorOffset + 1;
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Windows1251.GetString(bytes);
            }
        }

        private readonly void Require(int length)
        {
            if (length < 0 || length > _data.Length - _position)
            {
                throw Error($"packet is truncated at offset 0x{_position:X} (needs {length} bytes)");
            }
        }
    }
}
