using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

internal static class XRayArchiveFixtureBuilder
{
    public static byte[] BuildUncompressed(string name, byte[] data)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var headerSize = 18 + nameBytes.Length;
        var dataStart = checked((uint)(8 + headerSize + 8));
        using var header = new MemoryStream(headerSize);
        WriteUInt16(header, checked((ushort)(16 + nameBytes.Length)));
        WriteUInt32(header, checked((uint)data.Length));
        WriteUInt32(header, checked((uint)data.Length));
        WriteUInt32(header, Crc32(data));
        header.Write(nameBytes);
        WriteUInt32(header, dataStart);
        return [.. Chunk(1, header.ToArray()), .. Chunk(0, data)];
    }

    private static byte[] Chunk(uint type, byte[] body)
    {
        var result = new byte[sizeof(uint) * 2 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, type);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sizeof(uint)), checked((uint)body.Length));
        body.CopyTo(result, sizeof(uint) * 2);
        return result;
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
