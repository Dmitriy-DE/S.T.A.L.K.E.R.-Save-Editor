using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayInfoPortionWriterTests
{
    [Theory]
    [InlineData("writer-factions/cop-source.sav")]
    [InlineData("writer-factions/cs-source.sav")]
    public void Adds_missing_actor_info_portions_and_keeps_the_rest_of_the_save(string fixture)
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var before = XRayTrilogyReader.FromBytes(source);

        var edit = XRayInfoPortionWriter.AddActorInfo(source, ["esc_wolf_dead"]);
        var after = XRayTrilogyReader.FromBytes(edit.Data.Span);

        Assert.Contains("esc_wolf_dead", after.ActorKnownInfo);
        Assert.Equal(before.ActorKnownInfo.Count + 1, after.ActorKnownInfo.Count);
        Assert.Equal(before.FactionRelations, after.FactionRelations);
        Assert.Equal(before.Money, after.Money);
        Assert.Throws<XRayFormatException>(() => XRayInfoPortionWriter.AddActorInfo(edit.Data.Span, ["esc_wolf_dead"]));
    }

    [Fact]
    public void Rejects_empty_or_non_ascii_names()
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-factions", "cop-source.sav"));

        Assert.Throws<XRayFormatException>(() => XRayInfoPortionWriter.AddActorInfo(source, []));
        Assert.Throws<XRayFormatException>(() => XRayInfoPortionWriter.AddActorInfo(source, ["флаг"]));
    }
}
