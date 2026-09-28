using System.Text.RegularExpressions;
using StalkerSaveEditor.Desktop.Services;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed partial class TranslationTests
{
    [Fact]
    public void Every_interface_language_loads_its_translations()
    {
        foreach (var code in I18nService.SupportedLanguages.Keys.Where(code => code != "ru"))
        {
            Assert.True(I18nService.Instance.GetCatalog(code).Count > 1000, $"{code} catalog is missing or empty");
        }

        Assert.Equal("INVENTORY", I18nService.Instance.TrIn("en", "ИНВЕНТАРЬ"));
    }

    [Fact]
    public void Every_interface_string_in_the_source_is_in_the_message_list()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        Assert.NotNull(root);
        var catalog = I18nService.Instance.GetCatalog("en");
        var missing = Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => InterfaceString().Matches(File.ReadAllText(file)).Select(match => Regex.Unescape(match.Groups[1].Value)))
            .Where(text => !catalog.ContainsKey(text))
            .Distinct()
            .ToArray();

        Assert.True(missing.Length == 0, "Not translated: " + string.Join(" | ", missing));
    }

    [GeneratedRegex("""L\.T\(\s*"((?:[^"\\]|\\.)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex InterfaceString();
}
