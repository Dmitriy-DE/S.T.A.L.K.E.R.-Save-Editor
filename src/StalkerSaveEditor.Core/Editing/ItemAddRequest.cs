namespace StalkerSaveEditor.Core.Editing;

public sealed record ItemAddRequest
{
    public ItemAddRequest(string itemKey, uint quantity = 1, string destination = "inventory")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (itemKey.Contains('\0'))
        {
            throw new ArgumentException("Item key must not contain NUL.", nameof(itemKey));
        }

        if (quantity is 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Item quantity must be in the range 1…65535.");
        }

        ItemKey = itemKey;
        Quantity = quantity;
        Destination = destination.Trim();
    }

    public string ItemKey { get; }

    public uint Quantity { get; }

    public string Destination { get; }
}
