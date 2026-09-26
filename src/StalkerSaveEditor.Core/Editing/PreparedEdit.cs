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

    public EditPlan Plan { get; }

    public string SourceSha256 => Plan.SourceSha256;

    public string OutputSha256 { get; }

    public ReadOnlyMemory<byte> Data => _data;
}
