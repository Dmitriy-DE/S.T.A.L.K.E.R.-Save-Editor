using System.Text.RegularExpressions;

namespace StalkerSaveEditor.Core.Editing;

public sealed record EditPlan
{
    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    public EditPlan(string sourceSha256, uint? money = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        if (!Sha256Pattern.IsMatch(sourceSha256))
        {
            throw new ArgumentException("Source SHA256 must contain 64 hexadecimal characters.", nameof(sourceSha256));
        }

        SourceSha256 = sourceSha256.ToLowerInvariant();
        Money = money;
    }

    public string SourceSha256 { get; }

    public uint? Money { get; }
}
