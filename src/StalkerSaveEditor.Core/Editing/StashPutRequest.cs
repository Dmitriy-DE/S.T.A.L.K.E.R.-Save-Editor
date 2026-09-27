namespace StalkerSaveEditor.Core.Editing;

public sealed record StashPutRequest
{
    public StashPutRequest(ushort objectId, ushort boxId)
    {
        if (objectId is 0 or ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(objectId), "Object id must be in the range 1…65534.");
        }

        if (boxId is 0 or ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(boxId), "Box id must be in the range 1…65534.");
        }

        ObjectId = objectId;
        BoxId = boxId;
    }

    public ushort ObjectId { get; }

    public ushort BoxId { get; }
}
