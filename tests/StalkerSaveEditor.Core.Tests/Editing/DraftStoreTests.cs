using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Core.Editing;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Editing;

public sealed class DraftStoreTests
{
    private static readonly byte[] LegacySource = Encoding.UTF8.GetBytes("synthetic Python draft legacy vector");

    [Fact]
    public void Loads_python_schema_one_draft_without_persisting_a_source_locator()
    {
        using var directory = new TemporaryDirectory();
        var store = new DraftStore(directory.Path);
        var sourceSha256 = Sha256(LegacySource);
        var legacyPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "drafts", "python-v1-draft.json");
        File.Copy(legacyPath, store.PathFor(sourceSha256));

        var journal = store.Load(sourceSha256);

        Assert.NotNull(journal);
        Assert.Equal(2, journal.Index);
        Assert.Equal(3, journal.Plans.Count);
        Assert.Equal((uint)900_000, journal.Current.Money);
        Assert.Equal((uint)44, journal.Current.StackCounts[0x1234]);
        Assert.Equal((ushort)0x2345, Assert.Single(journal.Current.DetachHandles));
        var addition = Assert.Single(journal.Current.Adds);
        Assert.Equal("wpn_test", addition.ItemKey);
        Assert.Equal((uint)2, addition.Quantity);
        Assert.Equal("inventory", addition.Destination);

        var json = File.ReadAllText(legacyPath, Encoding.UTF8);
        Assert.DoesNotContain("synthetic-old-slot.sav", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Saves_roundtrips_history_and_supports_undo_redo_and_branching()
    {
        using var directory = new TemporaryDirectory();
        var store = new DraftStore(directory.Path);
        var sourceSha256 = Sha256("synthetic save bytes"u8);
        var empty = new EditPlan(sourceSha256);
        var first = new EditPlan(sourceSha256, money: 100);
        var second = new EditPlan(sourceSha256, money: 200,
            stackCounts: new Dictionary<uint, uint> { [0x1234] = 8 });
        var journal = new DraftJournal([empty, first, second], 2);

        store.Save(journal);
        var path = store.PathFor(sourceSha256);
        using var payload = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(2, payload.RootElement.GetProperty("schema").GetInt32());
        var serialized = File.ReadAllText(path, Encoding.UTF8);
        Assert.DoesNotContain("/home/", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("slot.sav", serialized, StringComparison.Ordinal);

        var restored = store.Load(sourceSha256);
        Assert.NotNull(restored);
        Assert.Equal(2, restored.Index);
        Assert.Equal((uint)200, restored.Current.Money);
        Assert.Equal((uint)8, restored.Current.StackCounts[0x1234]);
        Assert.Equal((uint)100, restored.Undo().Current.Money);
        Assert.Equal((uint)200, restored.Undo().Redo().Current.Money);

        var branched = restored.Undo().Record(new EditPlan(sourceSha256, money: 300));
        Assert.Equal(3, branched.Plans.Count);
        Assert.Equal((uint)300, branched.Current.Money);
        Assert.False(branched.CanRedo);
    }

    [Fact]
    public void Does_not_load_a_draft_for_a_different_source_sha()
    {
        using var directory = new TemporaryDirectory();
        var store = new DraftStore(directory.Path);
        var oldSha = Sha256("original bytes"u8);
        var changedSha = Sha256("changed bytes"u8);
        store.Save(new DraftJournal([new EditPlan(oldSha), new EditPlan(oldSha, money: 9)], 1));

        Assert.Null(store.Load(changedSha));
        Assert.True(File.Exists(store.PathFor(oldSha)));
    }

    [Fact]
    public void Saving_an_empty_current_plan_removes_the_recovery_file()
    {
        using var directory = new TemporaryDirectory();
        var store = new DraftStore(directory.Path);
        var sourceSha256 = Sha256("save bytes"u8);
        store.Save(new DraftJournal([
            new EditPlan(sourceSha256),
            new EditPlan(sourceSha256, money: 77),
        ], 1));

        store.Save(new DraftJournal([new EditPlan(sourceSha256)], 0));

        Assert.False(File.Exists(store.PathFor(sourceSha256)));
        Assert.Null(store.Load(sourceSha256));
    }

    [Fact]
    public void A_draft_with_edits_this_version_cannot_read_is_not_edited_saved_or_deleted()
    {
        using var directory = new TemporaryDirectory();
        var store = new DraftStore(directory.Path);
        var sourceSha256 = Sha256(LegacySource);
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "drafts", "python-v1-draft.json"), Encoding.UTF8)
            .Replace("\"durability\":[]", "\"durability\":[],\"future_operation\":{\"opaque\":true}", StringComparison.Ordinal);
        File.WriteAllText(store.PathFor(sourceSha256), json, new UTF8Encoding(false));
        var session = new StalkerSaveEditor.Desktop.ViewModels.DraftSession(store);

        var opened = session.Open(sourceSha256);
        Assert.NotNull(opened);
        Assert.True(session.HasUnsupportedEdits);
        // Editing on top is refused without throwing and without touching the stored draft.
        Assert.False(session.Record(sourceSha256, new EditPlan(sourceSha256, money: 1)));
        Assert.Equal(json, File.ReadAllText(store.PathFor(sourceSha256), Encoding.UTF8));

        // The write boundary refuses the save even if a caller builds a plan from the visible fields.
        var save = new StalkerSaveEditor.Desktop.ViewModels.SaveFileSummary(Path.Combine(directory.Path, "none.sav"), "SoC", "stalker-soc", sourceSha256, 0, true, []);
        Assert.Throws<InvalidOperationException>(() => StalkerSaveEditor.Desktop.ViewModels.SaveEditSession.WritePlan(
            save, new EditPlan(sourceSha256, money: 1), catalog: null, directory.Path, store));
        Assert.True(File.Exists(store.PathFor(sourceSha256)));

        // Discarding keeps the unreadable draft next to the store instead of deleting it.
        session.Discard(sourceSha256);
        Assert.False(session.HasUnsupportedEdits);
        Assert.False(File.Exists(store.PathFor(sourceSha256)));
        var kept = Assert.Single(Directory.GetFiles(directory.Path, "*.unsupported-*"));
        Assert.Equal(json, File.ReadAllText(kept, Encoding.UTF8));
    }

    [Fact]
    public void Preserves_unmapped_legacy_edits_and_rejects_an_invalid_schema_version()
    {
        using var directory = new TemporaryDirectory();
        var store = new DraftStore(directory.Path);
        var sourceSha256 = Sha256(LegacySource);
        var legacyPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "drafts", "python-v1-draft.json");
        var json = File.ReadAllText(legacyPath, Encoding.UTF8);
        var legacyWithUnknownEdits = json.Replace(
            "\"durability\":[]",
            "\"durability\":[[9029,0.5]],\"future_operation\":{\"opaque\":true}",
            StringComparison.Ordinal);
        Assert.NotEqual(json, legacyWithUnknownEdits);
        File.WriteAllText(store.PathFor(sourceSha256), legacyWithUnknownEdits, new UTF8Encoding(false));

        var recovered = store.Load(sourceSha256);
        Assert.NotNull(recovered);
        Assert.False(recovered.CanApplyCurrent);
        Assert.Equal((uint)900_000, recovered.Current.Money);
        Assert.Throws<InvalidOperationException>(() => recovered.Record(new EditPlan(sourceSha256, money: 901_000)));
        Assert.Equal(0.5, recovered.CurrentUnmappedLegacyPlan!.Value
            .GetProperty("durability")[0][1].GetDouble());
        Assert.True(recovered.CurrentUnmappedLegacyPlan.Value
            .GetProperty("future_operation").GetProperty("opaque").GetBoolean());

        store.Save(recovered);
        var roundTripped = store.Load(sourceSha256);
        Assert.NotNull(roundTripped);
        Assert.False(roundTripped.CanApplyCurrent);
        Assert.True(roundTripped.CurrentUnmappedLegacyPlan!.Value
            .GetProperty("future_operation").GetProperty("opaque").GetBoolean());

        var explicitlyDiscarded = roundTripped.Record(
            new EditPlan(sourceSha256, money: 901_000), discardUnmappedEdits: true);
        Assert.True(explicitlyDiscarded.CanApplyCurrent);

        var invalidVersion = json.Replace("\"schema\":1", "\"schema\":true", StringComparison.Ordinal);
        File.WriteAllText(store.PathFor(sourceSha256), invalidVersion, new UTF8Encoding(false));
        Assert.Null(store.Load(sourceSha256));
    }

    [Fact]
    public void Refuses_a_current_plan_that_exceeds_the_draft_storage_limit()
    {
        using var directory = new TemporaryDirectory();
        var store = new DraftStore(directory.Path);
        var sourceSha256 = Sha256("large synthetic save"u8);
        var additions = Enumerable.Range(0, 40_000)
            .Select(index => new ItemAddRequest($"synthetic_item_{index:D5}", 1))
            .ToArray();
        var plan = new EditPlan(sourceSha256, adds: additions);

        Assert.Throws<IOException>(() => store.Save(new DraftJournal([plan], 0)));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"draft-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void A_long_session_keeps_the_untouched_state_and_the_latest_steps_only()
    {
        var sha = new string('a', 64);
        var journal = new DraftJournal([new EditPlan(sha)], 0);
        for (uint money = 1; money <= DraftJournal.MaximumSteps + 50; money++) journal = journal.Record(new EditPlan(sha, money: money));

        Assert.Equal(DraftJournal.MaximumSteps + 1, journal.Plans.Count);
        Assert.Equal((uint)(DraftJournal.MaximumSteps + 50), journal.Current.Money);
        Assert.Null(journal.Plans[0].Money);
        Assert.Equal(51u, journal.Plans[1].Money);

        // Undo still walks back one edit at a time and ends at the untouched save; a new edit after undo drops the redo tail.
        var back = journal.Undo().Undo();
        Assert.Equal((uint)(DraftJournal.MaximumSteps + 48), back.Current.Money);
        var branched = back.Record(new EditPlan(sha, money: 7));
        Assert.Equal(7u, branched.Current.Money);
        Assert.False(branched.CanRedo);
        Assert.Equal(DraftJournal.MaximumSteps, branched.Plans.Count);
    }
}
