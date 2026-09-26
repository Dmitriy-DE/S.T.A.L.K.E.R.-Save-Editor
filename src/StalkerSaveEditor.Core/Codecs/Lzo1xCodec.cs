using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Codecs;

public static class Lzo1xCodec
{
    public const int MaximumUnpackedSize = 512 * 1024 * 1024;

    public static byte[] Compress(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return [0x11, 0, 0];
        }

        var prefixLength = LiteralPrefixLength(payload.Length);
        var output = GC.AllocateUninitializedArray<byte>(prefixLength + payload.Length + 3);
        var position = WriteLiteralPrefix(output, payload.Length);
        payload.CopyTo(output.AsSpan(position));
        output[^3] = 0x11;
        output[^2] = 0;
        output[^1] = 0;
        return output;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> stream, int expectedSize)
    {
        if (expectedSize < 0 || expectedSize > MaximumUnpackedSize)
        {
            throw new InvalidDataException($"Invalid LZO output size: {expectedSize}.");
        }

        if (stream.Length < 3)
        {
            throw new InvalidDataException("LZO stream is too short.");
        }

        var reader = new LzoReader(stream);
        var output = GC.AllocateUninitializedArray<byte>(expectedSize);
        var outputLength = 0;
        var state = 0;
        var bitstreamVersion = 0;

        if (stream.Length >= 5 && stream[0] == 17)
        {
            bitstreamVersion = stream[1];
            reader.Position = 2;
        }

        if (reader.Remaining > 0 && stream[reader.Position] > 17)
        {
            var firstLiteralLength = reader.ReadByte() - 17;
            CopyLiterals(
                ref reader,
                output,
                ref outputLength,
                expectedSize,
                firstLiteralLength);
            state = firstLiteralLength < 4 ? firstLiteralLength : 4;
        }

        while (true)
        {
            var command = reader.ReadByte();
            if (command < 16)
            {
                if (state == 0)
                {
                    var literalLength = (int)command;
                    if (literalLength == 0)
                    {
                        literalLength = ReadExtendedLength(ref reader, 15);
                    }

                    CopyLiterals(
                        ref reader,
                        output,
                        ref outputLength,
                        expectedSize,
                        checked(literalLength + 3));
                    state = 4;
                    continue;
                }

                var nextLiterals = command & 3;
                int matchStart;
                int matchLength;
                if (state != 4)
                {
                    matchStart = outputLength - 1 - (command >> 2) - (reader.ReadByte() << 2);
                    matchLength = 2;
                }
                else
                {
                    matchStart = outputLength - (1 + 0x0800) - (command >> 2) - (reader.ReadByte() << 2);
                    matchLength = 3;
                }

                CopyMatch(output, ref outputLength, expectedSize, matchStart, matchLength);
                CopyLiterals(ref reader, output, ref outputLength, expectedSize, nextLiterals);
                state = nextLiterals;
                continue;
            }

            if (command >= 64)
            {
                var nextLiterals = command & 3;
                var matchStart = outputLength - 1 - ((command >> 2) & 7) - (reader.ReadByte() << 3);
                var matchLength = (command >> 5) + 1;
                CopyMatch(output, ref outputLength, expectedSize, matchStart, matchLength);
                CopyLiterals(ref reader, output, ref outputLength, expectedSize, nextLiterals);
                state = nextLiterals;
                continue;
            }

            if (command >= 32)
            {
                var matchLength = (command & 31) + 2;
                if (matchLength == 2)
                {
                    matchLength += ReadExtendedLength(ref reader, 31);
                }

                var encoded = reader.ReadUInt16();
                var matchStart = outputLength - 1 - (encoded >> 2);
                var nextLiterals = encoded & 3;
                CopyMatch(output, ref outputLength, expectedSize, matchStart, matchLength);
                CopyLiterals(ref reader, output, ref outputLength, expectedSize, nextLiterals);
                state = nextLiterals;
                continue;
            }

            reader.Require(2);
            var peeked = reader.PeekUInt16();
            var trailingLiterals = peeked & 3;
            if (
                bitstreamVersion != 0
                && (peeked & 0xFFFC) == 0xFFFC
                && (command & 0xF8) == 0x18)
            {
                reader.ReadUInt16();
                if (reader.Remaining == 0)
                {
                    throw new InvalidDataException("Truncated LZO zero-run extension.");
                }

                var zeroLength = (command & 7) | (reader.ReadByte() << 3);
                zeroLength += 4;
                EnsureOutputCapacity(outputLength, expectedSize, zeroLength);
                output.AsSpan(outputLength, zeroLength).Clear();
                outputLength += zeroLength;
                CopyLiterals(ref reader, output, ref outputLength, expectedSize, trailingLiterals);
                state = trailingLiterals;
                continue;
            }

            var shortMatchLength = (command & 7) + 2;
            if (shortMatchLength == 2)
            {
                shortMatchLength += ReadExtendedLength(ref reader, 7);
            }

            var shortEncoded = reader.ReadUInt16();
            trailingLiterals = shortEncoded & 3;
            int shortMatchStart;
            if ((command & 8) == 0)
            {
                shortMatchStart = outputLength - (shortEncoded >> 2);
                if (shortMatchStart == outputLength)
                {
                    if (shortMatchLength != 3)
                    {
                        throw new InvalidDataException("Invalid LZO end marker.");
                    }

                    if (reader.Position != stream.Length)
                    {
                        throw new InvalidDataException("Trailing bytes after LZO end marker.");
                    }

                    break;
                }
            }
            else
            {
                shortMatchStart = outputLength - ((command & 8) << 11) - (shortEncoded >> 2);
            }

            shortMatchStart -= 0x4000;
            CopyMatch(output, ref outputLength, expectedSize, shortMatchStart, shortMatchLength);
            CopyLiterals(ref reader, output, ref outputLength, expectedSize, trailingLiterals);
            state = trailingLiterals;
        }

        if (outputLength != expectedSize)
        {
            throw new InvalidDataException(
                $"LZO output has {outputLength} bytes; expected {expectedSize}.");
        }

        return output;
    }

    private static int LiteralPrefixLength(int length)
    {
        if (length <= 238)
        {
            return 1;
        }

        var value = length - 18;
        var prefixLength = 2;
        while (value > byte.MaxValue)
        {
            prefixLength++;
            value -= byte.MaxValue;
        }

        return prefixLength;
    }

    private static int WriteLiteralPrefix(Span<byte> output, int length)
    {
        if (length <= 238)
        {
            output[0] = (byte)(17 + length);
            return 1;
        }

        var position = 0;
        var value = length - 18;
        output[position++] = 0;
        while (value > byte.MaxValue)
        {
            output[position++] = 0;
            value -= byte.MaxValue;
        }

        output[position++] = (byte)value;
        return position;
    }

    private static int ReadExtendedLength(ref LzoReader reader, int baseLength)
    {
        var zeroes = 0;
        byte value;
        while ((value = reader.ReadByte()) == 0)
        {
            zeroes++;
            if (zeroes > (MaximumUnpackedSize / byte.MaxValue) + 1)
            {
                throw new InvalidDataException("LZO length extension is too large.");
            }
        }

        var result = (long)zeroes * byte.MaxValue + baseLength + value;
        if (result > int.MaxValue)
        {
            throw new InvalidDataException("LZO length extension overflows the supported range.");
        }

        return (int)result;
    }

    private static void CopyLiterals(
        ref LzoReader reader,
        byte[] output,
        ref int outputLength,
        int expectedSize,
        int length)
    {
        if (length < 0 || length > reader.Remaining)
        {
            throw new InvalidDataException("Truncated LZO literal run.");
        }

        EnsureOutputCapacity(outputLength, expectedSize, length);
        reader.ReadSpan(length).CopyTo(output.AsSpan(outputLength));
        outputLength += length;
    }

    private static void CopyMatch(
        byte[] output,
        ref int outputLength,
        int expectedSize,
        int start,
        int length)
    {
        if (length <= 0 || start < 0 || start >= outputLength)
        {
            throw new InvalidDataException("Invalid LZO back-reference.");
        }

        EnsureOutputCapacity(outputLength, expectedSize, length);
        var distance = outputLength - start;
        for (var index = 0; index < length; index++)
        {
            output[outputLength + index] = output[start + (index % distance)];
        }

        outputLength += length;
    }

    private static void EnsureOutputCapacity(int currentLength, int expectedSize, int additionalLength)
    {
        if (additionalLength < 0 || additionalLength > expectedSize - currentLength)
        {
            throw new InvalidDataException("LZO output exceeds its advertised size.");
        }
    }

    private ref struct LzoReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; set; }

        public int Remaining => _data.Length - Position;

        public byte ReadByte()
        {
            Require(1);
            return _data[Position++];
        }

        public ushort ReadUInt16()
        {
            Require(2);
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(Position, 2));
            Position += 2;
            return value;
        }

        public ushort PeekUInt16()
        {
            Require(2);
            return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(Position, 2));
        }

        public ReadOnlySpan<byte> ReadSpan(int length)
        {
            Require(length);
            var result = _data.Slice(Position, length);
            Position += length;
            return result;
        }

        public void Require(int length)
        {
            if (length < 0 || length > Remaining)
            {
                throw new InvalidDataException("Truncated LZO stream.");
            }
        }
    }
}
