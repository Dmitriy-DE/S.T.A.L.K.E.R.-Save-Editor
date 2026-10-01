using System.Buffers.Binary;
using System.Text;
using StalkerSaveEditor.Core.Content;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Content;

/// <summary>Malformed external files must end in the parser's own error, not in a crash or invented data.</summary>
public sealed class MalformedInputTests
{
    [Fact]
    public void A_truncated_lzhuf_payload_is_rejected_instead_of_decoded_from_zero_padding()
    {
        var expected = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 400).Select(index => $"entry-{index:D4};")));
        var encoded = StalkerSaveEditor.Core.Tests.Formats.XRay.LzhufTestEncoder.Encode(expected);
        Assert.Equal(expected, XRayArchiveHeaderCodec.DecodeLzhuf(encoded));

        foreach (var keep in new[] { encoded.Length / 2, encoded.Length * 3 / 4, encoded.Length - 8 })
        {
            Assert.Throws<XRayFormatException>(() => XRayArchiveHeaderCodec.DecodeLzhuf(encoded.AsSpan(0, keep)));
        }
    }

    [Theory]
    [InlineData(0x80000000u)] // negative after the cast
    [InlineData(3u)]          // smaller than one row
    public void A_dds_with_an_impossible_pitch_is_invalid_data(uint pitch)
    {
        var dds = UncompressedDds(width: 4, height: 4, pitch, redMask: 0x00FF0000);
        Assert.Throws<InvalidDataException>(() => DdsImage.Decode(dds));
    }

    [Fact]
    public void A_dds_with_a_32_bit_channel_mask_is_invalid_data_not_a_division_by_zero()
    {
        Assert.Throws<InvalidDataException>(() => DdsImage.Decode(UncompressedDds(4, 4, pitch: 16, redMask: 0xFFFFFFFF)));
        Assert.Throws<InvalidDataException>(() => DdsImage.Decode(UncompressedDds(4, 4, pitch: 16, redMask: 0x00FF00FF)));
        Assert.Equal(4, DdsImage.Decode(UncompressedDds(4, 4, pitch: 16, redMask: 0x00FF0000)).Width);
    }

    [Fact]
    public void A_vdf_nested_too_deep_is_a_format_error()
    {
        var deep = string.Concat(Enumerable.Repeat("\"a\" { ", 5000)) + string.Concat(Enumerable.Repeat("} ", 5000));
        Assert.Throws<FormatException>(() => SteamVdfParser.Parse(deep));
        var fine = string.Concat(Enumerable.Repeat("\"a\" { ", 20)) + "\"k\" \"v\" " + string.Concat(Enumerable.Repeat("} ", 20));
        Assert.NotEmpty(SteamVdfParser.Parse(fine));
    }

    [Theory]
    [InlineData("[a]:b\nx = 1\n[b]:a\ny = 2\n")]
    [InlineData("[b]:a\ny = 2\n[a]:b\nx = 1\n")]
    public void An_ltx_inheritance_cycle_resolves_the_same_way_in_any_section_order(string text)
    {
        var resolved = LtxDocument.Resolve(LtxDocument.Parse(text, "test.ltx")).ToDictionary(pair => pair.Section.Name, pair => pair.Values);

        Assert.Equal("1", resolved["a"]["x"]);
        Assert.Equal("2", resolved["a"]["y"]);
        Assert.Equal("1", resolved["b"]["x"]);
        Assert.Equal("2", resolved["b"]["y"]);
    }

    private static byte[] UncompressedDds(int width, int height, uint pitch, uint redMask)
    {
        var data = new byte[128 + width * height * 4];
        "DDS "u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), pitch);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(88), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(92), redMask);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(96), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(100), 0x000000FF);
        return data;
    }
}
