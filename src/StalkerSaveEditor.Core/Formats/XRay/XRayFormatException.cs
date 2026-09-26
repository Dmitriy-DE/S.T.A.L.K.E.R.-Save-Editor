namespace StalkerSaveEditor.Core.Formats.XRay;

public sealed class XRayFormatException(string message, Exception? innerException = null)
    : FormatException(message, innerException);
