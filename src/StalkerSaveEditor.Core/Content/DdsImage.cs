using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace StalkerSaveEditor.Core.Content;

/// <summary>An RGBA8 image decoded from the DDS formats used by X-Ray icon atlases.</summary>
internal sealed class RgbaImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>Row-major RGBA, 4 bytes per pixel.</summary>
    public byte[] Pixels { get; } = pixels;

    public RgbaImage? Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x >= Width || y >= Height) return null;
        width = Math.Min(width, Width - x);
        height = Math.Min(height, Height - y);
        var result = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(Pixels, ((y + row) * Width + x) * 4, result, row * width * 4, width * 4);
        }

        return new RgbaImage(width, height, result);
    }

    /// <summary>Minimal PNG encoder (RGBA8, filter 0, zlib) — enough for cached icons.</summary>
    public byte[] ToPng()
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var row = 0; row < Height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(Pixels, row * Width * 4, Width * 4);
            }
        }

        WriteChunk(output, "IDAT"u8, raw.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        output.Write(type);
        output.Write(data);
        var crc = Crc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        output.Write(buffer);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>DDS decoder for DXT1/DXT3/DXT5 and uncompressed 24/32-bit (port of the Python oracle's <c>decode_dds</c>).</summary>
internal static class DdsImage
{
    private const int HeaderSize = 128;

    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..4].SequenceEqual("DDS "u8))
        {
            throw new InvalidDataException("Not a DDS image.");
        }

        var height = checked((int)U32(data, 12));
        var width = checked((int)U32(data, 16));
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216)
        {
            throw new InvalidDataException("DDS dimensions are invalid.");
        }

        var pixelFlags = U32(data, 80);
        var payload = data[HeaderSize..];
        if ((pixelFlags & 0x4) != 0)
        {
            return new RgbaImage(width, height, DecodeDxt(payload, width, height, data.Slice(84, 4)));
        }

        if ((pixelFlags & 0x40) != 0)
        {
            return new RgbaImage(width, height, DecodeUncompressed(data, payload, width, height));
        }

        throw new InvalidDataException("Unsupported DDS pixel format.");
    }

    private static byte[] DecodeUncompressed(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, int width, int height)
    {
        var bits = U32(header, 88);
        if (bits is not (24 or 32)) throw new InvalidDataException("Unsupported uncompressed DDS pixel size.");
        var bytesPerPixel = (int)bits / 8;
        var pitch = (int)U32(header, 20);
        if (pitch == 0) pitch = width * bytesPerPixel;
        uint red = U32(header, 92), green = U32(header, 96), blue = U32(header, 100), alpha = U32(header, 104);
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = y * pitch + x * bytesPerPixel;
                if (start + bytesPerPixel > payload.Length) throw new InvalidDataException("DDS pixel data is truncated.");
                uint value = 0;
                for (var index = 0; index < bytesPerPixel; index++) value |= (uint)payload[start + index] << (8 * index);
                var pixel = (y * width + x) * 4;
                rgba[pixel] = Channel(value, red);
                rgba[pixel + 1] = Channel(value, green);
                rgba[pixel + 2] = Channel(value, blue);
                rgba[pixel + 3] = alpha != 0 ? Channel(value, alpha) : (byte)255;
            }
        }

        return rgba;
    }

    private static byte[] DecodeDxt(ReadOnlySpan<byte> data, int width, int height, ReadOnlySpan<byte> fourCc)
    {
        var kind = fourCc.SequenceEqual("DXT1"u8) ? 1 : fourCc.SequenceEqual("DXT3"u8) ? 3 : fourCc.SequenceEqual("DXT5"u8) ? 5 : 0;
        if (kind == 0) throw new InvalidDataException("Unsupported DDS compression.");
        var blockSize = kind == 1 ? 8 : 16;
        var rgba = new byte[width * height * 4];
        Span<uint> colors = stackalloc uint[4];
        Span<byte> alphas = stackalloc byte[16];
        Span<byte> alphaValues = stackalloc byte[8];
        var offset = 0;
        for (var blockY = 0; blockY < height; blockY += 4)
        {
            for (var blockX = 0; blockX < width; blockX += 4)
            {
                if (offset + blockSize > data.Length) throw new InvalidDataException("DDS block data is truncated.");
                var block = data.Slice(offset, blockSize);
                offset += blockSize;
                uint colorBits;
                if (kind == 1)
                {
                    Colors(block, allowTransparent: true, colors);
                    colorBits = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
                    alphas.Fill(255);
                }
                else if (kind == 3)
                {
                    Colors(block[8..], allowTransparent: false, colors);
                    var alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block);
                    for (var index = 0; index < 16; index++) alphas[index] = (byte)(((alphaBits >> (index * 4)) & 0xF) * 17);
                    colorBits = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
                }
                else
                {
                    Colors(block[8..], allowTransparent: false, colors);
                    int a0 = block[0], a1 = block[1];
                    alphaValues[0] = (byte)a0;
                    alphaValues[1] = (byte)a1;
                    if (a0 > a1)
                    {
                        for (var step = 1; step <= 6; step++) alphaValues[step + 1] = (byte)(((7 - step) * a0 + step * a1) / 7);
                    }
                    else
                    {
                        for (var step = 1; step <= 4; step++) alphaValues[step + 1] = (byte)(((5 - step) * a0 + step * a1) / 5);
                        alphaValues[6] = 0;
                        alphaValues[7] = 255;
                    }

                    ulong alphaBits = 0;
                    for (var index = 0; index < 6; index++) alphaBits |= (ulong)block[2 + index] << (8 * index);
                    for (var index = 0; index < 16; index++) alphas[index] = alphaValues[(int)((alphaBits >> (index * 3)) & 0x7)];
                    colorBits = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
                }

                for (var localY = 0; localY < 4; localY++)
                {
                    for (var localX = 0; localX < 4; localX++)
                    {
                        int x = blockX + localX, y = blockY + localY;
                        if (x >= width || y >= height) continue;
                        var index = localY * 4 + localX;
                        var color = colors[(int)((colorBits >> (index * 2)) & 0x3)];
                        var pixel = (y * width + x) * 4;
                        rgba[pixel] = (byte)(color >> 16 & 0xFF);
                        rgba[pixel + 1] = (byte)(color >> 8);
                        rgba[pixel + 2] = (byte)color;
                        rgba[pixel + 3] = color == 0xFF000000 ? (byte)0 : alphas[index];
                    }
                }
            }
        }

        return rgba;
    }

    /// <summary>Palette as 0x00RRGGBB; transparent black encoded as 0 with alpha handled by the caller for DXT1.</summary>
    private static void Colors(ReadOnlySpan<byte> block, bool allowTransparent, Span<uint> colors)
    {
        var first = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var second = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var (r0, g0, b0) = Rgb565(first);
        var (r1, g1, b1) = Rgb565(second);
        colors[0] = Pack(r0, g0, b0);
        colors[1] = Pack(r1, g1, b1);
        if (first > second || !allowTransparent)
        {
            colors[2] = Pack((2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3);
            colors[3] = Pack((r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3);
        }
        else
        {
            colors[2] = Pack((r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2);
            colors[3] = 0xFF000000; // marker: transparent black
        }
    }

    private static uint Pack(int r, int g, int b) => ((uint)r << 16) | ((uint)g << 8) | (uint)b;

    private static (int R, int G, int B) Rgb565(int value) =>
        (((value >> 11) & 0x1F) * 255 / 31, ((value >> 5) & 0x3F) * 255 / 63, (value & 0x1F) * 255 / 31);

    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0) return 0;
        var shift = BitOperations.TrailingZeroCount(mask);
        var widthBits = BitOperations.PopCount(mask);
        var raw = (value & mask) >> shift;
        var maximum = (1u << widthBits) - 1;
        return (byte)((raw * 255 + maximum / 2) / maximum);
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
