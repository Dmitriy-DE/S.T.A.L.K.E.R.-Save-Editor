using System.Collections.ObjectModel;
using System.Text.Json;

namespace StalkerSaveEditor.Core.Capabilities;

public interface ICapabilityService
{
    IReadOnlyList<FormatCapabilityDescriptor> Formats { get; }

    FormatCapabilityDescriptor GetFormat(string releaseId);

#pragma warning disable CA1716 // Keep the existing public capability API stable across package consumers.
    CapabilitySupport Get(string releaseId, string capability);
#pragma warning restore CA1716

    bool HasParityCapability(string formatId, string capability);
}

public sealed class CapabilityService : ICapabilityService
{
    private const string SnapshotResourceName =
        "StalkerSaveEditor.Core.Capabilities.Data.capability-snapshot.json";

    private static readonly HashSet<string> SharedParityCapabilities = new(StringComparer.Ordinal)
    {
        "add_items",
        "capability_registry",
        "catalog",
        "codec",
        "container",
        "drafts",
        "edit_durability",
        "edit_money",
        "edit_player_faction",
        "edit_placement",
        "edit_relations",
        "edit_stacks",
        "edit_upgrades",
        "format_detection",
        "move_items",
        "read_inventory",
        "remove_items",
        "save_discovery",
        "steam_cloud_read",
        "steam_cloud_write",
        "steam_library",
        "steam_native",
        "storage",
    };

    private static readonly HashSet<string> FormatInfrastructureCapabilities = new(StringComparer.Ordinal)
    {
        "codec",
        "container",
        "format_detection",
    };

    private readonly ReadOnlyDictionary<string, FormatCapabilityDescriptor> _formatsById;

    private CapabilityService(string oracleRevision, IReadOnlyList<FormatCapabilityDescriptor> formats)
    {
        OracleRevision = oracleRevision;
        Formats = formats;
        _formatsById = new ReadOnlyDictionary<string, FormatCapabilityDescriptor>(
            formats.ToDictionary(format => format.Id, StringComparer.Ordinal));
    }

    public static CapabilityService Default { get; } = LoadEmbedded();

    public string OracleRevision { get; }

    public IReadOnlyList<FormatCapabilityDescriptor> Formats { get; }

    public FormatCapabilityDescriptor GetFormat(string releaseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseId);
        return _formatsById.TryGetValue(releaseId, out var format)
            ? format
            : throw new KeyNotFoundException($"Unknown release capability registry: {releaseId}");
    }

    public CapabilitySupport Get(string releaseId, string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        return GetFormat(releaseId).Capabilities.Support(capability);
    }

    public bool HasParityCapability(string formatId, string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        if (string.Equals(formatId, "shared", StringComparison.Ordinal))
        {
            return SharedParityCapabilities.Contains(capability);
        }

        if (!_formatsById.TryGetValue(formatId, out var format))
        {
            return false;
        }

        return FormatInfrastructureCapabilities.Contains(capability)
            || format.Capabilities.HasCapability(capability);
    }

    private static CapabilityService LoadEmbedded()
    {
        using var stream = typeof(CapabilityService).Assembly
            .GetManifestResourceStream(SnapshotResourceName)
            ?? throw new InvalidOperationException($"Embedded capability snapshot is missing: {SnapshotResourceName}");
        using var document = JsonDocument.Parse(stream);
        var revision = document.RootElement.GetProperty("oracle_revision").GetString();
        if (revision is null || revision.Length != 40)
        {
            throw new InvalidDataException("Embedded capability snapshot has an invalid oracle revision.");
        }

        var formats = new List<FormatCapabilityDescriptor>();

        foreach (var element in document.RootElement.GetProperty("formats").EnumerateArray())
        {
            formats.Add(ParseFormat(element));
        }

        if (formats.Count == 0)
        {
            throw new InvalidDataException("Embedded capability snapshot contains no formats.");
        }

        if (formats.Select(format => format.Id).Distinct(StringComparer.Ordinal).Count() != formats.Count)
        {
            throw new InvalidDataException("Embedded capability snapshot contains duplicate format ids.");
        }

        return new CapabilityService(revision, Array.AsReadOnly(formats.ToArray()));
    }

    private static FormatCapabilityDescriptor ParseFormat(JsonElement element)
    {
        var profile = element.GetProperty("capabilities");
        var mutationSupport = new Dictionary<string, CapabilitySupport>(StringComparer.Ordinal);
        foreach (var capability in profile.GetProperty("mutation_support").EnumerateObject())
        {
            mutationSupport.Add(capability.Name, ParseSupport(capability.Value));
        }

        var equipmentElement = profile.GetProperty("equipment");
        var equipment = equipmentElement.ValueKind is JsonValueKind.Null
            ? null
            : ParseEquipment(equipmentElement);

        return new FormatCapabilityDescriptor(
            element.GetProperty("id").GetString() ?? throw new InvalidDataException("Format id is null."),
            element.GetProperty("release_id").GetString() ?? throw new InvalidDataException("Release id is null."),
            element.GetProperty("edition").GetString() ?? throw new InvalidDataException("Edition is null."),
            new FormatCapabilityProfile(
                profile.GetProperty("read_inventory").GetBoolean(),
                profile.GetProperty("catalog").GetBoolean(),
                equipment,
                mutationSupport));
    }

    private static EquipmentCapabilityProfile ParseEquipment(JsonElement element) => new(
        ParseStrings(element.GetProperty("categories")),
        ParseStrings(element.GetProperty("device_subtypes")),
        element.GetProperty("icon_source").GetString() ?? string.Empty,
        ParseSupport(element.GetProperty("add")),
        ParseSupport(element.GetProperty("durability")),
        ParseSupport(element.GetProperty("placement")),
        ParseSupport(element.GetProperty("remove")),
        ParseSupport(element.GetProperty("upgrades")));

    private static ReadOnlyCollection<string> ParseStrings(JsonElement element) =>
        Array.AsReadOnly(element.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray());

    private static CapabilitySupport ParseSupport(JsonElement element)
    {
        var maturityName = element.GetProperty("maturity").GetString();
        if (!Enum.TryParse<CapabilityMaturity>(maturityName, ignoreCase: true, out var maturity))
        {
            throw new InvalidDataException($"Unknown capability maturity: {maturityName}");
        }

        var reasonElement = element.GetProperty("reason");
        return new CapabilitySupport(
            maturity,
            reasonElement.ValueKind is JsonValueKind.Null ? null : reasonElement.GetString());
    }
}

public sealed record FormatCapabilityDescriptor(
    string Id,
    string ReleaseId,
    string Edition,
    FormatCapabilityProfile Capabilities)
{
    public IReadOnlyDictionary<string, object?> AsDictionary() => ReadOnly(
        ("id", Id),
        ("release_id", ReleaseId),
        ("edition", Edition),
        ("capabilities", Capabilities.AsDictionary()));

    private static ReadOnlyDictionary<string, object?> ReadOnly(
        params (string Key, object? Value)[] values) =>
        new ReadOnlyDictionary<string, object?>(
            values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
}

public sealed class FormatCapabilityProfile
{
    private static readonly string[] MutationFields =
    [
        "edit_money",
        "edit_stacks",
        "move_items",
        "add_items",
        "remove_items",
        "edit_durability",
        "edit_upgrades",
        "edit_relations",
        "edit_player_faction",
        "edit_placement",
    ];

    private readonly ReadOnlyDictionary<string, CapabilitySupport> _mutationSupport;

    internal FormatCapabilityProfile(
        bool readInventory,
        bool catalog,
        EquipmentCapabilityProfile? equipment,
        IReadOnlyDictionary<string, CapabilitySupport> mutationSupport)
    {
        var missing = MutationFields.Where(field => !mutationSupport.ContainsKey(field)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Capability snapshot is missing mutation fields: {string.Join(", ", missing)}");
        }

        ReadInventory = readInventory;
        Catalog = catalog;
        Equipment = equipment;
        _mutationSupport = new ReadOnlyDictionary<string, CapabilitySupport>(
            MutationFields.ToDictionary(field => field, field => mutationSupport[field], StringComparer.Ordinal));
        MutationSupport = _mutationSupport;
        ExperimentalFields = Array.AsReadOnly(
            MutationFields.Where(field => _mutationSupport[field].Maturity is CapabilityMaturity.Experimental)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public bool ReadInventory { get; }

    public bool Catalog { get; }

    public EquipmentCapabilityProfile? Equipment { get; }

    public IReadOnlyDictionary<string, CapabilitySupport> MutationSupport { get; }

    public IReadOnlyList<string> ExperimentalFields { get; }

    public CapabilitySupport Support(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        if (capability.StartsWith("equipment.", StringComparison.Ordinal))
        {
            return Equipment?.Support(capability)
                ?? throw new KeyNotFoundException($"Unknown capability '{capability}'.");
        }

        return _mutationSupport.TryGetValue(capability, out var support)
            ? support
            : throw new KeyNotFoundException($"Unknown capability '{capability}'.");
    }

    public bool HasCapability(string capability) => capability switch
    {
        "read_inventory" or "catalog" => true,
        "equipment.add" or "equipment.durability" or "equipment.placement"
            or "equipment.remove" or "equipment.upgrades" => Equipment is not null,
        _ => _mutationSupport.ContainsKey(capability),
    };

    public IReadOnlyDictionary<string, object?> AsDictionary()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["read_inventory"] = ReadInventory,
            ["catalog"] = Catalog,
            ["equipment"] = Equipment?.AsDictionary(),
            ["mutation_support"] = ReadOnly(
                MutationFields.Select(field =>
                    new KeyValuePair<string, object?>(field, _mutationSupport[field].AsDictionary()))),
            ["experimental_fields"] = ExperimentalFields,
        };

        foreach (var field in MutationFields)
        {
            payload[field] = _mutationSupport[field].Writable;
        }

        return new ReadOnlyDictionary<string, object?>(payload);
    }

    private static ReadOnlyDictionary<string, object?> ReadOnly(
        IEnumerable<KeyValuePair<string, object?>> values) =>
        new ReadOnlyDictionary<string, object?>(
            values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
}

public sealed record EquipmentCapabilityProfile(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> DeviceSubtypes,
    string IconSource,
    CapabilitySupport Add,
    CapabilitySupport Durability,
    CapabilitySupport Placement,
    CapabilitySupport Remove,
    CapabilitySupport Upgrades)
{
    public CapabilitySupport Support(string capability) => capability switch
    {
        "equipment.add" => Add,
        "equipment.durability" => Durability,
        "equipment.placement" => Placement,
        "equipment.remove" => Remove,
        "equipment.upgrades" => Upgrades,
        _ => throw new KeyNotFoundException($"Unknown equipment capability '{capability}'."),
    };

    public IReadOnlyDictionary<string, object?> AsDictionary() =>
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["categories"] = Categories,
            ["device_subtypes"] = DeviceSubtypes,
            ["icon_source"] = IconSource,
            ["add"] = Add.AsDictionary(),
            ["durability"] = Durability.AsDictionary(),
            ["placement"] = Placement.AsDictionary(),
            ["remove"] = Remove.AsDictionary(),
            ["upgrades"] = Upgrades.AsDictionary(),
        });
}

public static class CapabilityRegistry
{
    public static IReadOnlyList<FormatCapabilityDescriptor> Formats => CapabilityService.Default.Formats;

    public static FormatCapabilityDescriptor GetFormat(string releaseId) =>
        CapabilityService.Default.GetFormat(releaseId);

    public static CapabilitySupport Get(string releaseId, string capability) =>
        CapabilityService.Default.Get(releaseId, capability);

    public static bool HasParityCapability(string formatId, string capability) =>
        CapabilityService.Default.HasParityCapability(formatId, capability);
}
