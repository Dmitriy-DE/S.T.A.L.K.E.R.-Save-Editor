using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

internal static class Stalker2ContainerWriter
{
    internal static byte[] Build(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> stream)
    {
        var output = new byte[checked(sizeof(uint) + stream.Length + sizeof(uint))];
        BinaryPrimitives.WriteUInt32LittleEndian(output, checked((uint)raw.Length));
        stream.CopyTo(output.AsSpan(sizeof(uint)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(output.Length - sizeof(uint)),
            Crc32(output.AsSpan(0, output.Length - sizeof(uint))));
        return output;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
