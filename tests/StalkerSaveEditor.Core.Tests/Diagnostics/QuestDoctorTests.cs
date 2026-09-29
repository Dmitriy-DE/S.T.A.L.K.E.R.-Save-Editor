using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class QuestDoctorTests
{
    [Theory]
    [InlineData("xray-clear-sky.sav", "stalker-cs")]
    [InlineData("writer-s2-money/s2-money-source.sav", "stalker2")]
    public void Reports_that_quest_states_are_unavailable_when_the_reader_exposes_no_proven_task_fields(string fixture, string format)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture.Replace('/', Path.DirectorySeparatorChar));

        var report = QuestDoctor.Analyze(File.ReadAllBytes(path));

        Assert.Equal(SaveDoctorStatus.Unknown, report.Status);
        Assert.Equal(format, report.FormatId);
        Assert.False(report.QuestStatesAvailable);
        Assert.Empty(report.States);
        Assert.Contains("does not expose", report.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
