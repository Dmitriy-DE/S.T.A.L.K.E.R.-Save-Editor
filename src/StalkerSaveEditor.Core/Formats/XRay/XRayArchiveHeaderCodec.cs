using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Formats.XRay;

internal static class XRayArchiveHeaderCodec
{
    private const int LzhufWindowSize = 4096;
    private const int LzhufLookaheadSize = 60;
    private const int LzhufThreshold = 2;
    private const int LzhufMaximumFrequency = 0x4000;
    private const int LzhufCharacterCount = 256 - LzhufThreshold + LzhufLookaheadSize;
    private const int LzhufTreeSize = LzhufCharacterCount * 2 - 1;
    private const int LzhufRoot = LzhufTreeSize - 1;
    private const int MaximumHeaderOutput = 64 * 1024 * 1024;

    private static readonly byte[] DistanceCode = BuildDistanceCode();
    private static readonly byte[] DistanceLength = BuildDistanceLength();

    public static byte[] DecodeLzhuf(ReadOnlySpan<byte> code)
    {
        if (code.Length < sizeof(uint))
        {
            throw Error("LZ-Huffman header is truncated");
        }

        var textSize = BinaryPrimitives.ReadUInt32LittleEndian(code);
        if (textSize > MaximumHeaderOutput)
        {
            throw Error("LZ-Huffman output exceeds the size limit");
        }

        var source = code.ToArray();
        var frequency = new int[LzhufTreeSize + 1];
        var son = new int[LzhufTreeSize];
        var parent = new int[LzhufTreeSize + LzhufCharacterCount];
        for (var index = 0; index < LzhufCharacterCount; index++)
        {
            frequency[index] = 1;
            son[index] = index + LzhufTreeSize;
            parent[index + LzhufTreeSize] = index;
        }

        var leaf = 0;
        for (var node = LzhufCharacterCount; node <= LzhufRoot; node++)
        {
            frequency[node] = frequency[leaf] + frequency[leaf + 1];
            son[node] = leaf;
            parent[leaf] = node;
            parent[leaf + 1] = node;
            leaf += 2;
        }

        frequency[LzhufTreeSize] = 0xFFFF;
        parent[LzhufRoot] = 0;

        var sourcePosition = sizeof(uint);
        uint bitBuffer = 0;
        var bitCount = 0;
        var output = GC.AllocateUninitializedArray<byte>(checked((int)textSize));
        var textBuffer = new byte[LzhufWindowSize + LzhufLookaheadSize - 1];
        Array.Fill(textBuffer, (byte)' ');
        var writePosition = LzhufWindowSize - LzhufLookaheadSize;
        var outputLength = 0;

        // The bit buffer is refilled ahead of use, so a complete stream is read at most two bytes past its end; those
        // read as zero. Anything further means the compressed data is cut short, and decoding on would invent output.
        int NextSourceByte()
        {
            if (sourcePosition < source.Length) return source[sourcePosition++];
            if (sourcePosition++ >= source.Length + 2) throw Error("LZ-Huffman payload is truncated");
            return 0;
        }

        int ReadBit()
        {
            while (bitCount <= 8)
            {
                var value = NextSourceByte();
                bitBuffer |= (uint)value << (8 - bitCount);
                bitCount += 8;
            }

            var valueAtTop = bitBuffer;
            bitBuffer <<= 1;
            bitCount--;
            return (int)((valueAtTop >> 15) & 1);
        }

        int ReadByte()
        {
            while (bitCount <= 8)
            {
                var value = NextSourceByte();
                bitBuffer |= (uint)value << (8 - bitCount);
                bitCount += 8;
            }

            var valueAtTop = bitBuffer;
            bitBuffer <<= 8;
            bitCount -= 8;
            return (int)((valueAtTop & 0xFF00) >> 8);
        }

        void Update(int symbol)
        {
            if (frequency[LzhufRoot] == LzhufMaximumFrequency)
            {
                var firstLeaf = 0;
                for (var node = 0; node < LzhufTreeSize; node++)
                {
                    if (son[node] >= LzhufTreeSize)
                    {
                        frequency[firstLeaf] = (frequency[node] + 1) / 2;
                        son[firstLeaf] = son[node];
                        firstLeaf++;
                    }
                }

                var left = 0;
                var right = LzhufCharacterCount;
                while (right < LzhufTreeSize)
                {
                    var nextFrequency = frequency[left] + frequency[left + 1];
                    var insert = right - 1;
                    while (nextFrequency < frequency[insert])
                    {
                        insert--;
                    }

                    insert++;
                    for (var index = right; index > insert; index--)
                    {
                        frequency[index] = frequency[index - 1];
                        son[index] = son[index - 1];
                    }

                    frequency[insert] = nextFrequency;
                    son[insert] = left;
                    left += 2;
                    right++;
                }

                for (var node = 0; node < LzhufTreeSize; node++)
                {
                    var child = son[node];
                    parent[child] = node;
                    if (child < LzhufTreeSize)
                    {
                        parent[child + 1] = node;
                    }
                }
            }

            var nodeIndex = parent[symbol + LzhufTreeSize];
            while (true)
            {
                var nextFrequency = frequency[nodeIndex] + 1;
                frequency[nodeIndex] = nextFrequency;
                var child = nodeIndex + 1;
                if (nextFrequency > frequency[child])
                {
                    while (nextFrequency > frequency[child + 1])
                    {
                        child++;
                    }

                    (frequency[nodeIndex], frequency[child]) = (frequency[child], nextFrequency);
                    var leftChild = son[nodeIndex];
                    parent[leftChild] = child;
                    if (leftChild < LzhufTreeSize)
                    {
                        parent[leftChild + 1] = child;
                    }

                    var rightChild = son[child];
                    son[child] = leftChild;
                    parent[rightChild] = nodeIndex;
                    if (rightChild < LzhufTreeSize)
                    {
                        parent[rightChild + 1] = nodeIndex;
                    }

                    son[nodeIndex] = rightChild;
                    nodeIndex = child;
                }

                nodeIndex = parent[nodeIndex];
                if (nodeIndex == 0)
                {
                    break;
                }
            }
        }

        int DecodeCharacter()
        {
            var node = son[LzhufRoot];
            while (node < LzhufTreeSize)
            {
                node = son[node + ReadBit()];
            }

            var symbol = node - LzhufTreeSize;
            Update(symbol);
            return symbol;
        }

        while (outputLength < output.Length)
        {
            var symbol = DecodeCharacter();
            if (symbol < 256)
            {
                var value = (byte)symbol;
                output[outputLength++] = value;
                textBuffer[writePosition] = value;
                writePosition = (writePosition + 1) & (LzhufWindowSize - 1);
                continue;
            }

            var encodedPosition = ReadByte();
            var distance = DistanceCode[encodedPosition] << 6;
            var bitLength = DistanceLength[encodedPosition] - 2;
            for (var index = 0; index < bitLength; index++)
            {
                encodedPosition = (encodedPosition << 1) + ReadBit();
            }

            distance |= encodedPosition & 0x3F;
            var copyPosition = (writePosition - distance - 1) & (LzhufWindowSize - 1);
            var copyLength = symbol - 255 + LzhufThreshold;
            for (var index = 0; index < copyLength && outputLength < output.Length; index++)
            {
                var value = textBuffer[copyPosition];
                output[outputLength++] = value;
                textBuffer[writePosition] = value;
                copyPosition = (copyPosition + 1) & (LzhufWindowSize - 1);
                writePosition = (writePosition + 1) & (LzhufWindowSize - 1);
            }
        }

        return output;
    }

    public static byte[] DecryptScramble(ReadOnlySpan<byte> data, bool worldWide)
    {
        var seed = worldWide ? 0x16EB2EBu : 0x131A9D3u;
        var seed0 = worldWide ? 0x5BBC4Bu : 0x1329436u;
        var sizeMultiplier = worldWide ? 4 : 8;
        var sbox = new byte[256];
        for (var index = 0; index < sbox.Length; index++)
        {
            sbox[index] = checked((byte)index);
        }

        for (var index = 0; index < sizeMultiplier * 256; index++)
        {
            seed0 = NextSeed(seed0);
            var first = (byte)(seed0 >> 24);
            byte second;
            do
            {
                seed0 = NextSeed(seed0);
                second = (byte)(seed0 >> 24);
            }
            while (first == second);

            (sbox[first], sbox[second]) = (sbox[second], sbox[first]);
        }

        var inverse = new byte[256];
        for (var index = 0; index < sbox.Length; index++)
        {
            inverse[sbox[index]] = checked((byte)index);
        }

        var output = new byte[data.Length];
        for (var index = 0; index < data.Length; index++)
        {
            seed = NextSeed(seed);
            output[index] = inverse[data[index] ^ (byte)(seed >> 24)];
        }

        return output;
    }

    private static byte[] BuildDistanceCode()
    {
        var values = new List<byte>(64);
        values.AddRange(Enumerable.Repeat((byte)0, 32));
        values.AddRange(Enumerable.Repeat((byte)1, 16));
        values.AddRange(Enumerable.Repeat((byte)2, 16));
        values.AddRange(Enumerable.Repeat((byte)3, 16));
        for (byte value = 4; value < 12; value++)
        {
            values.AddRange(Enumerable.Repeat(value, 8));
        }

        for (byte value = 12; value < 24; value++)
        {
            values.AddRange(Enumerable.Repeat(value, 4));
        }

        for (byte value = 24; value < 48; value++)
        {
            values.AddRange(Enumerable.Repeat(value, 2));
        }

        for (byte value = 48; value < 64; value++)
        {
            values.Add(value);
        }

        return [.. values];
    }

    private static byte[] BuildDistanceLength() =>
    [
        .. Enumerable.Repeat((byte)3, 32),
        .. Enumerable.Repeat((byte)4, 48),
        .. Enumerable.Repeat((byte)5, 64),
        .. Enumerable.Repeat((byte)6, 48),
        .. Enumerable.Repeat((byte)7, 48),
        .. Enumerable.Repeat((byte)8, 16),
    ];

    private static uint NextSeed(uint seed) => unchecked(1 + seed * 0x8088405u);

    private static XRayFormatException Error(string message) =>
        new($"X-Ray archive: {message}");
}
