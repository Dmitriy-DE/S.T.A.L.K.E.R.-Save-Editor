using System.Text;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Formats.XRay;

public sealed class XRayProgressReaderTests
{
    private sealed class Bytes
    {
        private readonly List<byte> _data = [];
        public Bytes U8(byte v) { _data.Add(v); return this; }
        public Bytes U16(ushort v) { _data.AddRange(BitConverter.GetBytes(v)); return this; }
        public Bytes U32(uint v) { _data.AddRange(BitConverter.GetBytes(v)); return this; }
        public Bytes I32(int v) { _data.AddRange(BitConverter.GetBytes(v)); return this; }
        public Bytes U64(ulong v) { _data.AddRange(BitConverter.GetBytes(v)); return this; }
        public Bytes Str(string v) { _data.AddRange(Encoding.ASCII.GetBytes(v)); _data.Add(0); return this; }
        public Bytes Raw(int n) { _data.AddRange(new byte[n]); return this; }
        public byte[] ToArray() => [.. _data];
    }

    private static Bytes SocObjective(Bytes b, int index, int state, string description) =>
        b.I32(index).I32(state).Str(description).Str("").U16(ushort.MaxValue).I32(state).U8(1).Str("").Str("ui\\ui_iconsTotal")
            .Raw(16).Str("").U32(1).Str("some_info").U32(0).U32(0).U32(0).U8(0);

    private static Bytes Statistics(Bytes b) =>
        b.U32(1).U16(0).U32(2)
            .U32(2).Str("bandit_novice").I32(40).I32(40).Str("").Str("military_novice").I32(5).I32(10).Str("").Str("stalkerkills")
            .U32(1).Str("dog_weak").I32(7).I32(7).Str("").Str("monsterkills");

    [Fact]
    public void Reads_shadow_of_chernobyl_tasks_objectives_and_statistics()
    {
        var b = new Bytes().Raw(40).U32(1).U16(0).U32(2);
        b.Str("agr_help_krot").U64(1000).U64(2000).U64(0).Str("").U32(2);
        SocObjective(b, 0, 2, "agr_help_krot_0");
        SocObjective(b, 1, 2, "agr_help_krot_1");
        b.Str("esc_kill_bandits").U64(3000).U64(0).U64(0).Str("").U32(1);
        SocObjective(b, 0, 1, "esc_kill_bandits_0");
        b.U16(ushort.MaxValue).U32(0).Str("trailer").U8(1).U8(0);
        var data = Statistics(b).ToArray();

        var progress = XRayProgressReader.Parse(data, soc: true, actorId: 0, from: 0);

        Assert.NotNull(progress);
        Assert.Equal(["agr_help_krot", "esc_kill_bandits"], progress.Tasks.Select(t => t.Id));
        Assert.Equal(XRayTaskState.Completed, progress.Tasks[0].State);
        Assert.Equal(XRayTaskState.InProgress, progress.Tasks[1].State);
        Assert.Equal(2, progress.Tasks[0].Objectives.Count);
        Assert.Equal(2000UL, progress.Tasks[0].FinishTime);
        Assert.Equal(45, progress.Count("stalkerkills"));
        Assert.Equal(7, progress.Count("monsterkills"));
    }

    [Fact]
    public void Reads_clear_sky_style_tasks_and_call_of_pripyat_without_statistics()
    {
        var b = new Bytes().Raw(20).U32(1).U16(0).U32(2);
        foreach (var (id, state) in new[] { ("mar_quest_scout", 2), ("mar_find_doctor", 0) })
        {
            b.Str(id).I32(state).I32(0).U64(10).U64(20).U64(0).U64(0).Str(id + "_name").Str(id + "_text")
                .U32(0).U32(0).U32(0).U32(0).Str("ui_icon").Str("").Str("green_location").U16(12).U32(3);
        }
        var tasksOnly = b.ToArray();

        var cop = XRayProgressReader.Parse(tasksOnly, soc: false, actorId: 0, from: 0);
        Assert.NotNull(cop);
        Assert.Equal([XRayTaskState.Completed, XRayTaskState.Failed], cop.Tasks.Select(t => t.State));
        Assert.Empty(cop.Statistics);
        Assert.True(cop.TasksKnown);

        // Statistics alone: the task registry was not found, so "no tasks" is unknown rather than a fact.
        var statisticsOnly = XRayProgressReader.Parse(Statistics(new Bytes()).ToArray(), soc: false, actorId: 0, from: 0);
        Assert.NotNull(statisticsOnly);
        Assert.Empty(statisticsOnly.Tasks);
        Assert.False(statisticsOnly.TasksKnown);

        var cs = XRayProgressReader.Parse(Statistics(b).ToArray(), soc: false, actorId: 0, from: 0);
        Assert.NotNull(cs);
        Assert.Equal(2, cs.Tasks.Count);
        Assert.Equal("mar_quest_scout_name", cs.Tasks[0].Title);
        Assert.Equal(40, cs.Statistics.Single(l => l.Key == "bandit_novice").Count);
    }

    [Fact]
    public void Reads_enhanced_edition_tasks_with_a_trailing_byte_and_trailer()
    {
        var b = new Bytes().Raw(20).U32(1).U16(0).U32(1);
        b.Str("zat_a2_reach_base").I32(1).I32(0).U64(10).U64(0).U64(0).U64(0).Str("name").Str("text")
            .U32(0).U32(0).U32(0).U32(0).Str("ui_icon").Str("").Str("storyline_task_location").U16(7056).U32(1).U8(0);
        b.Str("zat_a2_reach_base").U32(0);

        var progress = XRayProgressReader.Parse(b.ToArray(), soc: false, actorId: 0, from: 0);

        Assert.NotNull(progress);
        Assert.Equal(XRayTaskState.InProgress, Assert.Single(progress.Tasks).State);
    }

    [Fact]
    public void Truncated_or_foreign_data_reads_as_nothing()
    {
        var data = Statistics(new Bytes().Raw(30)).ToArray();

        Assert.Null(XRayProgressReader.Parse(data.AsSpan(0, data.Length - 3), soc: true, actorId: 0, from: 0));
        Assert.Null(XRayProgressReader.Parse(new byte[200], soc: false, actorId: 0, from: 0));
        Assert.Null(XRayProgressReader.Parse(data, soc: true, actorId: 5, from: 0));
    }
}

public sealed class XRayWeatherReaderTests
{
    [Theory]
    [InlineData("\u0001weather_state\0\u0001dynamic_default=clear,cloudy\0", "clear", "cloudy")]
    [InlineData("Fdynamic_default=thunder,rain\0tail", "thunder", "rain")]
    public void Reads_current_and_next_weather(string text, string current, string next)
    {
        var weather = XRayWeatherReader.Find(text);
        Assert.NotNull(weather);
        Assert.Equal("dynamic_default", weather.Graph);
        Assert.Equal((current, next), (weather.Current, weather.Next));
    }

    [Theory]
    [InlineData("dynamic_default=clear\0")]
    [InlineData("mil_lager=agroprom,military\0")]
    [InlineData("")]
    public void Anything_else_is_not_weather(string text) => Assert.Null(XRayWeatherReader.Find(text));

    [Fact]
    public void Whole_save_fallback_needs_one_unambiguous_dynamic_value()
    {
        Assert.Equal(new XRayWeather("dynamic_default", "rain", "cloudy"), XRayWeatherReader.FindUnique("x\0dynamic_default=rain,cloudy\0y\0dynamic_default=rain,cloudy\0"));
        Assert.Null(XRayWeatherReader.FindUnique("dynamic_default=rain,cloudy\0dynamic_default=clear,clear\0"));
        Assert.Null(XRayWeatherReader.FindUnique("other_graph=rain,cloudy\0"));
    }
}
