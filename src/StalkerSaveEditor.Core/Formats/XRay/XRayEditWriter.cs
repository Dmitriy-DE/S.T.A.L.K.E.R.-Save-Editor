using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayEditWriter
{
    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var hasMoneyOrStacks = plan.Money is not null || plan.StackCounts.Count > 0;
        var hasStashMoves = plan.StashTakes.Count > 0 || plan.StashPuts.Count > 0;
        if (plan.DetachHandles.Count > 0 || plan.Adds.Count > 0)
        {
            throw new XRayFormatException(
                "X-Ray edit: EditPlan cannot mix add/delete operations with money, stacks, or stash transfers.");
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

        if (plan.Money is { } money && plan.StackCounts.Count > 0)
        {
            var moneyPlan = new EditPlan(plan.SourceSha256, money: money);
            var moneyEdit = XRayMoneyWriter.Prepare(source, moneyPlan);
            var stackPlan = new EditPlan(
                Sha256(moneyEdit.Data.Span),
                stackCounts: plan.StackCounts);
            var stackEdit = XRayStackWriter.Prepare(moneyEdit.Data.Span, stackPlan);
            return new PreparedEdit(plan, stackEdit.Data.Span);
        }

        return plan.Money is not null
            ? XRayMoneyWriter.Prepare(source, new EditPlan(plan.SourceSha256, money: plan.Money))
            : XRayStackWriter.Prepare(source, new EditPlan(plan.SourceSha256, stackCounts: plan.StackCounts));
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
