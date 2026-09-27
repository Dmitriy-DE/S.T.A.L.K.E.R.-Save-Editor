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
                "X-Ray edit: EditPlan cannot mix add/delete operations with money, stacks, or stash transfers.");
        }

        const EditKind supportedKinds =
            EditKind.Money |
            EditKind.StackCounts |
            EditKind.XRayStashTransfer |
            EditKind.Upgrades |
            EditKind.Faction;
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

        var working = source.ToArray();
        var currentSha256 = plan.SourceSha256;
        if ((editKinds & EditKind.Upgrades) != EditKind.None)
        {
            var upgradeCatalog = catalogs?.Upgrades
                ?? throw new XRayFormatException("X-Ray edit: upgrade edits require a release-matched upgrade catalog.");
            working = XRayUpgradeWriter.Prepare(
                working,
                new EditPlan(currentSha256, upgrades: plan.Upgrades),
                upgradeCatalog).Data.ToArray();
            currentSha256 = Sha256(working);
        }

        if ((editKinds & EditKind.Money) != EditKind.None)
        {
            working = XRayMoneyWriter.Prepare(
                working,
                new EditPlan(currentSha256, money: plan.Money)).Data.ToArray();
            currentSha256 = Sha256(working);
        }

        if ((editKinds & EditKind.StackCounts) != EditKind.None)
        {
            working = XRayStackWriter.Prepare(
                working,
                new EditPlan(currentSha256, stackCounts: plan.StackCounts)).Data.ToArray();
            currentSha256 = Sha256(working);
        }

        if ((editKinds & EditKind.XRayStashTransfer) != EditKind.None)
        {
            working = XRayStashWriter.Prepare(
                working,
                new EditPlan(
                    currentSha256,
                    stashTakes: plan.StashTakes,
                    stashPuts: plan.StashPuts)).Data.ToArray();
            currentSha256 = Sha256(working);
        }

        if ((editKinds & EditKind.Faction) != EditKind.None)
        {
            working = XRayFactionWriter.Prepare(
                working,
                new EditPlan(
                    currentSha256,
                    playerFaction: plan.PlayerFaction,
                    factionRelations: plan.FactionRelations),
                catalogs?.Factions).Data.ToArray();
        }

        return new PreparedEdit(plan, working);
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
