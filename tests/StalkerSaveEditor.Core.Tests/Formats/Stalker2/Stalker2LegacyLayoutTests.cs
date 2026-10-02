using System.Buffers.Binary;
using System.Text;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.Stalker2;

/// <summary>
/// The inventory block as game 1.0.x wrote it (December 2024). The bytes are built here field by field in the shape
/// found in real saves of that version; no real save is stored in the repository.
/// </summary>
public sealed class Stalker2LegacyLayoutTests
{
    private static readonly byte[] ContainerId = [0xCA, 0xCF, 0xA8, 0x48, 0xC8, 0x95, 0x21, 0x49, 0xB5, 0x1B, 0x94, 0x44];
    private static readonly string[] Names = ["Player", "", "Bandage", "ArmyMedkit", "A939A"];

    [Fact]
    public void Reads_money_grid_items_and_names_of_a_save_written_by_game_1_0()
    {
        var save = Stalker2SaveReader.FromBytes(Pack(BuildRaw(money: 85433, headerNumber: 23)));

        Assert.True(save.IsLegacy);
        Assert.True(Stalker2SaveReader.Detect(save.Original.Span));
        Assert.Equal(85433u, save.Money);
        Assert.Equal(
            [("Bandage", 10u, 0, 0), ("ArmyMedkit", 31u, 1, 0), ("A939A", 468u, 0, 1), ("Bandage", 2u, 1, 1)],
            save.Inventory.Select(item => (item.DisplayName!, item.Count, (int)item.X!, (int)item.Y!)).ToArray());
        Assert.All(save.Inventory, item => Assert.False(item.EditableCount));
        Assert.Empty(save.UnresolvedHandles);
    }

    [Fact]
    public void No_writer_accepts_a_save_in_the_old_layout()
    {
        var source = Pack(BuildRaw(money: 100, headerNumber: 1));

        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)).ToLowerInvariant();

        Assert.Throws<NotSupportedException>(() => EditService.PrepareEdit(source, new EditPlan(sha256, 200)));
        Assert.Throws<NotSupportedException>(() => Stalker2MoneyWriter.Prepare(source, new EditPlan(sha256, 200)));
    }

    [Theory]
    [InlineData(0u, 1)]      // the header number must not be zero
    [InlineData(5u, 6)]      // and must be the same in both of its fields
    public void A_wallet_header_of_another_shape_is_not_taken_for_the_old_layout(uint number, ushort repeated)
    {
        var raw = BuildRaw(money: 100, headerNumber: number, repeatedNumber: repeated);

        Assert.False(Stalker2SaveReader.Detect(Pack(raw)));
        Assert.Throws<Stalker2FormatException>(() => Stalker2SaveReader.FromBytes(Pack(raw)));
    }

    [Fact]
    public void Two_container_ids_are_ambiguous_and_refused()
    {
        var raw = BuildRaw(money: 100, headerNumber: 1);
        var doubled = raw.Concat(ContainerId).ToArray();

        Assert.False(Stalker2SaveReader.Detect(Pack(doubled)));
    }

    private static byte[] Pack(byte[] raw) => Stalker2ContainerWriter.Build(raw, KrakenCodec.Compress(raw));

    private static byte[] BuildRaw(uint money, uint headerNumber, ushort? repeatedNumber = null)
    {
        var raw = new List<byte>();
        void U16(int value) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)value); raw.AddRange(b); }
        void U32(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); raw.AddRange(b); }

        // Object records: handle, id, name index (2 bytes), x, y, three zero bytes, 0x38, count, a byte, weight, three bytes, kind.
        var items = new (uint Handle, int Name, byte X, byte Y, uint Count, float Weight, byte Kind)[]
        {
            (0x30000001, 2, 0, 0, 10, 0.5f, 5),
            (0x30000002, 3, 1, 0, 31, 3.1f, 5),
            (0x30000003, 4, 0, 1, 468, 4.68f, 4),
            (0x30000004, 2, 1, 1, 2, 0.1f, 5),
        };
        raw.AddRange(new byte[64]);
        foreach (var item in items)
        {
            U32(item.Handle);
            U32(0x46000000 + item.Handle);
            U16(item.Name);
            raw.Add(item.X);
            raw.Add(item.Y);
            raw.AddRange(new byte[3]);
            raw.Add(0x38);
            U32(item.Count);
            raw.Add(0);
            U32(BitConverter.SingleToUInt32Bits(item.Weight));
            raw.AddRange([0x53, 0x01, 0x01]);
            raw.Add(item.Kind);
            raw.AddRange(new byte[24]);
        }

        // The container: its id, the sub-container list, the ten-byte header, the wallet, the handles, the grid.
        raw.AddRange(ContainerId);
        U16(2);
        U32(0x380003DF); U32(1);
        U32(0x38002650); U32(1);
        U32(0);
        U32(headerNumber);
        U16(repeatedNumber ?? (ushort)headerNumber);
        U32(money);
        U32(1);
        U16(items.Length + 2);
        foreach (var item in items) U32(item.Handle);
        U32(uint.MaxValue);
        U32(uint.MaxValue);
        U16(items.Length);
        foreach (var item in items)
        {
            U32(item.Handle);
            raw.Add(item.X);
            raw.Add(item.Y);
        }

        raw.AddRange(new byte[32]);

        // One table of every name, running to the last byte.
        U16(Names.Length);
        foreach (var name in Names)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            U16(bytes.Length);
            raw.AddRange(bytes);
        }

        return raw.ToArray();
    }
}
