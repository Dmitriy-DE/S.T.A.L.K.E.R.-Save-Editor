using StalkerSaveEditor.Core.Inspection;
using System.Text.Json.Serialization;

namespace StalkerSaveEditor.Core.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<SaveDoctorStatus>))]
public enum SaveDoctorStatus
{
    Ok,
    Warning,
    Error,
    Unknown,
}

public sealed record SaveDoctorCheck(
    string Id,
    SaveDoctorStatus Status,
    string Summary,
    string Detail = "");

/// <summary>
/// Reports only facts established by the supported save readers. It deliberately does not infer
/// quest reachability, semantic corruption, or a repair from unknown save fields.
/// </summary>
public sealed record SaveDoctorReport(
    SaveDoctorStatus Status,
    SaveOverview? Overview,
    IReadOnlyList<SaveDoctorCheck> Checks);

public static class SaveDoctor
{
    public static SaveDoctorReport Analyze(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return Unsupported("The selected file is empty.");
        }

        try
        {
            var overview = SaveInspector.Inspect(data);
            SaveDoctorCheck[] checks =
            [
                new("structure", SaveDoctorStatus.Ok, "Supported save structure parsed.",
                    $"Format {overview.FormatId}; {overview.ItemCount} parsed inventory record(s)."),
                new("semantic-state", SaveDoctorStatus.Unknown, "Semantic health is not classified.",
                    "No validated quest, object-reference, or progression signatures are registered for this save format."),
                new("repair", SaveDoctorStatus.Unknown, "No save repair is available.",
                    "The toolkit has no evidence-backed repair for a detected issue in this save."),
            ];
            return new SaveDoctorReport(SaveDoctorStatus.Ok, overview, checks);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException or OverflowException)
        {
            return new SaveDoctorReport(SaveDoctorStatus.Error, null,
            [
                new SaveDoctorCheck("structure", SaveDoctorStatus.Error, "Save structure could not be validated.", exception.Message),
                new SaveDoctorCheck("semantic-state", SaveDoctorStatus.Unknown, "Semantic health is unknown.", "The save must parse before semantic checks can run."),
                new SaveDoctorCheck("repair", SaveDoctorStatus.Unknown, "No save was changed.", "Save Doctor is read-only."),
            ]);
        }
    }

    private static SaveDoctorReport Unsupported(string detail) => new(
        SaveDoctorStatus.Error,
        null,
        [
            new SaveDoctorCheck("structure", SaveDoctorStatus.Error, "Save structure could not be validated.", detail),
            new SaveDoctorCheck("semantic-state", SaveDoctorStatus.Unknown, "Semantic health is unknown.", "The save must parse before semantic checks can run."),
            new SaveDoctorCheck("repair", SaveDoctorStatus.Unknown, "No save was changed.", "Save Doctor is read-only."),
        ]);
}
