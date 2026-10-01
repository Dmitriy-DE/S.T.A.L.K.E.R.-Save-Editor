using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StalkerSaveEditor.Core.Editing;

public sealed class DraftJournal
{
    public DraftJournal(IEnumerable<EditPlan> plans, int index, IEnumerable<JsonElement?>? unmappedLegacyPlans = null)
    {
        ArgumentNullException.ThrowIfNull(plans);
        var snapshots = plans.ToArray();
        if (snapshots.Length == 0 || snapshots.Any(plan => plan is null))
        {
            throw new ArgumentException("A draft journal must contain at least one plan.", nameof(plans));
        }

        var sourceSha256 = snapshots[0].SourceSha256;
        if (snapshots.Any(plan => !string.Equals(plan.SourceSha256, sourceSha256, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Draft journal plans must share one source SHA256.", nameof(plans));
        }

        if ((uint)index >= (uint)snapshots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Draft journal index is out of range.");
        }

        Plans = Array.AsReadOnly(snapshots);
        var legacySnapshots = unmappedLegacyPlans?.Select(value => value?.Clone()).ToArray() ?? new JsonElement?[snapshots.Length];
        if (legacySnapshots.Length != snapshots.Length)
        {
            throw new ArgumentException("Unmapped legacy snapshots must match the plan history length.", nameof(unmappedLegacyPlans));
        }

        UnmappedLegacyPlans = Array.AsReadOnly(legacySnapshots);
        Index = index;
    }

    public IReadOnlyList<EditPlan> Plans { get; }

    public int Index { get; }

    public EditPlan Current => Plans[Index];

    public IReadOnlyList<JsonElement?> UnmappedLegacyPlans { get; }

    public JsonElement? CurrentUnmappedLegacyPlan => UnmappedLegacyPlans[Index];

    public bool CurrentHasUnmappedEdits => CurrentUnmappedLegacyPlan.HasValue;

    public bool CanApplyCurrent => !CurrentHasUnmappedEdits;

    public bool CanUndo => Index > 0;

    public bool CanRedo => Index + 1 < Plans.Count;

    public DraftJournal Undo() => CanUndo ? new DraftJournal(Plans, Index - 1, UnmappedLegacyPlans) : this;

    public DraftJournal Redo() => CanRedo ? new DraftJournal(Plans, Index + 1, UnmappedLegacyPlans) : this;

    public DraftJournal Record(EditPlan plan, bool discardUnmappedEdits = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (CurrentHasUnmappedEdits && !discardUnmappedEdits)
        {
            throw new InvalidOperationException("The current draft has legacy edits this version cannot interpret.");
        }

        if (!string.Equals(plan.SourceSha256, Current.SourceSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("Recorded plan must match the journal source SHA256.", nameof(plan));
        }

        var next = new EditPlan[Index + 2];
        for (var i = 0; i <= Index; i++)
        {
            next[i] = Plans[i];
        }

        next[^1] = plan;
        var nextLegacy = new JsonElement?[next.Length];
        for (var i = 0; i <= Index; i++)
        {
            nextLegacy[i] = UnmappedLegacyPlans[i];
        }

        return new DraftJournal(next, next.Length - 1, nextLegacy);
    }
}

public sealed partial class DraftStore
{
    private const int CurrentSchemaVersion = 2;
    private const int LegacyPythonSchemaVersion = 1;
    private const int MaximumDraftBytes = 2 * 1024 * 1024;
    private const string DraftNamePattern = "^[0-9a-f]{64}$";

    private static readonly HashSet<string> LegacyPlanFields = new(StringComparer.Ordinal)
    {
        "adds",
        "attach",
        "detach",
        "durability",
        "faction_relations",
        "money",
        "moves",
        "placements",
        "player_faction",
        "raw",
        "stacks",
        "upgrades",
    };

    private readonly string _directory;

    public DraftStore(string? directory = null)
    {
        var selectedDirectory = string.IsNullOrWhiteSpace(directory) ? GetDefaultDirectory() : directory;
        _directory = Path.GetFullPath(selectedDirectory);
    }

    public string DirectoryPath => _directory;

    public string PathFor(string sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(sourceSha256);
        if (!System.Text.RegularExpressions.Regex.IsMatch(sourceSha256, DraftNamePattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Draft key must be a lowercase SHA256.", nameof(sourceSha256));
        }

        return Path.Combine(_directory, $"{sourceSha256}.json");
    }

    public void Save(DraftJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var sourceSha256 = journal.Current.SourceSha256;
        var path = PathFor(sourceSha256);
        if (!HasChanges(journal.Current) && !journal.CurrentHasUnmappedEdits)
        {
            File.Delete(path);
            return;
        }

        var persisted = journal;
        var data = Serialize(persisted);
        if (data.Length > MaximumDraftBytes)
        {
            persisted = new DraftJournal(
                [new EditPlan(sourceSha256), journal.Current],
                1,
                [null, journal.CurrentUnmappedLegacyPlan]);
            data = Serialize(persisted);
            if (data.Length > MaximumDraftBytes)
            {
                throw new IOException("Current edit draft exceeds the storage limit.");
            }
        }

        Directory.CreateDirectory(_directory);
        var temporaryPath = Path.Combine(_directory, $".{sourceSha256}.{Guid.NewGuid():N}.tmp");
        try
        {
            Storage.DurableFile.WriteNew(temporaryPath, data, ownerOnly: true);
            Storage.DurableFile.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    public DraftJournal? Load(string sourceSha256)
    {
        var path = PathFor(sourceSha256);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaximumDraftBytes)
            {
                return null;
            }

            var data = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryReadInt32(document.RootElement, "schema", out var schema))
            {
                return null;
            }

            return schema switch
            {
                CurrentSchemaVersion => LoadCurrent(document.RootElement, sourceSha256),
                LegacyPythonSchemaVersion => LoadLegacyPython(document.RootElement, sourceSha256),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                                          ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            return null;
        }
    }

    public void Remove(string sourceSha256) => File.Delete(PathFor(sourceSha256));

    /// <summary>
    /// Sets the draft aside instead of deleting it: used when it holds edits this version cannot interpret, so a
    /// newer version (or the user) can still read them. Returns the kept file, or null when there was no draft.
    /// </summary>
    public string? SetAside(string sourceSha256)
    {
        var path = PathFor(sourceSha256);
        if (!File.Exists(path)) return null;
        var kept = path + ".unsupported-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        File.Move(path, kept);
        return kept;
    }

    private static DraftJournal? LoadCurrent(JsonElement root, string expectedSha256)
    {
        RequireProperties(root, "index", "plans", "schema", "source_sha256");
        if (!TryReadString(root, "source_sha256", out var sourceSha256) || sourceSha256 != expectedSha256 ||
            !TryReadInt32(root, "index", out var index) ||
            !root.TryGetProperty("plans", out var rawPlans) || rawPlans.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var planEnvelopes = JsonSerializer.Deserialize(
            rawPlans.GetRawText(),
            DraftJsonContext.Default.CurrentPlanEnvelopes);
        if (planEnvelopes is null || planEnvelopes.Any(plan => plan is null || plan.SourceSha256 != expectedSha256))
        {
            return null;
        }

        var plans = planEnvelopes.Select(plan => plan.ToEditPlan()).ToArray();
        var legacyPlans = planEnvelopes.Select(plan => plan.UnmappedLegacyPlan).ToArray();
        var journal = new DraftJournal(plans, index, legacyPlans);
        return HasChanges(journal.Current) || journal.CurrentHasUnmappedEdits ? journal : null;
    }

    private static DraftJournal? LoadLegacyPython(JsonElement root, string expectedSha256)
    {
        RequireProperties(root, "index", "plans", "schema", "source_sha256");
        if (!TryReadString(root, "source_sha256", out var sourceSha256) || sourceSha256 != expectedSha256 ||
            !TryReadInt32(root, "index", out var index) ||
            !root.TryGetProperty("plans", out var rawPlans) || rawPlans.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var legacySnapshots = rawPlans.EnumerateArray()
            .Select(plan => ReadLegacyPlan(plan, sourceSha256))
            .ToArray();
        var journal = new DraftJournal(
            legacySnapshots.Select(snapshot => snapshot.Plan),
            index,
            legacySnapshots.Select(snapshot => snapshot.UnmappedPlan));
        return HasChanges(journal.Current) || journal.CurrentHasUnmappedEdits ? journal : null;
    }

    private static (EditPlan Plan, JsonElement? UnmappedPlan) ReadLegacyPlan(JsonElement raw, string sourceSha256)
    {
        if (raw.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Legacy draft plan must be an object.");
        }

        var hasUnknownFields = raw.EnumerateObject().Any(property => !LegacyPlanFields.Contains(property.Name));
        var hasUnsupportedEdits = HasUnsupportedLegacyEdits(raw);
        var money = ReadNullableUInt32(raw, "money");
        var stacks = ReadStacks(raw);
        var (detach, hasDeepDetach) = ReadDetaches(raw);
        var adds = ReadAdds(raw);
        var plan = new EditPlan(sourceSha256, money, stacks, detach, adds);
        var unmapped = hasUnknownFields || hasUnsupportedEdits || hasDeepDetach ? raw.Clone() : (JsonElement?)null;
        return (plan, unmapped);
    }

    private static bool HasUnsupportedLegacyEdits(JsonElement plan)
    {
        var hasEdits = false;
        foreach (var name in new[] { "attach", "durability", "faction_relations", "moves", "placements", "raw", "upgrades" })
        {
            if (plan.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Legacy draft {name} field must be an array.");
            }

            if (plan.TryGetProperty(name, out value) && value.GetArrayLength() != 0)
            {
                hasEdits = true;
            }
        }

        if (plan.TryGetProperty("player_faction", out var faction))
        {
            if (faction.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            {
                throw new InvalidDataException("Legacy draft player faction must be text or null.");
            }

            hasEdits |= faction.ValueKind == JsonValueKind.String;
        }

        return hasEdits;
    }

    private static uint? ReadNullableUInt32(JsonElement plan, string property)
    {
        if (!plan.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var parsed))
        {
            throw new InvalidDataException("Legacy draft money must be an unsigned 32-bit integer or null.");
        }

        return parsed;
    }

    private static Dictionary<uint, uint> ReadStacks(JsonElement plan)
    {
        var result = new Dictionary<uint, uint>();
        if (!plan.TryGetProperty("stacks", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            if (plan.TryGetProperty("stacks", out _))
            {
                throw new InvalidDataException("Legacy draft stacks must be an array.");
            }

            return result;
        }

        foreach (var row in rows.EnumerateArray())
        {
            var values = ReadTuple(row, 2);
            result.Add(ReadUInt32(values[0]), ReadUInt32(values[1]));
        }

        return result;
    }

    private static (ushort[] Handles, bool HasDeepDetach) ReadDetaches(JsonElement plan)
    {
        var result = new List<ushort>();
        var hasDeepDetach = false;
        if (!plan.TryGetProperty("detach", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            if (plan.TryGetProperty("detach", out _))
            {
                throw new InvalidDataException("Legacy draft detach edits must be an array.");
            }

            return ([], false);
        }

        foreach (var row in rows.EnumerateArray())
        {
            var values = ReadTuple(row, 2);
            var handle = ReadUInt32(values[0]);
            if (handle is 0 or ushort.MaxValue or > ushort.MaxValue ||
                values[1].ValueKind is not (JsonValueKind.False or JsonValueKind.True))
            {
                throw new InvalidDataException("Legacy draft detach row is invalid.");
            }

            if (values[1].GetBoolean())
            {
                hasDeepDetach = true;
                continue;
            }

            result.Add((ushort)handle);
        }

        return (result.ToArray(), hasDeepDetach);
    }

    private static ItemAddRequest[] ReadAdds(JsonElement plan)
    {
        var result = new List<ItemAddRequest>();
        if (!plan.TryGetProperty("adds", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            if (plan.TryGetProperty("adds", out _))
            {
                throw new InvalidDataException("Legacy draft additions must be an array.");
            }

            return [];
        }

        foreach (var row in rows.EnumerateArray())
        {
            var values = ReadTuple(row, 3);
            if (values[0].ValueKind != JsonValueKind.String || values[2].ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Legacy draft addition text fields must be strings.");
            }

            result.Add(new ItemAddRequest(values[0].GetString()!, ReadUInt32(values[1]), values[2].GetString()!));
        }

        return result.ToArray();
    }

    private static JsonElement[] ReadTuple(JsonElement value, int expectedLength)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Legacy draft row must be an array.");
        }

        var elements = value.EnumerateArray().ToArray();
        if (elements.Length != expectedLength)
        {
            throw new InvalidDataException("Legacy draft row has an invalid shape.");
        }

        return elements;
    }

    private static uint ReadUInt32(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var parsed))
        {
            throw new InvalidDataException("Legacy draft value must be an unsigned 32-bit integer.");
        }

        return parsed;
    }

    private static byte[] Serialize(DraftJournal journal)
    {
        var payload = new CurrentDraftEnvelope
        {
            Index = journal.Index,
            Plans = journal.Plans
                .Select((plan, index) => CurrentPlanEnvelope.From(plan, journal.UnmappedLegacyPlans[index]))
                .ToArray(),
            Schema = CurrentSchemaVersion,
            SourceSha256 = journal.Current.SourceSha256,
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, DraftJsonContext.Default.CurrentDraftEnvelope);
    }

    private static bool HasChanges(EditPlan plan) =>
        plan.Money is not null || plan.StackCounts.Count != 0 || plan.DetachHandles.Count != 0 ||
        plan.Adds.Count != 0 || plan.StashTakes.Count != 0 || plan.StashPuts.Count != 0;

    private static void RequireProperties(JsonElement root, params string[] expected)
    {
        var set = new HashSet<string>(expected, StringComparer.Ordinal);
        if (root.EnumerateObject().Any(property => !set.Remove(property.Name)) || set.Count != 0)
        {
            throw new InvalidDataException("Draft journal has an unknown or missing field.");
        }
    }

    private static bool TryReadString(JsonElement root, string property, out string value)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadInt32(JsonElement root, string property, out int value)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string GetDefaultDirectory() => Diagnostics.AppPaths.Drafts;

    private static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
            // A failed write remains the caller's primary error.
        }
        catch (DirectoryNotFoundException)
        {
            // A failed write remains the caller's primary error.
        }
        catch (IOException)
        {
            // A failed write remains the caller's primary error.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed write remains the caller's primary error.
        }
    }

    private sealed class CurrentDraftEnvelope
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("plans")]
        public IReadOnlyList<CurrentPlanEnvelope> Plans { get; init; } = Array.Empty<CurrentPlanEnvelope>();

        [JsonPropertyName("schema")]
        public int Schema { get; init; }

        [JsonPropertyName("source_sha256")]
        public string SourceSha256 { get; init; } = string.Empty;
    }

    private sealed class CurrentPlanEnvelope
    {
        public string SourceSha256 { get; init; } = string.Empty;

        public uint? Money { get; init; }

        public Dictionary<uint, uint> StackCounts { get; init; } = [];

        public List<ushort> DetachHandles { get; init; } = [];

        public List<ItemAddRequest> Adds { get; init; } = [];

        public List<ushort> StashTakes { get; init; } = [];

        public List<StashPutRequest> StashPuts { get; init; } = [];

        public JsonElement? UnmappedLegacyPlan { get; init; }

        public EditPlan ToEditPlan() => new(
            SourceSha256,
            Money,
            StackCounts,
            DetachHandles,
            Adds,
            StashTakes,
            StashPuts);

        public static CurrentPlanEnvelope From(EditPlan plan, JsonElement? unmappedLegacyPlan) => new()
        {
            SourceSha256 = plan.SourceSha256,
            Money = plan.Money,
            StackCounts = new Dictionary<uint, uint>(plan.StackCounts),
            DetachHandles = plan.DetachHandles.ToList(),
            Adds = plan.Adds.ToList(),
            StashTakes = plan.StashTakes.ToList(),
            StashPuts = plan.StashPuts.ToList(),
            UnmappedLegacyPlan = unmappedLegacyPlan,
        };
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
    [JsonSerializable(typeof(CurrentDraftEnvelope), TypeInfoPropertyName = "CurrentDraftEnvelope")]
    [JsonSerializable(typeof(CurrentPlanEnvelope[]), TypeInfoPropertyName = "CurrentPlanEnvelopes")]
    private partial class DraftJsonContext : JsonSerializerContext
    {
    }
}
