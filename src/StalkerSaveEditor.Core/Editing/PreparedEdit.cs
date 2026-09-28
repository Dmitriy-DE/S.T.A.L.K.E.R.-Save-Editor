using System.Security.Cryptography;

namespace StalkerSaveEditor.Core.Editing;

public sealed class PreparedEdit
{
    private readonly byte[] _data;

    internal PreparedEdit(EditPlan plan, ReadOnlySpan<byte> data)
    {
        Plan = plan;
        _data = data.ToArray();
        OutputSha256 = Convert.ToHexString(SHA256.HashData(_data)).ToLowerInvariant();
    }

    /// <summary>
    /// Bytes that replace a file as a whole (a local save sent to Steam Cloud, a cloud copy taken
    /// locally): no edits, only the hash of the file being replaced for the stale-source check.
    /// </summary>
    public static PreparedEdit Replacing(string replacedSha256, ReadOnlySpan<byte> data) => new(new EditPlan(replacedSha256), data);

    public EditPlan Plan { get; }

    public string SourceSha256 => Plan.SourceSha256;

    public string OutputSha256 { get; }

    public ReadOnlyMemory<byte> Data => _data;
}
