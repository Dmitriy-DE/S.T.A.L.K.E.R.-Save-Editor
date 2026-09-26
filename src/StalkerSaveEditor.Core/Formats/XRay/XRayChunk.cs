namespace StalkerSaveEditor.Core.Formats.XRay;

public sealed record XRayChunk(
    uint Type,
    int Offset,
    int Size,
    ReadOnlyMemory<byte> Data)
{
    public int End => checked(Offset + sizeof(uint) * 2 + Size);
}
