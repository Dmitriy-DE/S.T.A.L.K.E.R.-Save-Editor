using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Core.Editing;

/// <summary>
/// Unified save editing service for S.T.A.L.K.E.R. save files (X-Ray trilogy and S.T.A.L.K.E.R. 2).
/// Dispatches edit operations to appropriate format writers and enforces platform/release safety.
/// </summary>
public static class EditService
{
    private const EditKind KnownEditKinds =
        EditKind.Money |
        EditKind.StackCounts |
        EditKind.Delete |
        EditKind.Add |
        EditKind.XRayStashTransfer |
        EditKind.Stalker2StashTransfer |
        EditKind.Upgrades |
        EditKind.Durability |
        EditKind.Placement |
        EditKind.Faction;

    private static readonly (EditKind Kind, string[] Capabilities)[] EditCapabilities =
    [
        (EditKind.Money, ["edit_money"]),
        (EditKind.StackCounts, ["edit_stacks"]),
        (EditKind.Delete, ["remove_items"]),
        (EditKind.Add, ["add_items"]),
        (EditKind.XRayStashTransfer, ["add_items"]),
        (EditKind.Stalker2StashTransfer, ["move_items"]),
        (EditKind.Upgrades, ["edit_upgrades"]),
        (EditKind.Durability, ["edit_durability"]),
        (EditKind.Placement, ["edit_placement"]),
        (EditKind.Faction, ["edit_relations", "edit_player_faction"]),
    ];

    /// <summary>
    /// Checks whether saving/editing is permitted by the central release capability registry.
    /// </summary>
    public static bool CanEdit(string? releaseId, EditKind editKinds = EditKind.None)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
        {
            return false;
        }

        if (editKinds == EditKind.None)
        {
            return CapabilityService.Default.CanWrite(releaseId, "edit_money");
        }

        if ((editKinds & ~KnownEditKinds) != EditKind.None)
        {
            return false;
        }

        var requiredCapabilities = new List<string>(EditCapabilities.Length);
        foreach (var requirement in EditCapabilities)
        {
            if ((editKinds & requirement.Kind) != EditKind.None)
            {
                requiredCapabilities.AddRange(requirement.Capabilities);
            }
        }

        return CapabilityService.Default.CanWrite(releaseId, requiredCapabilities.ToArray());
    }

    /// <summary>
    /// Returns true if the specified release ID belongs to the X-Ray engine family.
    /// </summary>
    public static bool IsXRayRelease(string? releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId)) return false;
        var normalized = releaseId.Trim().ToLowerInvariant();
        return normalized is "stalker-soc" or "stalker-soc-ee"
            or "stalker-cs" or "stalker-cs-ee"
            or "stalker-cop" or "stalker-cop-ee"
            or "soc" or "clear_sky" or "cop";
    }

    /// <summary>
    /// Returns true if the specified release ID belongs to S.T.A.L.K.E.R. 2.
    /// </summary>
    public static bool IsStalker2Release(string? releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId)) return false;
        var normalized = releaseId.Trim().ToLowerInvariant();
        return normalized is "stalker2" or "s2" || normalized.StartsWith("stalker2", StringComparison.Ordinal);
    }

    /// <summary>
    /// Detects the format of the save data using content-based inspection.
    /// </summary>
    public static string? DetectFormat(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data).FormatId;
        }
        catch (XRayFormatException)
        {
        }

        try
        {
            return XRayEnhancedReader.FromBytes(data).FormatId;
        }
        catch (XRayFormatException)
        {
        }

        return Stalker2SaveReader.Detect(data) ? "stalker2" : null;
    }

    /// <summary>
    /// Prepares an edit against the provided source save bytes using the appropriate format writer.
    /// </summary>
    public static PreparedEdit PrepareEdit(
        ReadOnlySpan<byte> source,
        EditPlan plan,
        string? releaseId = null,
        CatalogBundle? catalogs = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlan(plan);

        // Every distinct save image met while preparing (the source, each stage's output) is parsed once.
        using var parseSession = Formats.ParseSession.Begin();

        var detectedRelease = CanonicalReleaseId(releaseId);
        if (string.IsNullOrWhiteSpace(detectedRelease))
        {
            detectedRelease = DetectFormat(source)
                ?? throw new InvalidOperationException("Could not detect save format for editing.");
        }

        if (IsXRayRelease(detectedRelease))
        {
            if (plan.Stalker2StashTakeHandle is not null)
                throw new NotSupportedException("The plan holds a S.T.A.L.K.E.R. 2 stash transfer, which an X-Ray save cannot take.");
            // The requested release must be the one the bytes actually are, before anything is written:
            // the read-back after the replacement compares against the same canonical id.
            var actual = DetectXRayFormat(source);
            if (!string.IsNullOrWhiteSpace(releaseId) && !string.Equals(actual, detectedRelease, StringComparison.Ordinal))
                throw new InvalidOperationException($"The save is '{actual}', not the requested release '{detectedRelease}'.");
            var prepared = PrepareXRay(source, plan, catalogs);
            // The prepared bytes must already satisfy the plan: a mismatch found only by the read-back after the
            // replacement would leave a changed file and a recovery to do. (The S2 writer round-trips its own output.)
            try
            {
                VerifyReadBack(prepared.Data.Span, actual, plan);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidOperationException("The prepared save does not match the requested edits; nothing was written. " + exception.Message, exception);
            }
            return prepared;
        }

        if (IsStalker2Release(detectedRelease))
        {
            return Stalker2EditWriter.Prepare(source, plan);
        }

        throw new NotSupportedException($"Unsupported release for editing: '{detectedRelease}'.");
    }

    /// <summary>The canonical release id for the aliases the API accepts ("cop" → "stalker-cop"); null stays null.</summary>
    public static string? CanonicalReleaseId(string? releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId)) return null;
        var normalized = releaseId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "soc" => "stalker-soc",
            "clear_sky" => "stalker-cs",
            "cop" => "stalker-cop",
            "s2" => "stalker2",
            _ => normalized,
        };
    }

    private static string DetectXRayFormat(ReadOnlySpan<byte> source)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(source).FormatId;
        }
        catch (XRayFormatException)
        {
            return XRayEnhancedReader.FromBytes(source).FormatId;
        }
    }

    /// <summary>
    /// Refuses a plan that edits and removes the same item: the writers would run both, and the read-back after the
    /// file was already replaced would then fail on the removed item.
    /// </summary>
    private static void ValidatePlan(EditPlan plan)
    {
        if (plan.DetachHandles.Count == 0) return;
        var removed = plan.DetachHandles.Select(handle => (uint)handle).ToHashSet();
        var edited = plan.StackCounts.Keys
            .Concat(plan.Durability.Keys)
            .Concat(plan.Upgrades.Keys.Select(handle => (uint)handle))
            .Concat(plan.Placements.Select(placement => placement.Handle))
            .Concat(plan.StashPuts.Select(put => (uint)put.ObjectId))
            .Concat(plan.StashTakes.Select(handle => (uint)handle));
        foreach (var handle in edited)
        {
            if (removed.Contains(handle))
                throw new InvalidOperationException($"The plan both edits and removes item 0x{handle:X4}; remove the item or keep the edit, not both.");
        }
    }

    /// <summary>
    /// X-Ray add and delete have their own writers; the rest of the plan goes through
    /// <see cref="XRayEditWriter"/>. Steps run in a fixed order (in-place edits, then deletes, then
    /// adds), each against the previous step's output and its SHA-256.
    /// </summary>
    private static PreparedEdit PrepareXRay(ReadOnlySpan<byte> source, EditPlan plan, CatalogBundle? catalogs)
    {
        if ((plan.EditKinds & (EditKind.Add | EditKind.Delete)) == EditKind.None)
        {
            return XRayEditWriter.Prepare(source, plan, catalogs);
        }

        var working = source.ToArray();
        var sha = plan.SourceSha256;
        var rest = new EditPlan(
            sha,
            money: plan.Money,
            stackCounts: plan.StackCounts,
            stashTakes: plan.StashTakes,
            stashPuts: plan.StashPuts,
            upgrades: plan.Upgrades,
            playerFaction: plan.PlayerFaction,
            factionRelations: plan.FactionRelations,
            durability: plan.Durability,
            placements: plan.Placements);
        if (rest.EditKinds != EditKind.None)
        {
            var step = XRayEditWriter.Prepare(working, rest, catalogs);
            (working, sha) = (step.Data.ToArray(), step.OutputSha256);
        }

        if (plan.DetachHandles.Count > 0)
        {
            var step = XRayDeleteWriter.Prepare(working, new EditPlan(sha, detachHandles: plan.DetachHandles));
            (working, sha) = (step.Data.ToArray(), step.OutputSha256);
        }

        if (plan.Adds.Count > 0)
        {
            var items = catalogs?.Items ?? throw new XRayFormatException("X-Ray edit: adding items requires the release catalog.");
            var step = XRayAddWriter.Prepare(working, new EditPlan(sha, adds: plan.Adds), items);
            working = step.Data.ToArray();
        }

        return new PreparedEdit(plan, working);
    }

    /// <summary>
    /// Verifies that data read back from a newly written save matches expectations and the edit plan.
    /// </summary>
    public static void VerifyReadBack(ReadOnlySpan<byte> data, string expectedReleaseId, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedReleaseId);
        expectedReleaseId = CanonicalReleaseId(expectedReleaseId)!;

        if (IsXRayRelease(expectedReleaseId))
        {
            XRayTrilogySave parsed;
            try
            {
                parsed = XRayTrilogyReader.FromBytes(data);
            }
            catch (XRayFormatException)
            {
                parsed = XRayEnhancedReader.FromBytes(data);
            }

            if (!string.Equals(parsed.FormatId, expectedReleaseId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The replaced save changed its detected release.");
            }

            if (plan.Money is { } money && parsed.Money != money)
            {
                throw new InvalidDataException("The replaced save money does not match the requested value.");
            }

            foreach (var (handle, count) in plan.StackCounts)
            {
                if (parsed.Inventory.FirstOrDefault(item => item.Handle == handle)?.Count != count)
                {
                    throw new InvalidDataException($"The replaced save stack 0x{handle:X4} does not match the requested value.");
                }
            }

            foreach (var put in plan.StashPuts)
            {
                if (parsed.Stashes.FirstOrDefault(stash => stash.Handle == put.BoxId)?.Items.Any(item => item.Handle == put.ObjectId) != true)
                {
                    throw new InvalidDataException($"The replaced save does not hold item 0x{put.ObjectId:X4} in stash 0x{put.BoxId:X4}.");
                }
            }

            foreach (var take in plan.StashTakes)
            {
                if (parsed.Inventory.All(item => item.Handle != take))
                {
                    throw new InvalidDataException($"The replaced save does not hold item 0x{take:X4} in the inventory.");
                }
            }
            return;
        }

        if (IsStalker2Release(expectedReleaseId))
        {
            var parsed = Stalker2SaveReader.FromBytes(data);
            if (!string.Equals(parsed.ReleaseId, expectedReleaseId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The replaced save changed its detected release.");
            }

            if (plan.Money is { } money && parsed.Money != money)
            {
                throw new InvalidDataException("The replaced save money does not match the requested value.");
            }

            foreach (var (handle, count) in plan.StackCounts)
            {
                if (parsed.Inventory.FirstOrDefault(item => item.Handle == handle)?.Count != count)
                {
                    throw new InvalidDataException($"The replaced save stack 0x{handle:X8} does not match the requested value.");
                }
            }
            return;
        }

        throw new NotSupportedException($"Unsupported release for readback verification: '{expectedReleaseId}'.");
    }

}
