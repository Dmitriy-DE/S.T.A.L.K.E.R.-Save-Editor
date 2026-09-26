using System.Buffers.Binary;
using StalkerSaveEditor.Core.Codecs;

namespace StalkerSaveEditor.Core.Formats.XRay;

public sealed class XRayContainer
{
    public const uint Signature = uint.MaxValue;
    public const int MaximumUnpackedSize = 512 * 1024 * 1024;

    private readonly byte[] _original;
    private readonly byte[] _raw;
    private readonly IReadOnlyList<XRayChunk> _chunks;
    private readonly IReadOnlyList<uint> _chunkTypes;

    private XRayContainer(
        byte[] original,
        uint magic,
        uint version,
        int unpackedSize,
        byte[] raw,
        IReadOnlyList<XRayChunk> chunks)
    {
        _original = original;
        _raw = raw;
        _chunks = chunks;
        Magic = magic;
        Version = version;
        UnpackedSize = unpackedSize;
        _chunkTypes = Array.AsReadOnly(chunks.Select(chunk => chunk.Type).ToArray());
    }

    public uint Magic { get; }

    public uint Version { get; }

    public int UnpackedSize { get; }

    public ReadOnlyMemory<byte> Original => _original;

    public ReadOnlyMemory<byte> Raw => _raw;

    public IReadOnlyList<XRayChunk> Chunks => _chunks;

    public IReadOnlyList<uint> ChunkTypes => _chunkTypes;

    public static XRayContainer FromBytes(ReadOnlySpan<byte> data)
    {
        var original = data.ToArray();
        var snapshot = original.AsSpan();
        if (snapshot.Length < 12)
        {
            throw Error("заголовок короче 12 байт");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(snapshot);
        var version = BinaryPrimitives.ReadUInt32LittleEndian(snapshot[4..]);
        var unpackedSize = BinaryPrimitives.ReadUInt32LittleEndian(snapshot[8..]);

        if (magic != Signature)
        {
            throw Error($"неверная сигнатура 0x{magic:X8}, ожидалось 0x{Signature:X8}");
        }

        if (version is not (3 or 5 or 6))
        {
            throw Error($"неподдерживаемая версия контейнера {version}; поддерживаются 3, 5, 6");
        }

        if (unpackedSize == 0 || unpackedSize > MaximumUnpackedSize)
        {
            throw Error($"недопустимый распакованный размер: {unpackedSize}");
        }

        byte[] raw;
        try
        {
            raw = Lzo1xCodec.Decompress(snapshot[12..], checked((int)unpackedSize));
        }
        catch (InvalidDataException exception)
        {
            throw Error($"ошибка LZO: {exception.Message}", exception);
        }

        var chunks = ParseChunksOwned(raw);
        return new XRayContainer(
            original,
            magic,
            version,
            checked((int)unpackedSize),
            raw,
            chunks);
    }

    public static IReadOnlyList<XRayChunk> ParseChunks(ReadOnlySpan<byte> raw) =>
        ParseChunksOwned(raw.ToArray());

    private static IReadOnlyList<XRayChunk> ParseChunksOwned(byte[] raw)
    {
        var chunks = new List<XRayChunk>();
        var offset = 0;
        while (offset < raw.Length)
        {
            if (raw.Length - offset < sizeof(uint) * 2)
            {
                throw Error($"заголовок chunk обрезан на смещении 0x{offset:X}");
            }

            var type = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset + sizeof(uint)));
            var dataStart = offset + sizeof(uint) * 2;
            var remaining = raw.Length - dataStart;
            if (size > remaining)
            {
                var end = (ulong)dataStart + size;
                throw Error(
                    $"chunk type={type} обрезан: конец 0x{end:X}, " +
                    $"payload заканчивается на 0x{raw.Length:X}");
            }

            var chunkSize = checked((int)size);
            chunks.Add(new XRayChunk(
                type,
                offset,
                chunkSize,
                raw.AsMemory(dataStart, chunkSize)));
            offset = checked(dataStart + chunkSize);
        }

        if (chunks.Count == 0)
        {
            throw Error("payload не содержит chunks");
        }

        return Array.AsReadOnly(chunks.ToArray());
    }

    private static XRayFormatException Error(string message, Exception? innerException = null) =>
        new($"X-Ray: {message}", innerException);
}
