namespace StalkerSaveEditor.Core.Diagnostics;

public sealed record QuestTaskState(string TaskId, string State, string? PreventingFixId = null);

public sealed record QuestDoctorReport(
    SaveDoctorStatus Status,
    string? FormatId,
    bool QuestStatesAvailable,
    string Summary,
    IReadOnlyList<QuestTaskState> States);

/// <summary>Exposes only quest/task state already established by a supported save reader.</summary>
public static class QuestDoctor
{
    public static QuestDoctorReport Analyze(ReadOnlySpan<byte> data)
    {
        var save = SaveDoctor.Analyze(data);
        if (save.Overview is { } overview)
        {
            return new QuestDoctorReport(
                SaveDoctorStatus.Unknown,
                overview.FormatId,
                QuestStatesAvailable: false,
                "The supported save reader does not expose validated quest or task state fields; no states or preventing fixes can be inferred.",
                []);
        }

        return new QuestDoctorReport(
            save.Status,
            FormatId: null,
            QuestStatesAvailable: false,
            "The save must parse before quest or task state fields can be inspected.",
            []);
    }
}
