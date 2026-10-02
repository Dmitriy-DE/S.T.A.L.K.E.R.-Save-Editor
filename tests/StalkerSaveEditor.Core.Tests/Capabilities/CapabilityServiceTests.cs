using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StalkerSaveEditor.Core.Capabilities;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Capabilities;

public sealed class CapabilityServiceTests
{
    [Fact]
    public void Every_parity_row_names_a_capability()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "PARITY.csv");
        var records = File.ReadAllLines(path).Select(ParseCsvRow).ToArray();
        var headers = records[0];
        var capabilityIndex = Array.IndexOf(headers, "capability");
        var formatIndex = Array.IndexOf(headers, "format");
        var operationIndex = Array.IndexOf(headers, "operation");

        Assert.True(capabilityIndex >= 0, "PARITY.csv must declare a capability column.");
        Assert.True(formatIndex >= 0, "PARITY.csv must declare a format column.");
        Assert.True(operationIndex >= 0, "PARITY.csv must declare an operation column.");
        Assert.True(records.Length > 1, "PARITY.csv must contain operation rows.");

        for (var index = 1; index < records.Length; index++)
        {
            var fields = records[index];
            Assert.Equal(headers.Length, fields.Length);
            var formatId = fields[formatIndex];
            var operation = fields[operationIndex];
            var capability = fields[capabilityIndex];
            Assert.False(string.IsNullOrWhiteSpace(capability), $"PARITY.csv row {index + 1} has no capability.");
            Assert.True(
                CapabilityService.Default.HasParityCapability(formatId, capability),
                $"PARITY.csv row {index + 1} ({formatId}/{operation}) refers to unknown capability '{capability}'.");
        }
    }

    [Fact]
    public void Registered_capabilities_match_the_python_formats_snapshot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "capabilities", "capability-registry.json");
        var expected = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var actual = JsonSerializer.SerializeToNode(
            CapabilityService.Default.Formats.Select(format => format.AsDictionary()));

        Assert.Equal(expected["oracle_revision"]!.GetValue<string>(), CapabilityService.Default.OracleRevision);
        Assert.True(
            JsonNode.DeepEquals(expected["formats"], actual),
            "Core capability metadata differs from the Python snapshot.");
    }

    [Fact]
    public void Format_registry_and_capability_views_are_read_only()
    {
        var formats = CapabilityService.Default.Formats;
        var soc = CapabilityService.Default.GetFormat("stalker-soc");

        Assert.Throws<NotSupportedException>(() =>
            ((IList<FormatCapabilityDescriptor>)formats)[0] = soc);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, CapabilitySupport>)soc.Capabilities.MutationSupport)["edit_money"] =
                new CapabilitySupport(CapabilityMaturity.Unsupported));
    }

    [Fact]
    public void Unknown_formats_and_capabilities_are_rejected()
    {
        Assert.Throws<KeyNotFoundException>(() => CapabilityService.Default.GetFormat("unknown-release"));
        Assert.Throws<KeyNotFoundException>(() => CapabilityService.Default.Get("stalker-soc", "unknown-capability"));
        Assert.False(CapabilityService.Default.HasParityCapability("unknown-release", "read_inventory"));
    }

    [Theory]
    [InlineData("equipment.add")]
    [InlineData("equipment.durability")]
    [InlineData("equipment.placement")]
    [InlineData("equipment.remove")]
    [InlineData("equipment.upgrades")]
    public void Equipment_operations_are_available_through_the_capability_service(string capability)
    {
        foreach (var format in CapabilityService.Default.Formats)
        {
            if (format.Capabilities.Equipment is null)
            {
                Assert.False(CapabilityService.Default.HasParityCapability(format.Id, capability));
                continue;
            }

            Assert.True(CapabilityService.Default.HasParityCapability(format.Id, capability));
            Assert.Equal(
                format.Capabilities.Equipment.Support(capability),
                CapabilityService.Default.Get(format.ReleaseId, capability));
        }
    }

    private static string[] ParseCsvRow(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        fields.Add(field.ToString());
        return fields.ToArray();
    }
}
