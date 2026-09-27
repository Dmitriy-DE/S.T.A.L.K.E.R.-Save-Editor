using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayFactionWriter
{
    public static PreparedEdit Prepare(
        ReadOnlySpan<byte> source,
        EditPlan plan,
        FactionCatalog? factionCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.PlayerFaction is null && plan.FactionRelations.Count == 0)
        {
            throw Error("EditPlan must include a player-faction or relation edit.");
        }

        if (plan.EditKinds != EditKind.Faction)
        {
            throw Error("Faction writer accepts only player-faction and relation edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Sha256(sourceBytes);
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = ReadSupported(sourceBytes);
        if (plan.PlayerFaction is not null &&
            !CapabilityRegistry.Get(parsed.FormatId, "edit_player_faction").Writable)
        {
            throw Error($"Player-faction editing is not enabled for {parsed.FormatId}.");
        }

        if (plan.FactionRelations.Count > 0 &&
            !CapabilityRegistry.Get(parsed.FormatId, "edit_relations").Writable)
        {
            throw Error($"Faction-relation editing is not enabled for {parsed.FormatId}.");
        }

        if (parsed.FormatId is not ("stalker-soc" or "stalker-cs" or "stalker-cop"))
        {
            throw Error(
                $"Player-faction and relation writes are not enabled for {parsed.FormatId}; " +
                "the Python capability registry marks this format read-only.");
        }

        factionCatalog ??= CatalogBundleReader.LoadEmbedded()
            .GetValueOrDefault(parsed.FormatId)?.Factions;
        if (factionCatalog is null ||
            !string.Equals(factionCatalog.ReleaseId, parsed.FormatId, StringComparison.Ordinal))
        {
            throw Error($"A matching faction catalog is required for {parsed.FormatId}.");
        }

        if (plan.FactionRelations.Count > 0 &&
            (factionCatalog.GoodwillMin is not int goodwillMin ||
             factionCatalog.GoodwillMax is not int goodwillMax))
        {
            throw Error($"Goodwill limits are not confirmed for {parsed.FormatId}.");
        }

        var working = sourceBytes;
        foreach (var (factionKey, goodwill) in plan.FactionRelations)
        {
            var faction = ResolveFaction(factionCatalog, factionKey);
            var communityId = faction.NumericId
                ?? throw Error($"Faction '{factionKey}' has no confirmed numeric community id.");
            if (goodwill < factionCatalog.GoodwillMin || goodwill > factionCatalog.GoodwillMax)
            {
                throw Error(
                    $"Goodwill for '{factionKey}' must be in the range " +
                    $"{factionCatalog.GoodwillMin}…{factionCatalog.GoodwillMax}.");
            }

            parsed = ReadSupported(working);
            if (!parsed.FactionRelationsEditable || parsed.RelationRegistry is null)
            {
                throw Error("The actor relation row is absent or the relation registry is malformed.");
            }

            var row = parsed.RelationRegistry.ForCharacter(parsed.ActorId)
                ?? throw Error($"Actor 0x{parsed.ActorId:X4} has no relation-registry row.");
            var relationChunk = GetSingleRelationChunk(parsed.Container);
            var updatedRelationData = XRayRelationRegistry.Patch(
                relationChunk.Data.Span,
                row,
                communityId,
                goodwill);
            var updatedRaw = ReplaceChunkData(parsed.Container, relationChunk, updatedRelationData);
            working = parsed.Container.Build(updatedRaw);
        }

        if (plan.PlayerFaction is { } playerFactionKey)
        {
            parsed = ReadSupported(working);
            var faction = ResolveFaction(factionCatalog, playerFactionKey);
            var communityId = faction.NumericId
                ?? throw Error($"Faction '{playerFactionKey}' has no confirmed numeric community id.");
            if (parsed.PlayerFactionOffset is not int factionOffset)
            {
                throw Error("The actor community field is not confirmed for this actor STATE.");
            }

            var raw = parsed.Container.Raw.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(factionOffset), communityId);
            working = parsed.Container.Build(raw);
        }

        var after = ReadSupported(working);
        if (after.FormatId != parsed.FormatId)
        {
            throw Error("Faction write changed the detected X-Ray format.");
        }

        if (plan.PlayerFaction is { } targetPlayerFaction &&
            after.PlayerFactionIndex != ResolveFaction(factionCatalog, targetPlayerFaction).NumericId)
        {
            throw Error("Player-faction write did not pass its round-trip.");
        }

        foreach (var (factionKey, goodwill) in plan.FactionRelations)
        {
            var communityId = ResolveFaction(factionCatalog, factionKey).NumericId;
            if (!after.FactionRelations.Any(
                relation => relation.CommunityIndex == communityId && relation.Value == goodwill))
            {
                throw Error($"Relation write for '{factionKey}' did not pass its round-trip.");
            }
        }

        return new PreparedEdit(plan, working);
    }

    private static FactionDefinition ResolveFaction(FactionCatalog catalog, string key)
    {
        try
        {
            return catalog.Resolve(key);
        }
        catch (CatalogLookupException exception)
        {
            throw Error(exception.Message, exception);
        }
    }

    private static XRayTrilogySave ReadSupported(ReadOnlySpan<byte> data)
    {
        try
        {
            return XRayTrilogyReader.FromBytes(data);
        }
        catch (XRayFormatException originalFailure)
        {
            try
            {
                return XRayEnhancedReader.FromBytes(data);
            }
            catch (XRayFormatException)
            {
                throw originalFailure;
            }
        }
    }

    private static XRayChunk GetSingleRelationChunk(XRayContainer container)
    {
        XRayChunk? found = null;
        foreach (var chunk in container.Chunks)
        {
            if (chunk.Type != 9)
            {
                continue;
            }

            if (found is not null)
            {
                throw Error("Relation chunk 9 occurs more than once.");
            }

            found = chunk;
        }

        return found ?? throw Error("Relation chunk 9 is missing.");
    }

    private static byte[] ReplaceChunkData(
        XRayContainer container,
        XRayChunk chunk,
        ReadOnlySpan<byte> replacement)
    {
        var original = container.Raw.Span;
        var outputLength = checked(original.Length - chunk.Size + replacement.Length);
        var output = GC.AllocateUninitializedArray<byte>(outputLength);
        original[..chunk.Offset].CopyTo(output);
        var destination = chunk.Offset;
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destination), chunk.Type);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destination + sizeof(uint)), checked((uint)replacement.Length));
        replacement.CopyTo(output.AsSpan(destination + sizeof(uint) * 2));
        destination += sizeof(uint) * 2 + replacement.Length;
        original[chunk.End..].CopyTo(output.AsSpan(destination));
        return output;
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static XRayFormatException Error(string message, Exception? innerException = null) =>
        new($"X-Ray faction edit: {message}", innerException);
}
