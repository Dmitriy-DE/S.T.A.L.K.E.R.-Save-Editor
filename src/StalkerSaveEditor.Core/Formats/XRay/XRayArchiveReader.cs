using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayArchiveReader
{
    private const int MaximumHeaderSize = 64 * 1024 * 1024;
    private const uint CompressedChunkFlag = 0x80000000;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Windows1251 = CreateWindows1251();
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Opens an archive whose entry table is already known (read earlier from the same, unchanged file), without
    /// decoding the table again. Every file read is still bounds-checked and verified against its CRC, so a table
    /// that no longer matches the bytes fails the read instead of returning wrong data.
    /// </summary>
    internal static XRayArchive OpenWithEntries(Stream source, IReadOnlyList<XRayArchiveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);
        if (!source.CanRead || !source.CanSeek)
        {
            source.Dispose();
            throw new ArgumentException("X-Ray archives require a readable, seekable stream.", nameof(source));
        }

        return new XRayArchive(source, source.Position, source.Length - source.Position, entries, leaveOpen: false);
    }

    public static XRayArchive Open(Stream source, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            if (!source.CanRead || !source.CanSeek)
            {
                throw new ArgumentException("X-Ray archives require a readable, seekable stream.", nameof(source));
            }

            var archiveStart = source.Position;
            var archiveLength = source.Length - archiveStart;
            if (archiveLength < 0)
            {
                throw Error("archive stream position exceeds its length");
            }

            var entries = ReadEntries(source, archiveStart, archiveLength);
            return new XRayArchive(source, archiveStart, archiveLength, entries, leaveOpen);
        }
        catch (Exception exception)
        {
            if (!leaveOpen)
            {
                source.Dispose();
            }

            if (exception is XRayFormatException or ArgumentException)
            {
                throw;
            }

            if (exception is IOException or NotSupportedException or OverflowException)
            {
                throw Error($"could not read archive: {exception.Message}", exception);
            }

            throw;
        }
    }

    private static ReadOnlyCollection<XRayArchiveEntry> ReadEntries(
        Stream source,
        long archiveStart,
        long archiveLength)
    {
        byte[]? headerData = null;
        var headerCompressed = false;
        long? dataStart = null;
        long dataEnd = 0;
        var position = 0L;
        Span<byte> chunkHeader = stackalloc byte[sizeof(uint) * 2];

        while (position < archiveLength)
        {
            if (archiveLength - position < chunkHeader.Length)
            {
                throw Error("truncated archive chunk header");
            }

            ReadAt(source, checked(archiveStart + position), chunkHeader);
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[sizeof(uint)..]);
            var bodyOffset = checked(position + chunkHeader.Length);
            var bodyEnd = checked((ulong)bodyOffset + chunkSize);
            if (bodyEnd > (ulong)archiveLength)
            {
                throw Error("archive chunk exceeds the input stream");
            }

            var baseType = chunkType & ~CompressedChunkFlag;
            if (baseType == 0)
            {
                dataStart ??= bodyOffset;
                dataEnd = Math.Max(dataEnd, checked((long)bodyEnd));
            }
            else if (baseType == 1)
            {
                if (headerData is not null)
                {
                    throw Error("archive contains multiple file-table headers");
                }

                if (chunkSize > MaximumHeaderSize)
                {
                    throw Error("file-table header exceeds the size limit");
                }

                headerData = new byte[checked((int)chunkSize)];
                ReadAt(source, checked(archiveStart + bodyOffset), headerData);
                headerCompressed = (chunkType & CompressedChunkFlag) != 0;
            }

            position = checked((long)bodyEnd);
        }

        if (headerData is null || dataStart is null || dataEnd == 0)
        {
            throw Error("archive has no file-table header or data chunk");
        }

        foreach (var decodedHeader in DecodeHeaders(headerData, headerCompressed))
        {
            if (TryParseEntries(decodedHeader, dataStart.Value, dataEnd, out var entries))
            {
                return Array.AsReadOnly(entries.ToArray());
            }
        }

        throw Error("archive has no verified file-table header variant");
    }

    private static List<byte[]> DecodeHeaders(byte[] headerData, bool compressed)
    {
        if (!compressed)
        {
            return [headerData];
        }

        try
        {
            return [XRayArchiveHeaderCodec.DecodeLzhuf(headerData)];
        }
        catch (XRayFormatException)
        {
            // Some archive generations scramble the same LZ-Huffman stream.
        }

        var decodedHeaders = new List<byte[]>();
        foreach (var worldWide in new[] { true, false })
        {
            try
            {
                decodedHeaders.Add(XRayArchiveHeaderCodec.DecodeLzhuf(
                    XRayArchiveHeaderCodec.DecryptScramble(headerData, worldWide)));
            }
            catch (XRayFormatException)
            {
                // Try the next regional key variant.
            }
        }

        return decodedHeaders;
    }

    private static bool TryParseEntries(
        ReadOnlySpan<byte> header,
        long dataStart,
        long dataEnd,
        out List<XRayArchiveEntry> entries)
    {
        entries = [];
        var position = 0;
        while (position < header.Length)
        {
            if (header.Length - position < 14)
            {
                return false;
            }

            var nameSize = BinaryPrimitives.ReadUInt16LittleEndian(header[position..]);
            var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[(position + 2)..]);
            var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[(position + 6)..]);
            var crc32 = BinaryPrimitives.ReadUInt32LittleEndian(header[(position + 10)..]);
            position += 14;

            var nameLength = nameSize - 16;
            if (nameLength < 0 || nameLength > header.Length - position)
            {
                return false;
            }

            var name = DecodeName(header.Slice(position, nameLength)).Replace('\\', '/');
            position += nameLength;
            if (header.Length - position < sizeof(uint))
            {
                return false;
            }

            var offset = BinaryPrimitives.ReadUInt32LittleEndian(header[position..]);
            position += sizeof(uint);
            if (offset != 0 &&
                ((ulong)offset < (ulong)dataStart || (ulong)offset + compressedSize > (ulong)dataEnd))
            {
                return false;
            }

            entries.Add(new XRayArchiveEntry(name, uncompressedSize, compressedSize, crc32, offset));
        }

        return entries.Count > 0;
    }

    private static string DecodeName(ReadOnlySpan<byte> value)
    {
        var bytes = value.StartsWith(Utf8Preamble) ? value[Utf8Preamble.Length..] : value;
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return Windows1251.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }

    private static Encoding CreateWindows1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static void ReadAt(Stream source, long absolutePosition, Span<byte> buffer)
    {
        source.Seek(absolutePosition, SeekOrigin.Begin);
        source.ReadExactly(buffer);
    }

    private static XRayFormatException Error(string message, Exception? innerException = null) =>
        new($"X-Ray archive: {message}", innerException);
}
