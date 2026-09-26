using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayEditWriter
{
    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Money is null && plan.StackCounts.Count == 0)
        {
            throw new XRayFormatException("X-Ray edit: EditPlan must include money or stack changes.");
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
