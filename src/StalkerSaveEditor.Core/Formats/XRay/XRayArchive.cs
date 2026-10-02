using StalkerSaveEditor.Core.Codecs;

namespace StalkerSaveEditor.Core.Formats.XRay;

public sealed class XRayArchive : IDisposable
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private readonly Stream _source;
    private readonly long _archiveStart;
    private readonly long _archiveLength;
    private readonly bool _leaveOpen;
    private readonly Dictionary<string, XRayArchiveEntry> _entriesByName;
    private readonly Dictionary<string, XRayArchiveEntry> _entriesByNameIgnoreCase;
    private readonly object _streamLock = new();
    private bool _disposed;

    internal XRayArchive(
        Stream source,
        long archiveStart,
        long archiveLength,
        IReadOnlyList<XRayArchiveEntry> entries,
        bool leaveOpen)
    {
        _source = source;
        _archiveStart = archiveStart;
        _archiveLength = archiveLength;
        _leaveOpen = leaveOpen;
        Entries = entries;
        _entriesByName = new Dictionary<string, XRayArchiveEntry>(StringComparer.Ordinal);
        _entriesByNameIgnoreCase = new Dictionary<string, XRayArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var normalizedName = NormalizeName(entry.Name);
            _entriesByName[normalizedName] = entry;
            _entriesByNameIgnoreCase[normalizedName] = entry;
        }
    }

    public IReadOnlyList<XRayArchiveEntry> Entries { get; }

    public byte[] ReadFile(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var normalizedName = NormalizeName(name);
        if (!_entriesByName.TryGetValue(normalizedName, out var entry) &&
            !_entriesByNameIgnoreCase.TryGetValue(normalizedName, out entry))
        {
            throw new KeyNotFoundException($"X-Ray archive entry '{name}' was not found.");
        }

        if (entry.Offset == 0)
        {
            throw Error($"entry '{entry.Name}' has no data offset");
        }

        if (entry.UncompressedSize > Lzo1xCodec.MaximumUnpackedSize ||
            entry.CompressedSize > Lzo1xCodec.MaximumUnpackedSize)
        {
            throw Error($"entry '{entry.Name}' exceeds the supported size limit");
        }

        if ((ulong)entry.Offset + entry.CompressedSize > (ulong)_archiveLength)
        {
            throw Error($"entry '{entry.Name}' exceeds the archive stream");
        }

        var stored = GC.AllocateUninitializedArray<byte>(checked((int)entry.CompressedSize));
        lock (_streamLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _source.Seek(checked(_archiveStart + entry.Offset), SeekOrigin.Begin);
                _source.ReadExactly(stored);
            }
            catch (Exception exception) when (exception is IOException or OverflowException)
            {
                throw Error($"entry '{entry.Name}' could not be read", exception);
            }
        }

        byte[] data;
        try
        {
            data = entry.CompressedSize == entry.UncompressedSize
                ? stored
                : Lzo1xCodec.Decompress(stored, checked((int)entry.UncompressedSize));
        }
        catch (InvalidDataException exception)
        {
            throw Error($"entry '{entry.Name}' has an invalid LZO stream", exception);
        }

        if (data.Length != entry.UncompressedSize)
        {
            throw Error($"entry '{entry.Name}' has an unexpected uncompressed size");
        }

        if (entry.Crc32 != 0 && ComputeCrc32(data) != entry.Crc32)
        {
            throw Error($"entry '{entry.Name}' failed its CRC32 check");
        }

        return data;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_streamLock)
        {
            if (_disposed)
            {
                return;
            }

            if (!_leaveOpen)
            {
                _source.Dispose();
            }

            _disposed = true;
        }
    }

    private static string NormalizeName(string name) => name.Replace('\\', '/').TrimStart('/');

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc = CrcTable[(byte)(crc ^ value)] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320 : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private static XRayFormatException Error(string message, Exception? innerException = null) =>
        new($"X-Ray archive: {message}", innerException);
}

public sealed record XRayArchiveEntry(
    string Name,
    uint UncompressedSize,
    uint CompressedSize,
    uint Crc32,
    uint Offset);
