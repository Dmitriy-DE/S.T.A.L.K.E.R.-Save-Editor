using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

/// <summary>
/// Centralized S.T.A.L.K.E.R. 2 edit dispatcher, analogous to XRayEditWriter.
/// Dispatches EditPlan operations (money, stack counts, durability, stash transfers)
/// to format-specific S2 writers and handles multi-kind edit sequencing.
/// </summary>
public static class Stalker2EditWriter
{
    private const EditKind SupportedKinds =
        EditKind.Money |
        EditKind.StackCounts |
        EditKind.Durability |
        EditKind.Stalker2StashTransfer;

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var editKinds = plan.EditKinds;

        if (editKinds == EditKind.None)
        {
            throw Error("EditPlan must include at least one supported S2 edit.");
        }

        if ((editKinds & ~SupportedKinds) != EditKind.None)
        {
            throw Error($"EditPlan contains unsupported edit kinds for S.T.A.L.K.E.R. 2: {editKinds & ~SupportedKinds}.");
        }

        // Fast paths for single edit kinds
        if (editKinds == EditKind.Money)
        {
            return Stalker2MoneyWriter.Prepare(source, plan);
        }

        if (editKinds == EditKind.StackCounts)
        {
            return Stalker2StackWriter.Prepare(source, plan);
        }

        if (editKinds == EditKind.Stalker2StashTransfer)
        {
            return Stalker2StashWriter.Prepare(source, plan);
        }

        // Stalker2DurabilityWriter already supports Money + StackCounts + Durability in one pass
        if ((editKinds & EditKind.Durability) != EditKind.None &&
            (editKinds & EditKind.Stalker2StashTransfer) == EditKind.None)
        {
            return Stalker2DurabilityWriter.Prepare(source, plan);
        }

        // Composite edits pipeline
        // The first specialized writer verifies SourceSha256. Keep the input copy only for
        // multi-stage edits, where each stage produces the next stage's source.
        // Each stage reads the previous stage's bytes in place and takes its hash from the stage result: the save
        // is not copied and hashed again between stages.
        PreparedEdit? stage = null;

        if ((editKinds & EditKind.Stalker2StashTransfer) != EditKind.None)
        {
            var stashPlan = new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, stalker2StashTakeHandle: plan.Stalker2StashTakeHandle);
            stage = Stalker2StashWriter.Prepare(stage is null ? source : stage.Bytes, stashPlan);
        }

        if ((editKinds & EditKind.Durability) != EditKind.None)
        {
            var durPlan = new EditPlan(
                (stage?.OutputSha256 ?? plan.SourceSha256),
                money: plan.Money,
                stackCounts: plan.StackCounts,
                durability: plan.Durability);
            stage = Stalker2DurabilityWriter.Prepare(stage is null ? source : stage.Bytes, durPlan);
        }
        else
        {
            if ((editKinds & EditKind.Money) != EditKind.None)
            {
                var moneyPlan = new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, money: plan.Money);
                stage = Stalker2MoneyWriter.Prepare(stage is null ? source : stage.Bytes, moneyPlan);
            }

            if ((editKinds & EditKind.StackCounts) != EditKind.None)
            {
                var stackPlan = new EditPlan(stage?.OutputSha256 ?? plan.SourceSha256, stackCounts: plan.StackCounts);
                stage = Stalker2StackWriter.Prepare(stage is null ? source : stage.Bytes, stackPlan);
            }
        }

        return new PreparedEdit(plan, stage ?? throw new InvalidOperationException("The edit plan produced no stage."));
    }


    private static Stalker2FormatException Error(string message) =>
        new($"S.T.A.L.K.E.R. 2 edit: {message}");
}
