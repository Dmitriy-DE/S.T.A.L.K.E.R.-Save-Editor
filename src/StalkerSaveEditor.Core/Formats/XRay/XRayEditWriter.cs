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
            EditKind.Money | EditKind.StackCounts | EditKind.XRayStashTransfer | EditKind.Upgrades;
        if ((editKinds & ~supportedKinds) != EditKind.None)
        {
            throw new XRayFormatException("X-Ray edit: EditPlan contains an unsupported X-Ray edit kind.");
        }

        var hasMoneyOrStacks = (editKinds & (EditKind.Money | EditKind.StackCounts)) != EditKind.None;
        var hasStashMoves = (editKinds & EditKind.XRayStashTransfer) != EditKind.None;
        if ((editKinds & EditKind.Upgrades) != EditKind.None)
        {
            var upgradeCatalog = catalogs?.Upgrades;
            if (upgradeCatalog is null)
            {
                throw new XRayFormatException("X-Ray edit: upgrade edits require a release-matched upgrade catalog.");
            }

            var upgradesOnlyPlan = new EditPlan(plan.SourceSha256, upgrades: plan.Upgrades);
            var upgradesEdit = XRayUpgradeWriter.Prepare(source, upgradesOnlyPlan, upgradeCatalog);
            if (!hasMoneyOrStacks && !hasStashMoves)
            {
                return new PreparedEdit(plan, upgradesEdit.Data.Span);
            }

            var remainingPlan = new EditPlan(
                Sha256(upgradesEdit.Data.Span),
                money: plan.Money,
                stackCounts: plan.StackCounts,
                stashTakes: plan.StashTakes,
                stashPuts: plan.StashPuts);
            var remainingEdit = Prepare(upgradesEdit.Data.Span, remainingPlan);
            return new PreparedEdit(plan, remainingEdit.Data.Span);
        }

        if (!hasMoneyOrStacks && !hasStashMoves)
        {
            throw new XRayFormatException("X-Ray edit: EditPlan must include a supported X-Ray edit.");
        }

        if (hasStashMoves && !hasMoneyOrStacks)
        {
            return XRayStashWriter.Prepare(source, plan);
        }

        if (hasStashMoves)
        {
            var numericPlan = new EditPlan(
                plan.SourceSha256,
                money: plan.Money,
                stackCounts: plan.StackCounts);
            var numericEdit = Prepare(source, numericPlan);
            var stashPlan = new EditPlan(
                Sha256(numericEdit.Data.Span),
                stashTakes: plan.StashTakes,
                stashPuts: plan.StashPuts);
            var stashEdit = XRayStashWriter.Prepare(numericEdit.Data.Span, stashPlan);
            return new PreparedEdit(plan, stashEdit.Data.Span);
        }

        if ((editKinds & (EditKind.Money | EditKind.StackCounts)) ==
            (EditKind.Money | EditKind.StackCounts))
        {
            var moneyPlan = new EditPlan(plan.SourceSha256, money: plan.Money);
            var moneyEdit = XRayMoneyWriter.Prepare(source, moneyPlan);
            var stackPlan = new EditPlan(
                Sha256(moneyEdit.Data.Span),
                stackCounts: plan.StackCounts);
            var stackEdit = XRayStackWriter.Prepare(moneyEdit.Data.Span, stackPlan);
            return new PreparedEdit(plan, stackEdit.Data.Span);
        }

        return (editKinds & EditKind.Money) != EditKind.None
            ? XRayMoneyWriter.Prepare(source, new EditPlan(plan.SourceSha256, money: plan.Money))
            : XRayStackWriter.Prepare(source, new EditPlan(plan.SourceSha256, stackCounts: plan.StackCounts));
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
