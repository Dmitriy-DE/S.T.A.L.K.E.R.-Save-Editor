using System.Security.Cryptography;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayEditWriter
{
    public static PreparedEdit Prepare(
        ReadOnlySpan<byte> source,
        EditPlan plan,
        CatalogBundle? catalogs = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var editKinds = plan.EditKinds;
        if ((editKinds & (EditKind.Delete | EditKind.Add)) != EditKind.None)
        {
            throw new XRayFormatException(
                "X-Ray edit: EditPlan cannot mix add/delete operations with money, stacks, durability, placement, or stash transfers.");
        }

        const EditKind supportedKinds =
            EditKind.Money |
            EditKind.StackCounts |
            EditKind.XRayStashTransfer |
            EditKind.Upgrades |
            EditKind.Faction |
            EditKind.Durability |
            EditKind.Placement;
        if ((editKinds & ~supportedKinds) != EditKind.None)
        {
            throw new XRayFormatException("X-Ray edit: EditPlan contains an unsupported X-Ray edit kind.");
        }

        if (editKinds == EditKind.None)
        {
            throw new XRayFormatException("X-Ray edit: EditPlan must include a supported X-Ray edit.");
        }

        if (editKinds == EditKind.Upgrades)
        {
            var upgradeCatalog = catalogs?.Upgrades;
            if (upgradeCatalog is null)
            {
                throw new XRayFormatException("X-Ray edit: upgrade edits require a release-matched upgrade catalog.");
            }

            return XRayUpgradeWriter.Prepare(source, plan, upgradeCatalog);
        }

        if (editKinds == EditKind.Faction)
        {
            return XRayFactionWriter.Prepare(source, plan, catalogs?.Factions);
        }

        if (editKinds == EditKind.Durability)
        {
            return XRayDurabilityWriter.Prepare(source, plan);
        }

        if (editKinds == EditKind.Placement)
        {
            return XRayPlacementWriter.Prepare(source, plan);
        }

        if (editKinds == EditKind.Money)
        {
            return XRayMoneyWriter.Prepare(source, plan);
        }

        if (editKinds == EditKind.StackCounts)
        {
            return XRayStackWriter.Prepare(source, plan);
        }

        if (editKinds == EditKind.XRayStashTransfer)
        {
            return XRayStashWriter.Prepare(source, plan);
        }

        // Each stage reads the previous stage's bytes in place and takes its hash from the stage result: the save
        // is not copied and hashed again between stages.
        PreparedEdit? stage = null;
        if ((editKinds & EditKind.Upgrades) != EditKind.None)
        {
            var upgradeCatalog = catalogs?.Upgrades
                ?? throw new XRayFormatException("X-Ray edit: upgrade edits require a release-matched upgrade catalog.");
            stage = XRayUpgradeWriter.Prepare(
                stage is null ? source : stage.Bytes,
                new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, upgrades: plan.Upgrades),
                upgradeCatalog);
        }

        if ((editKinds & EditKind.Money) != EditKind.None)
        {
            stage = XRayMoneyWriter.Prepare(
                stage is null ? source : stage.Bytes,
                new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, money: plan.Money));
        }

        if ((editKinds & EditKind.StackCounts) != EditKind.None)
        {
            stage = XRayStackWriter.Prepare(
                stage is null ? source : stage.Bytes,
                new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, stackCounts: plan.StackCounts));
        }

        if ((editKinds & EditKind.Durability) != EditKind.None)
        {
            stage = XRayDurabilityWriter.Prepare(
                stage is null ? source : stage.Bytes,
                new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, durability: plan.Durability));
        }

        if ((editKinds & EditKind.Placement) != EditKind.None)
        {
            stage = XRayPlacementWriter.Prepare(
                stage is null ? source : stage.Bytes,
                new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, placements: plan.Placements));
        }

        if ((editKinds & EditKind.XRayStashTransfer) != EditKind.None)
        {
            stage = XRayStashWriter.Prepare(
                stage is null ? source : stage.Bytes,
                new EditPlan(
                    stage?.OutputSha256 ?? plan.SourceSha256,
                    stashTakes: plan.StashTakes,
                    stashPuts: plan.StashPuts));
        }

        if ((editKinds & EditKind.Faction) != EditKind.None)
        {
            stage = XRayFactionWriter.Prepare(
                stage is null ? source : stage.Bytes,
                new EditPlan(
                    stage?.OutputSha256 ?? plan.SourceSha256,
                    playerFaction: plan.PlayerFaction,
                    factionRelations: plan.FactionRelations),
                catalogs?.Factions);
        }

        return new PreparedEdit(plan, stage ?? throw new InvalidOperationException("The edit plan produced no stage."));
    }
}
