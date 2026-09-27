using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Core.Capabilities;

public static class CapabilityRegistry
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySupport>> Releases =
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySupport>>(
            new Dictionary<string, IReadOnlyDictionary<string, CapabilitySupport>>(StringComparer.Ordinal)
            {
                ["stalker-soc"] = Create(
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental),
                ["stalker-cs"] = Create(
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental),
                ["stalker-cop"] = Create(
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Verified,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental),
                ["stalker-soc-ee"] = Create(
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental),
                ["stalker-cs-ee"] = Create(
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental),
                ["stalker-cop-ee"] = Create(
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental),
                ["stalker2"] = Create(
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Experimental,
                    CapabilityMaturity.Unsupported,
                    CapabilityMaturity.Unsupported,
                    includeMoveItems: true),
            });

    public static CapabilitySupport Get(string releaseId, string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        if (!Releases.TryGetValue(releaseId, out var capabilities))
        {
            throw new KeyNotFoundException($"Unknown release capability registry: {releaseId}");
        }

        if (!capabilities.TryGetValue(capability, out var support))
        {
            throw new KeyNotFoundException($"Unknown capability '{capability}' for {releaseId}.");
        }

        return support;
    }

    private static IReadOnlyDictionary<string, CapabilitySupport> Create(
        CapabilityMaturity money,
        CapabilityMaturity stacks,
        CapabilityMaturity addItems,
        CapabilityMaturity removeItems,
        CapabilityMaturity relations = CapabilityMaturity.Unsupported,
        CapabilityMaturity playerFaction = CapabilityMaturity.Unsupported,
        bool includeMoveItems = false)
    {
        var capabilities = new Dictionary<string, CapabilitySupport>(StringComparer.Ordinal)
        {
            ["edit_money"] = new(money),
            ["edit_stacks"] = new(stacks),
            ["add_items"] = new(addItems),
            ["remove_items"] = new(removeItems),
            ["edit_relations"] = new(relations),
            ["edit_player_faction"] = new(playerFaction),
        };
        if (includeMoveItems)
        {
            capabilities["move_items"] = new(CapabilityMaturity.Unsupported);
        }

        return new ReadOnlyDictionary<string, CapabilitySupport>(capabilities);
    }
}
