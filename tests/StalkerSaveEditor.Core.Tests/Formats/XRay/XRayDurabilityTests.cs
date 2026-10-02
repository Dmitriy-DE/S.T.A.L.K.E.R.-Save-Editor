using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayDurabilityTests
{
    [Theory]
    [InlineData("soc", false)]
    [InlineData("cs", false)]
    [InlineData("cop", false)]
    [InlineData("soc-ee", true)]
    [InlineData("cs-ee", true)]
    [InlineData("cop-ee", true)]
    public void Reads_python_durability_vectors(string release, bool enhanced)
    {
        var suffix = $"xray-durability-{release}-source.sav";
        var parsed = enhanced
            ? XRayEnhancedReader.FromBytes(ReadFixture(suffix))
            : XRayTrilogyReader.FromBytes(ReadFixture(suffix));

        var item = Assert.Single(parsed.Inventory, item => item.Handle == 0x3456);
        Assert.Equal(0.25f, item.Condition);
        Assert.True(item.ConditionEditable);
    }

    [Fact]
    public void Edit_plan_accepts_durability_targets()
    {
        var parameter = typeof(EditPlan).GetConstructors().Single()
            .GetParameters()
            .SingleOrDefault(parameter => parameter.Name == "durability");

        Assert.NotNull(parameter);
        Assert.Equal(typeof(IReadOnlyDictionary<uint, double>), parameter.ParameterType);
    }

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "writer-durability",
        name));
}
