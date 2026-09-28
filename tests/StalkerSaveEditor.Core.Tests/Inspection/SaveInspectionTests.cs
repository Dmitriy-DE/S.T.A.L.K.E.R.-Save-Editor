using System.Buffers.Binary;
using System.Text;
using StalkerSaveEditor.Core.Inspection;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Inspection;

public sealed class SaveInspectionTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("xray-call-of-pripyat.sav", "stalker-cop")]
    [InlineData("xray-clear-sky.sav", "stalker-cs")]
    public void Inspects_xray_saves_with_names_from_the_catalog(string fixture, string release)
    {
        var overview = SaveInspector.Inspect(File.ReadAllBytes(Fixture(fixture)));

        Assert.Equal(release, overview.ReleaseId);
        Assert.NotNull(overview.Money);
        Assert.True(overview.ItemCount >= overview.ItemTotals.Count);
        Assert.All(overview.ItemTotals.Values, total => Assert.True(total.Count > 0));
    }

    [Fact]
    public void Rejects_bytes_that_are_not_a_save()
    {
        Assert.Throws<InvalidDataException>(() => SaveInspector.Inspect("not a save at all"u8));
    }

    [Fact]
    public void Compares_money_actor_facts_and_item_totals_by_key()
    {
        var before = new SaveOverview("f", "stalker-cop", 100, 0.5f, 10, 5, 3, null, new Dictionary<string, SaveItemTotal>
        {
            ["medkit"] = new("medkit", "Аптечка", 2),
            ["bandage"] = new("bandage", "Бинт", 1),
        });
        var after = before with
        {
            Money = 250,
            Health = 1f,
            ItemTotals = new Dictionary<string, SaveItemTotal>
            {
                ["medkit"] = new("medkit", "Аптечка", 5),
                ["wpn_pm"] = new("wpn_pm", "ПМ", 1),
                ["bandage"] = new("bandage", "Бинт", 1),
            },
        };

        var rows = SaveComparer.Compare(before, after);

        Assert.Contains(new SaveDifference("money", "money", "100", "250"), rows);
        Assert.Contains(new SaveDifference("health", "health", "50%", "100%"), rows);
        Assert.Contains(new SaveDifference("item", "Аптечка", "2", "5"), rows);
        Assert.Contains(new SaveDifference("item", "ПМ", null, "1"), rows);
        Assert.DoesNotContain(rows, row => row.Label == "Бинт");
        Assert.Empty(SaveComparer.Compare(before, before));
    }

    [Fact]
    public void Parses_s2_campaign_index_and_stops_at_a_broken_record()
    {
        var raw = CampaignIndex(("A9596B8349C51AC827DC24BC380BA942", "sid_locations_region_yanov_name", "quest_a", 83_000f));

        var records = SavePreviewReader.ParseCampaigns(raw);

        var meta = Assert.Single(records).Value;
        Assert.Equal("yanov", meta.RegionSlug);
        Assert.Equal(83_000 / 3600.0, meta.PlayHours, 3);
        Assert.Equal(2026, meta.SavedAtUtc.Year);
        Assert.Empty(SavePreviewReader.ParseCampaigns(raw.AsSpan(0, 20)));
    }

    [Fact]
    public void Preview_is_null_when_the_game_left_none()
    {
        var directory = Path.Combine(Path.GetTempPath(), "se-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var save = Path.Combine(directory, "slot.sav");
            File.WriteAllBytes(save, [1, 2, 3]);
            Assert.Null(SavePreviewReader.Preview(save, stalker2: false));
            File.WriteAllBytes(Path.ChangeExtension(save, ".dds"), "broken"u8.ToArray());
            Assert.Null(SavePreviewReader.Preview(save, stalker2: false));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CampaignIndex(params (string Guid, string Region, string Quest, float Seconds)[] records)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[13]);
        stream.Write("Slot_02\0"u8);
        var strings = new List<string>();
        foreach (var (guid, region, quest, seconds) in records)
        {
            var fixedPart = new byte[40];
            for (var part = 0; part < 4; part++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(4 + part * 4), Convert.ToUInt32(guid.Substring(part * 8, 8), 16));
            }

            BinaryPrimitives.WriteInt64LittleEndian(fixedPart.AsSpan(28), new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc).Ticks);
            BinaryPrimitives.WriteSingleLittleEndian(fixedPart.AsSpan(36), seconds);
            stream.Write(fixedPart);
            foreach (var text in new[] { region, quest })
            {
                var buffer = new byte[4];
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)strings.Count);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)text.Length);
                strings.Add(text);
                stream.Write(buffer);
                stream.Write(Encoding.ASCII.GetBytes(text));
            }

            stream.Write(new byte[6]);
        }

        stream.Write(new byte[48]);
        return stream.ToArray();
    }
}
