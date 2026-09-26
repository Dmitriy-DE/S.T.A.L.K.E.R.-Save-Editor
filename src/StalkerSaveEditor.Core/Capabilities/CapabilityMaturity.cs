namespace StalkerSaveEditor.Core.Capabilities;

public enum CapabilityMaturity
{
    Unsupported,
    Research,
    Experimental,
    Verified,
}

public sealed record CapabilitySupport(CapabilityMaturity Maturity, string? Reason = null)
{
    public bool Writable => Maturity is CapabilityMaturity.Experimental or CapabilityMaturity.Verified;
}
