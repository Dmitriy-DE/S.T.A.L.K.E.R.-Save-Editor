using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class SaveDoctorTests
{
    [Theory]
    [InlineData("xray-clear-sky.sav", "stalker-cs")]
    [InlineData("writer-s2-money/s2-money-source.sav", "stalker2")]
    public void Reports_supported_structure_but_does_not_claim_semantic_health_or_repair(string fixture, string format)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture.Replace('/', Path.DirectorySeparatorChar));
        var report = SaveDoctor.Analyze(File.ReadAllBytes(path));

        Assert.Equal(SaveDoctorStatus.Ok, report.Status);
        Assert.Equal(format, report.Overview?.FormatId);
        Assert.Contains(report.Checks, check => check.Id == "structure" && check.Status == SaveDoctorStatus.Ok);
        Assert.Contains(report.Checks, check => check.Id == "semantic-state" && check.Status == SaveDoctorStatus.Unknown);
        Assert.Contains(report.Checks, check => check.Id == "repair" && check.Status == SaveDoctorStatus.Unknown);
    }

    [Fact]
    public void Unsupported_or_damaged_input_is_reported_without_a_repair()
    {
        var report = SaveDoctor.Analyze("not a S.T.A.L.K.E.R. save"u8);

        Assert.Equal(SaveDoctorStatus.Error, report.Status);
        Assert.Null(report.Overview);
        Assert.Contains(report.Checks, check => check.Id == "structure" && check.Status == SaveDoctorStatus.Error);
        Assert.Contains(report.Checks, check => check.Id == "repair" && check.Status == SaveDoctorStatus.Unknown);
    }
}
