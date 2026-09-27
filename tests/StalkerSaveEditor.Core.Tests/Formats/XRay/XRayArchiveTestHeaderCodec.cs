using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

internal static class LzhufTestEncoder
{
    private const int F = 60;
    private const int Threshold = 2;
    private const int MaximumFrequency = 0x4000;
    private const int CharacterCount = 256 - Threshold + F;
    private const int TreeSize = CharacterCount * 2 - 1;
    private const int Root = TreeSize - 1;

    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        var encoder = new Encoder();
        foreach (var value in input)
        {
            encoder.EncodeCharacter(value);
        }

        var bits = encoder.Finish();
        var result = new byte[sizeof(uint) + bits.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)input.Length));
        bits.CopyTo(result, sizeof(uint));
        return result;
    }

    public static byte[] EncodeLiteralThenThreeByteMatch(byte value)
    {
        var encoder = new Encoder();
        encoder.EncodeCharacter(value);
        encoder.EncodeCharacter(256);
        encoder.EncodeDistanceZeroPosition();
        var bits = encoder.Finish();
        var result = new byte[sizeof(uint) + bits.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 4);
        bits.CopyTo(result, sizeof(uint));
        return result;
    }

    private sealed class Encoder
    {
        private readonly int[] _frequency = new int[TreeSize + 1];
        private readonly int[] _son = new int[TreeSize];
        private readonly int[] _parent = new int[TreeSize + CharacterCount];
        private readonly List<byte> _bytes = [];
        private byte _current;
        private int _bitCount;

        public Encoder()
        {
            for (var index = 0; index < CharacterCount; index++)
            {
                _frequency[index] = 1;
                _son[index] = index + TreeSize;
                _parent[index + TreeSize] = index;
            }

            var leaf = 0;
            for (var node = CharacterCount; node <= Root; node++)
            {
                _frequency[node] = _frequency[leaf] + _frequency[leaf + 1];
                _son[node] = leaf;
                _parent[leaf] = node;
                _parent[leaf + 1] = node;
                leaf += 2;
            }

            _frequency[TreeSize] = 0xFFFF;
            _parent[Root] = 0;
        }

        public void EncodeCharacter(int symbol)
        {
            var path = new List<int>();
            var node = _parent[symbol + TreeSize];
            while (true)
            {
                path.Add(node & 1);
                node = _parent[node];
                if (node == Root)
                {
                    break;
                }
            }

            for (var index = path.Count - 1; index >= 0; index--)
            {
                WriteBit(path[index]);
            }

            Update(symbol);
        }

        public void EncodeDistanceZeroPosition()
        {
            for (var bit = 0; bit < 9; bit++)
            {
                WriteBit(0);
            }
        }

        public byte[] Finish()
        {
            if (_bitCount != 0)
            {
                _bytes.Add((byte)(_current << (8 - _bitCount)));
                _current = 0;
                _bitCount = 0;
            }

            return [.. _bytes];
        }

        private void WriteBit(int bit)
        {
            _current = (byte)((_current << 1) | bit);
            _bitCount++;
            if (_bitCount == 8)
            {
                _bytes.Add(_current);
                _current = 0;
                _bitCount = 0;
            }
        }

        private void Update(int symbol)
        {
            if (_frequency[Root] == MaximumFrequency)
            {
                var leaf = 0;
                for (var node = 0; node < TreeSize; node++)
                {
                    if (_son[node] >= TreeSize)
                    {
                        _frequency[leaf] = (_frequency[node] + 1) / 2;
                        _son[leaf] = _son[node];
                        leaf++;
                    }
                }

                var left = 0;
                var right = CharacterCount;
                while (right < TreeSize)
                {
                    var frequency = _frequency[left] + _frequency[left + 1];
                    var insert = right - 1;
                    while (frequency < _frequency[insert])
                    {
                        insert--;
                    }

                    insert++;
                    for (var index = right; index > insert; index--)
                    {
                        _frequency[index] = _frequency[index - 1];
                        _son[index] = _son[index - 1];
                    }

                    _frequency[insert] = frequency;
                    _son[insert] = left;
                    left += 2;
                    right++;
                }

                for (var node = 0; node < TreeSize; node++)
                {
                    var child = _son[node];
                    _parent[child] = node;
                    if (child < TreeSize)
                    {
                        _parent[child + 1] = node;
                    }
                }
            }

            var current = _parent[symbol + TreeSize];
            while (true)
            {
                var nextFrequency = _frequency[current] + 1;
                _frequency[current] = nextFrequency;
                var child = current + 1;
                if (nextFrequency > _frequency[child])
                {
                    while (nextFrequency > _frequency[child + 1])
                    {
                        child++;
                    }

                    (_frequency[current], _frequency[child]) = (_frequency[child], nextFrequency);
                    var leftChild = _son[current];
                    _parent[leftChild] = child;
                    if (leftChild < TreeSize)
                    {
                        _parent[leftChild + 1] = child;
                    }

                    var rightChild = _son[child];
                    _son[child] = leftChild;
                    _parent[rightChild] = current;
                    if (rightChild < TreeSize)
                    {
                        _parent[rightChild + 1] = current;
                    }

                    _son[current] = rightChild;
                    current = child;
                }

                current = _parent[current];
                if (current == 0)
                {
                    break;
                }
            }
        }
    }
}

internal static class XRayScrambleTestEncoder
{
    public static byte[] Encrypt(ReadOnlySpan<byte> data, bool worldWide)
    {
        var seed = worldWide ? 0x16EB2EBu : 0x131A9D3u;
        var seed0 = worldWide ? 0x5BBC4Bu : 0x1329436u;
        var sbox = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var sizeMultiplier = worldWide ? 4 : 8;
        for (var index = 0; index < sizeMultiplier * 256; index++)
        {
            seed0 = Next(seed0);
            var first = (byte)(seed0 >> 24);
            byte second;
            do
            {
                seed0 = Next(seed0);
                second = (byte)(seed0 >> 24);
            }
            while (first == second);

            (sbox[first], sbox[second]) = (sbox[second], sbox[first]);
        }

        var output = new byte[data.Length];
        for (var index = 0; index < data.Length; index++)
        {
            seed = Next(seed);
            output[index] = (byte)(sbox[data[index]] ^ (byte)(seed >> 24));
        }

        return output;
    }

    private static uint Next(uint seed) => unchecked(1 + seed * 0x8088405u);
}
