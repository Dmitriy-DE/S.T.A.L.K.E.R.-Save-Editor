using System.Text.Json;
using System.Text.Json.Nodes;
using StalkerSaveEditor.Core.Storage;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Storage;

public sealed class SteamVdfParserTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "steam-vdf");

    private static readonly string GoldenPath = Path.Combine(
        AppContext.BaseDirectory,
        "golden",
        "steam-vdf",
        "libraryfolders.json");

    [Fact]
    public void Parses_nested_libraryfolders_and_windows_paths_like_the_python_oracle()
    {
        using var golden = JsonDocument.Parse(File.ReadAllBytes(GoldenPath));
        var text = File.ReadAllText(Path.Combine(FixtureDirectory, "libraryfolders.vdf"));

        var parsed = SteamVdfParser.Parse(text);
        var actual = JsonSerializer.SerializeToNode(parsed);

        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(golden.RootElement.GetProperty("parsed").GetRawText()),
                actual));
        Assert.Equal(
            golden.RootElement.GetProperty("library_paths").EnumerateArray().Select(value => value.GetString()),
            SteamVdfParser.GetLibraryPaths(text));
    }

    [Fact]
    public void Rejects_an_unterminated_vdf_string_like_the_python_oracle()
    {
        var text = File.ReadAllText(Path.Combine(FixtureDirectory, "unterminated.vdf"));

        Assert.Throws<FormatException>(() => SteamVdfParser.Parse(text));
    }
}
