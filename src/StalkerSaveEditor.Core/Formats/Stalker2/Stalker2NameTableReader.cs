using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public sealed class Stalker2NameTables
{
    private const byte BaseSelector = 4;
    private readonly ReadOnlyCollection<IReadOnlyList<string>> _tables;

    private readonly bool _singleTable;

    /// <param name="singleTable">Game 1.0.x: one table for everything, a record names its entry with two bytes.</param>
    internal Stalker2NameTables(IEnumerable<IEnumerable<string>> tables, bool singleTable = false)
    {
        _singleTable = singleTable;
        var values = tables
            .Select(table => (IReadOnlyList<string>)Array.AsReadOnly(table.ToArray()))
            .ToArray();
        _tables = Array.AsReadOnly(values);
    }

    public string? Resolve(ReadOnlySpan<byte> typeKey)
    {
        if (_singleTable)
        {
            if (typeKey.Length < 2) return null;
            var entry = typeKey[0] | (typeKey[1] << 8);
            if (_tables.Count == 0 || entry >= _tables[0].Count) return null;
            var name = _tables[0][entry].Trim();
            return name.Length == 0 ? null : name;
        }

        if (typeKey.Length != 3 || typeKey[0] < BaseSelector) return null;
        var selector = typeKey[0] - BaseSelector;
        var index = typeKey[1] | (typeKey[2] << 8);
        if (selector >= _tables.Count || index >= _tables[selector].Count) return null;
        var value = _tables[selector][index].Trim();
        return value.Length == 0 ? null : value;
    }
}

public static class Stalker2NameTableReader
{
    private const int MaximumEntries = 8192;
    private const int MaximumNameBytes = 4096;
    private const int MaximumTables = 16;
    private static readonly byte[] NameMarker =
    [
        0x0A, 0x00, (byte)'G', (byte)'u', (byte)'n', (byte)'A',
        (byte)'K', (byte)'7', (byte)'4', (byte)'_', (byte)'S', (byte)'T',
    ];
    // "Player" with its length: the first entry of the single table.
    private static readonly byte[] SingleTableStart = [0x06, 0x00, (byte)'P', (byte)'l', (byte)'a', (byte)'y', (byte)'e', (byte)'r'];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Stalker2NameTables? Locate(
        ReadOnlySpan<byte> raw,
        IEnumerable<byte[]>? typeKeys = null)
    {
        var keys = (typeKeys ?? Array.Empty<byte[]>())
            .Where(key => key is { Length: 3 })
            .Select(key => key.ToArray())
            .ToArray();
        var searchFrom = 0;
        while (searchFrom <= raw.Length - NameMarker.Length)
        {
            var relative = raw[searchFrom..].IndexOf(NameMarker);
            if (relative < 0) return null;
            var nameOffset = searchFrom + relative;
            var tableStart = nameOffset - sizeof(ushort);
            var names = ParseTable(raw, tableStart, out var offset);
            if (names is not null && names[0] == "GunAK74_ST")
            {
                var tables = new List<IReadOnlyList<string>> { names };
                while (tables.Count < MaximumTables)
                {
                    var following = ParseTable(raw, offset, out var next);
                    if (following is null) break;
                    tables.Add(following);
                    offset = next;
                }

                var found = new Stalker2NameTables(tables);
                if (keys.Length == 0 || keys.Any(key => found.Resolve(key) is not null)) return found;
            }

            searchFrom = nameOffset + 1;
        }

        return null;
    }

    /// <summary>
    /// Game 1.0.x keeps one table of every name at the very end of the save. It is recognised by its first entry and
    /// accepted only when it runs exactly to the last byte.
    /// </summary>
    public static Stalker2NameTables? LocateSingleTable(ReadOnlySpan<byte> raw)
    {
        var searchFrom = 0;
        while (searchFrom <= raw.Length - SingleTableStart.Length)
        {
            var relative = raw[searchFrom..].IndexOf(SingleTableStart);
            if (relative < 0) return null;
            var tableStart = searchFrom + relative - sizeof(ushort);
            var names = ParseTable(raw, tableStart, out var end, ushort.MaxValue);
            if (names is not null && end == raw.Length) return new Stalker2NameTables([names], singleTable: true);
            searchFrom += relative + 1;
        }

        return null;
    }

    /// <param name="end">Where the table ends (the parser already knows it; nothing is re-encoded to find out).</param>
    private static ReadOnlyCollection<string>? ParseTable(ReadOnlySpan<byte> raw, int start, out int end, int maximumEntries = MaximumEntries)
    {
        end = start;
        if (start < 0 || raw.Length - start < sizeof(ushort)) return null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(raw[start..]);
        if (count == 0 || count > maximumEntries) return null;

        var names = new string[count];
        var offset = start + sizeof(ushort);
        for (var index = 0; index < count; index++)
        {
            if (raw.Length - offset < sizeof(ushort)) return null;
            var byteCount = BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
            offset += sizeof(ushort);
            if (byteCount > MaximumNameBytes || raw.Length - offset < byteCount) return null;
            try
            {
                var value = StrictUtf8.GetString(raw.Slice(offset, byteCount));
                if (!IsPrintable(value)) return null;
                names[index] = value;
            }
            catch (DecoderFallbackException)
            {
                return null;
            }

            offset += byteCount;
        }

        end = offset;
        return Array.AsReadOnly(names);
    }

    private static bool IsPrintable(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is 0x09 or 0x20) continue;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or
                UnicodeCategory.Format or
                UnicodeCategory.Surrogate or
                UnicodeCategory.PrivateUse or
                UnicodeCategory.OtherNotAssigned or
                UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator or
                UnicodeCategory.SpaceSeparator)
            {
                return false;
            }
        }

        return true;
    }
}
