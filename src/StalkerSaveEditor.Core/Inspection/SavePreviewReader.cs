using System.Buffers.Binary;
using System.Text;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Formats.Stalker2;

namespace StalkerSaveEditor.Core.Inspection;

/// <summary>Region, play time and save time of an S.T.A.L.K.E.R. 2 slot from <c>CampaignsSave.sav</c>.</summary>
public sealed record Stalker2SlotMeta(string SlotGuid, string RegionKey, string QuestKey, double PlayHours, DateTime SavedAtUtc)
{
    /// <summary><c>sid_locations_region_iron_forest_name</c> → <c>iron_forest</c>.</summary>
    public string RegionSlug
    {
        get
        {
            var value = RegionKey;
            if (value.StartsWith("sid_locations_region_", StringComparison.Ordinal)) value = value["sid_locations_region_".Length..];
            if (value.EndsWith("_name", StringComparison.Ordinal)) value = value[..^"_name".Length];
            return value;
        }
    }
}

/// <summary>
/// The game's own preview of a save slot (port of the Python oracle's <c>save_thumbnails</c> and
/// <c>s2_campaigns</c>): X-Ray writes <c>&lt;save&gt;.dds</c> beside every save; S2 keeps a JPEG per
/// slot in <c>SaveGames/Thumbnails/&lt;GUID&gt;.sav</c>. Read-only and optional: null when absent.
/// </summary>
public static class SavePreviewReader
{
    private const int MaxDdsBytes = 8 * 1024 * 1024;
    private const int MaxThumbnailBytes = 4 * 1024 * 1024;
    private const int MaxCampaignBytes = 8 * 1024 * 1024;
    private const int RecordFixed = 40;

    /// <summary>PNG for X-Ray saves, JPEG for S2 saves; null when the game left no preview.</summary>
    public static byte[]? Preview(string savePath, bool stalker2)
    {
        try
        {
            if (stalker2)
            {
                var thumbnail = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(savePath)!)!, "Thumbnails", SlotGuid(savePath) + ".sav");
                if (!File.Exists(thumbnail) || new FileInfo(thumbnail).Length > MaxThumbnailBytes) return null;
                var raw = Stalker2SaveReader.Unpack(File.ReadAllBytes(thumbnail));
                var start = raw.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);
                return start is >= 0 and < 64 ? raw[start..] : null;
            }

            var dds = Path.ChangeExtension(savePath, ".dds");
            if (!File.Exists(dds) || new FileInfo(dds).Length > MaxDdsBytes) return null;
            return DdsImage.Decode(File.ReadAllBytes(dds)).ToPng();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
                                              Stalker2FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Metadata for an S2 <c>SaveGames/Data/&lt;GUID&gt;.sav</c> slot, or null.</summary>
    public static Stalker2SlotMeta? Stalker2Slot(string savePath)
    {
        var index = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(savePath)!)!, "CampaignsSave.sav");
        try
        {
            if (!File.Exists(index) || new FileInfo(index).Length > MaxCampaignBytes) return null;
            return ParseCampaigns(Stalker2SaveReader.Unpack(File.ReadAllBytes(index))).GetValueOrDefault(SlotGuid(savePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or Stalker2FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Campaign records: u32 id, 16-byte GUID (four LE u32), u32 kind, u32 build, i64 ticks, f32 play seconds,
    /// str region, str quest, 6 bytes. <c>str</c> is a deduplicated u16 index; a new index is followed by a u16
    /// length and ASCII text. Parsing stops at the first record that does not fit.
    /// </summary>
    internal static Dictionary<string, Stalker2SlotMeta> ParseCampaigns(ReadOnlySpan<byte> raw)
    {
        var result = new Dictionary<string, Stalker2SlotMeta>(StringComparer.Ordinal);
        var end = raw.IndexOf("Achievements"u8);
        if (end < 0) end = raw.Length;
        var nameEnd = raw.Length > 13 ? raw[13..Math.Min(raw.Length, 128)].IndexOf((byte)0) : -1;
        if (nameEnd < 0) return result;
        var position = 13 + nameEnd + 1;
        var strings = new List<string>();

        bool ReadString(ReadOnlySpan<byte> data, ref int offset, out string text)
        {
            text = string.Empty;
            if (offset + 2 > data.Length) return false;
            var index = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            offset += 2;
            if (index == strings.Count)
            {
                if (offset + 2 > data.Length) return false;
                var length = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
                offset += 2;
                if (offset + length > data.Length) return false;
                var bytes = data.Slice(offset, length);
                foreach (var value in bytes)
                {
                    if (value > 0x7F) return false;
                }

                strings.Add(Encoding.ASCII.GetString(bytes));
                offset += length;
            }
            else if (index > strings.Count)
            {
                return false;
            }

            text = strings[index];
            return true;
        }

        while (position + RecordFixed < end)
        {
            var guidBuilder = new StringBuilder(32);
            for (var part = 0; part < 4; part++)
            {
                guidBuilder.Append(BinaryPrimitives.ReadUInt32LittleEndian(raw[(position + 4 + part * 4)..])
                    .ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            }

            var guid = guidBuilder.ToString();
            var ticks = BinaryPrimitives.ReadInt64LittleEndian(raw[(position + 28)..]);
            var seconds = BinaryPrimitives.ReadSingleLittleEndian(raw[(position + 36)..]);
            var offset = position + RecordFixed;
            if (!ReadString(raw, ref offset, out var region) || !ReadString(raw, ref offset, out var quest)) break;
            if (ticks <= 0 || ticks >= DateTime.MaxValue.Ticks || seconds is < 0 or >= 1e8f || float.IsNaN(seconds)) break;
            result[guid] = new Stalker2SlotMeta(guid, region, quest, seconds / 3600.0, new DateTime(ticks, DateTimeKind.Utc));
            position = offset + 6;
        }

        return result;
    }

    /// <summary>The GUID part of a slot name; copies such as <c>&lt;GUID&gt;-edited</c> share it.</summary>
    private static string SlotGuid(string savePath)
    {
        var stem = Path.GetFileNameWithoutExtension(savePath);
        return (stem.Length > 32 ? stem[..32] : stem).ToUpperInvariant();
    }
}
