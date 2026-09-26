using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Core.Formats.Enhanced;

public static class XRayEnhancedReader
{
    public static XRayTrilogySave FromBytes(ReadOnlySpan<byte> data) =>
        XRayTrilogyReader.FromEnhancedBytes(data);
}
