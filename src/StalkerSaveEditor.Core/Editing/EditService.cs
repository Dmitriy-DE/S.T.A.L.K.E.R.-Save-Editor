using System.IO;
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
    /// <summary>
    /// Checks whether saving/editing is permitted for the specified release and optional edit kinds.
    /// S.T.A.L.K.E.R. 2 writing is explicitly disabled for consumer UI until full in-game validation.
    /// </summary>
    public static bool CanEdit(string? releaseId, EditKind editKinds = EditKind.None)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
        {
            return false;
        }

        // S2 writing remains disabled in UI until in-game mutation verification is completed.
        if (IsStalker2Release(releaseId))
        {
            return false;
        }

        if (!IsXRayRelease(releaseId))
        {
            return false;
        }

        var normalized = NormalizeXRayReleaseId(releaseId);
        if (normalized is null)
        {
            return false;
        }

        try
        {
            if (editKinds == EditKind.None)
            {
                return CapabilityRegistry.Get(normalized, "edit_money").Writable;
            }

            const EditKind allowedXRay =
                EditKind.Money |
                EditKind.StackCounts |
                EditKind.Durability |
                EditKind.Placement |
                EditKind.Upgrades |
                EditKind.Faction |
                EditKind.XRayStashTransfer;

            if ((editKinds & ~allowedXRay) != EditKind.None)
            {
                return false;
            }

            if ((editKinds & EditKind.Money) != EditKind.None &&
                !CapabilityRegistry.Get(normalized, "edit_money").Writable)
            {
                return false;
            }

            if ((editKinds & EditKind.StackCounts) != EditKind.None &&
                !CapabilityRegistry.Get(normalized, "edit_stacks").Writable)
            {
                return false;
            }

            if ((editKinds & EditKind.Durability) != EditKind.None &&
                !CapabilityRegistry.Get(normalized, "edit_durability").Writable)
            {
                return false;
            }

            if ((editKinds & EditKind.Placement) != EditKind.None &&
                !CapabilityRegistry.Get(normalized, "edit_placement").Writable)
            {
                return false;
            }

            if ((editKinds & EditKind.Upgrades) != EditKind.None &&
                normalized is "stalker-soc" or "stalker-soc-ee")
            {
                return false;
            }

            if ((editKinds & EditKind.Faction) != EditKind.None &&
                (!CapabilityRegistry.Get(normalized, "edit_relations").Writable ||
                 !CapabilityRegistry.Get(normalized, "edit_player_faction").Writable))
            {
                return false;
            }

            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
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

        var detectedRelease = releaseId;
        if (string.IsNullOrWhiteSpace(detectedRelease))
        {
            detectedRelease = DetectFormat(source)
                ?? throw new InvalidOperationException("Could not detect save format for editing.");
        }

        if (IsXRayRelease(detectedRelease))
        {
            return XRayEditWriter.Prepare(source, plan, catalogs);
        }

        if (IsStalker2Release(detectedRelease))
        {
            return Stalker2EditWriter.Prepare(source, plan);
        }

        throw new NotSupportedException($"Unsupported release for editing: '{detectedRelease}'.");
    }

    /// <summary>
    /// Verifies that data read back from a newly written save matches expectations and the edit plan.
    /// </summary>
    public static void VerifyReadBack(ReadOnlySpan<byte> data, string expectedReleaseId, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedReleaseId);

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

    private static string? NormalizeXRayReleaseId(string releaseId)
    {
        var lower = releaseId.Trim().ToLowerInvariant();
        return lower switch
        {
            "stalker-soc" or "soc" => "stalker-soc",
            "stalker-soc-ee" => "stalker-soc-ee",
            "stalker-cs" or "clear_sky" or "cs" => "stalker-cs",
            "stalker-cs-ee" => "stalker-cs-ee",
            "stalker-cop" or "cop" => "stalker-cop",
            "stalker-cop-ee" => "stalker-cop-ee",
            _ => null,
        };
    }
}
