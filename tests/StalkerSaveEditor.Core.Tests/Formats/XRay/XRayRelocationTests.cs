using System.Text;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayRelocationTests
{
    private static byte[] Destination(ushort gv, uint lv, string level, string point, byte[] tail, bool shapes = true)
    {
        var data = new List<byte>();
        data.AddRange(new byte[36]);                    // object and restrictor data before the shapes (varies)
        if (shapes)
        {
            data.AddRange([1, 1]);                      // one shape, a box
            foreach (var f in new[] { 2.2f, 0, 0, 0, 4.8f, 0, 0, 0, 2.2f, 1, 2, 3 }) data.AddRange(BitConverter.GetBytes(f));
            data.Add(3);                                // restrictor type
        }

        data.AddRange(BitConverter.GetBytes(gv));
        data.AddRange(BitConverter.GetBytes(lv));
        foreach (var f in new[] { -271.1f, -21.7f, -276.6f, 0f, -2.36f, 0f }) data.AddRange(BitConverter.GetBytes(f));
        data.AddRange(Encoding.ASCII.GetBytes(level + "\0" + point + "\0"));
        data.AddRange(tail);
        return [.. data];
    }

    [Fact]
    public void Finds_the_clear_sky_destination_with_its_hint_and_trailer()
    {
        // Layout copied from a real Clear Sky save: silent flag, hint string, trailing u16.
        var tail = new List<byte> { 0 };
        tail.AddRange(new byte[] { 1 });
        tail.AddRange(Encoding.ASCII.GetBytes("level_changer_invitation\0"));
        tail.AddRange(new byte[] { 0x1A, 0x00 });
        var state = Destination(473, 3366, "escape", "esc_graph_point_actor_spawn_from_marsh_1", [.. tail]);

        var found = XRayRelocation.FindDestination(state, 124);

        Assert.NotNull(found);
        Assert.Equal("escape", found.DestLevelName);
        Assert.Equal((ushort)473, found.DestGameVertexId);
        Assert.Equal(3366u, found.DestLevelVertexId);
        Assert.Equal(new XRayVector3(-271.1f, -21.7f, -276.6f), found.DestPosition);
    }

    [Fact]
    public void Finds_the_shadow_of_chernobyl_destination_at_the_end_of_the_state()
    {
        var state = Destination(719, 12, "l03u_agr_underground", "start_actor_02", []);

        var found = XRayRelocation.FindDestination(state, 102);

        Assert.Equal("start_actor_02", found?.DestLevelPointName);
    }

    [Fact]
    public void A_capitalised_level_name_is_read_whole()
    {
        // Shadow of Chernobyl spells levels "L02_Garbage". The reader used to reject the capital letter at the real
        // place and accept the first later offset that parsed, reporting "arbage" with numbers read from the wrong bytes.
        var state = Destination(253, 209876, "L02_Garbage", "start_actor_01", [0]);

        var found = XRayRelocation.FindDestination(state, 118);

        Assert.Equal("L02_Garbage", found?.DestLevelName);
        Assert.Equal((ushort)253, found?.DestGameVertexId);
        Assert.Equal(209876u, found?.DestLevelVertexId);
    }

    [Fact]
    public void A_block_that_no_shape_list_leads_to_is_not_a_destination()
    {
        var state = Destination(473, 3366, "escape", "point", [0], shapes: false);

        Assert.Null(XRayRelocation.FindDestination(state, 124));
    }

    [Fact]
    public void Garbage_after_the_block_is_not_a_destination()
    {
        var state = Destination(1, 2, "escape", "point", [0xFF, 0x10, 0x20, 0x30, 0x40]);

        Assert.Null(XRayRelocation.FindDestination(state, 124));
    }

    [Fact]
    public void A_save_without_level_changers_offers_no_destination_and_refuses_one()
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xray-soc.sav"));
        var save = XRayTrilogyReader.FromBytes(source);

        Assert.Empty(XRayRelocation.ReadAnchors(save));
        var fake = new XRayRelocationAnchor("x", "escape", "p", 1, 2, new XRayVector3(1, 2, 3), new XRayVector3(0, 0, 0));
        Assert.Throws<XRayFormatException>(() => XRayRelocation.Prepare(source, fake));
    }
}
