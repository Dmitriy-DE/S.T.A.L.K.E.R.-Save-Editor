using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayAddWriter
{
    private const uint MaximumObjects = 1_000_000;
    private const int ClientPlaceOffset = 1;
    private const int SlotCount = 14;

    private static readonly HashSet<string> SupportedFamilies = new(StringComparer.Ordinal)
    {
        "ammo",
        "base",
        "detector",
        "outfit",
        "pda",
        "document",
        "torch",
        "weapon",
        "weapon_magazined",
        "weapon_shotgun",
        "weapon_wgl",
    };

    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, EditPlan plan, ItemCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);
        if (plan.Adds.Count == 0)
        {
            throw Error("EditPlan must include at least one item to add.");
        }

        if (plan.Money is not null || plan.StackCounts.Count > 0 || plan.DetachHandles.Count > 0 || plan.Upgrades.Count > 0 ||
            plan.StashTakes.Count > 0 || plan.StashPuts.Count > 0)
        {
            throw Error("Add-only writer does not accept money, stack, delete, or stash-transfer edits.");
        }

        var sourceBytes = source.ToArray();
        var sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (!string.Equals(sourceSha256, plan.SourceSha256, StringComparison.Ordinal))
        {
            throw Error("Source SHA256 does not match the edit plan.");
        }

        var parsed = ReadSupported(sourceBytes);
        var formatId = parsed.FormatId;
        if (!CapabilityRegistry.Get(formatId, "add_items").Writable)
        {
            throw Error($"Item addition is not enabled for {formatId}.");
        }

        if (!CatalogMatches(formatId, catalog.ReleaseId))
        {
            throw Error($"Item catalog '{catalog.ReleaseId}' does not match {formatId}.");
        }

        var originalIds = parsed.RegistryObjects.Select(item => item.ObjectId).ToHashSet();
        var additions = new List<AddedObject>();
        var working = sourceBytes;
        foreach (var request in plan.Adds)
        {
            var current = ReadSupported(working);
            EnsureFormat(current, formatId);
            var parentId = ResolveDestinationParent(current, request.Destination);
            var definition = catalog.Resolve(request.ItemKey)
                ?? throw Error($"Item key '{request.ItemKey}' is absent from the item catalog.");
            var family = DefinitionFamily(definition)
                ?? throw Error($"Item key '{request.ItemKey}' has no confirmed serializer family.");
            if (!SupportedFamilies.Contains(family))
            {
                throw Error($"Serializer family '{family}' is not confirmed.");
            }

            if (family == "ammo" && definition.MaxStack is { } maxStack && request.Quantity > maxStack)
            {
                throw Error($"Ammo quantity {request.Quantity} exceeds catalog max_stack {maxStack}.");
            }

            var template = FindTemplate(current, catalog, family)
                ?? throw Error($"No registry template exists for serializer family '{family}'.");
            var copies = family == "ammo" ? 1U : request.Quantity;
            if ((ulong)current.RegistryObjects.Count + copies > MaximumObjects)
            {
                throw Error("OBJECT registry would exceed the supported object limit.");
            }

            for (var copyIndex = 0U; copyIndex < copies; copyIndex++)
            {
                current = ReadSupported(working);
                EnsureFormat(current, formatId);
                template = current.RegistryObjects.FirstOrDefault(item => item.ObjectId == template.ObjectId)
                    ?? throw Error("The selected registry template disappeared during the add operation.");

                var objectId = AllocateObjectId(current.RegistryObjects);
                var record = CloneRecord(current.Container.Raw.Span, template, request.ItemKey, objectId, parentId);
                working = AppendRecord(current.Container, record);

                current = ReadSupported(working);
                EnsureFormat(current, formatId);
                var added = current.RegistryObjects.FirstOrDefault(item => item.ObjectId == objectId)
                    ?? throw Error($"New object 0x{objectId:X4} is missing after append.");
                var resetRecord = ResetAddedState(current.Container.Raw.Span, added);
                if (!resetRecord.AsSpan().SequenceEqual(
                        current.Container.Raw.Span.Slice(added.RecordOffset, added.RecordLength)))
                {
                    working = ReplaceRecord(current.Container, added, resetRecord);
                }

                if (family == "ammo")
                {
                    current = ReadSupported(working);
                    EnsureFormat(current, formatId);
                    added = current.RegistryObjects.First(item => item.ObjectId == objectId);
                    working = SetAmmoCount(current, added, request.Quantity);
                }

                additions.Add(new AddedObject(objectId, parentId, request.ItemKey, request.Quantity, family));
            }
        }

        var roundTrip = ReadSupported(working);
        EnsureFormat(roundTrip, formatId);
        var finalRaw = roundTrip.Container.Raw.Span;
        foreach (var addition in additions)
        {
            if (originalIds.Contains(addition.ObjectId))
            {
                throw Error($"New object id 0x{addition.ObjectId:X4} collides with the source registry.");
            }

            var added = roundTrip.RegistryObjects.FirstOrDefault(item => item.ObjectId == addition.ObjectId);
            if (added is null || added.Name != addition.ItemKey || added.ParentId != addition.ParentId)
            {
                throw Error($"Added item '{addition.ItemKey}' did not pass its registry round-trip.");
            }

            if (added.Version > 123 &&
                (!XRayTrilogyReader.TryReadUpgrades(
                     finalRaw,
                     added.Version,
                     added.StateOffset,
                     added.StateLength,
                     out var upgrades,
                     out _,
                     out _) ||
                 upgrades.Length > 0))
            {
                throw Error($"Added item 0x{addition.ObjectId:X4} retained template upgrades.");
            }

            if (addition.Family == "ammo" &&
                (!XRayTrilogyReader.TryReadAmmoCount(
                     finalRaw,
                     added.Version,
                     added.StateOffset,
                     added.StateLength,
                     added.UpdateOffset,
                     added.UpdateLength,
                     out var ammoCount,
                     out _,
                     out var updateOffset) ||
                 ammoCount != addition.Quantity ||
                 BinaryPrimitives.ReadUInt16LittleEndian(finalRaw[updateOffset..]) != addition.Quantity))
            {
                throw Error($"Added ammo 0x{addition.ObjectId:X4} did not preserve both requested counts.");
            }
        }

        return new PreparedEdit(plan, working);
    }

    private static string? DefinitionFamily(ItemDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.SerializationFamily)
            ? InferKnownFamily(definition.Key)
            : definition.SerializationFamily;

    private static ushort ResolveDestinationParent(XRayTrilogySave save, string destination)
    {
        if (string.Equals(destination, "inventory", StringComparison.Ordinal))
        {
            return save.ActorId;
        }

        const string stashPrefix = "stash:";
        if (!destination.StartsWith(stashPrefix, StringComparison.Ordinal) ||
            !ushort.TryParse(
                destination.AsSpan(stashPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var boxId))
        {
            throw Error($"Destination '{destination}' must be 'inventory' or 'stash:<box_id>'.");
        }

        var box = save.RegistryObjects.FirstOrDefault(item => item.ObjectId == boxId);
        if (box is null || !string.Equals(box.Name, "inventory_box", StringComparison.Ordinal))
        {
            throw Error($"Destination stash box 0x{boxId:X4} is missing or is not an inventory_box.");
        }

        return box.ObjectId;
    }

    private static string ObjectFamily(XRayRegistryObject item, ItemCatalog catalog)
    {
        var definition = catalog.Resolve(item.Name);
        return definition is null
            ? InferKnownFamily(item.Name) ?? "base"
            : DefinitionFamily(definition) ?? "base";
    }

    private static string? InferKnownFamily(string name)
    {
        var key = name.ToLowerInvariant();
        if (key.StartsWith("ammo_", StringComparison.Ordinal)) return "ammo";
        if (key == "device_torch") return "torch";
        if (key == "device_pda") return "pda";
        if (key.StartsWith("detector_", StringComparison.Ordinal) ||
            key.StartsWith("device_detector", StringComparison.Ordinal)) return "detector";
        if (key.StartsWith("outfit_", StringComparison.Ordinal) ||
            key.StartsWith("scientific_", StringComparison.Ordinal) ||
            key.StartsWith("helm_", StringComparison.Ordinal) ||
            key.StartsWith("armor_", StringComparison.Ordinal) ||
            key.EndsWith("_outfit", StringComparison.Ordinal) ||
            key.EndsWith("_helmet", StringComparison.Ordinal) ||
            key.EndsWith("_helm", StringComparison.Ordinal) ||
            key.EndsWith("_armor", StringComparison.Ordinal)) return "outfit";

        if (key.StartsWith("wpn_", StringComparison.Ordinal) || key.StartsWith("weapon_", StringComparison.Ordinal))
        {
            if (key.EndsWith("_knife", StringComparison.Ordinal)) return "weapon";
            if (key is "wpn_bm16" or "wpn_rg6" or "wpn_shotgun" or "wpn_spas12" or "wpn_toz34")
            {
                return "weapon_shotgun";
            }

            if (key is "wpn_ak74" or "wpn_fn2000" or "wpn_groza") return "weapon_wgl";
            return "weapon_magazined";
        }

        return null;
    }

    private static XRayRegistryObject? FindTemplate(XRayTrilogySave save, ItemCatalog catalog, string family)
    {
        var candidates = save.RegistryObjects.Where(item => item.ObjectId != save.ActorId).ToArray();
        return candidates.FirstOrDefault(item => item.ParentId == save.ActorId &&
                   ObjectFamily(item, catalog) == family)
            ?? candidates.FirstOrDefault(item => item.ParentId != save.ActorId &&
                   ObjectFamily(item, catalog) == family);
    }

    private static byte[] CloneRecord(
        ReadOnlySpan<byte> raw,
        XRayRegistryObject template,
        string itemKey,
        ushort objectId,
        ushort actorId)
    {
        var spawnOffset = checked(template.RecordOffset + sizeof(ushort));
        var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(raw[template.RecordOffset..]);
        var spawn = PatchSpawnIdentity(raw.Slice(spawnOffset, spawnLength), itemKey, objectId, actorId);
        var update = raw.Slice(template.UpdateOffset, template.UpdateLength);
        return FrameRecord(spawn, update);
    }

    internal static byte[] PatchSpawnIdentity(ReadOnlySpan<byte> spawn, string name, ushort objectId, ushort parentId)
    {
        if (name.Contains('\0'))
        {
            throw Error("Serialized item key must not contain NUL.");
        }

        var (nameStart, nameEnd, objectIdOffset, parentIdOffset) = ReadIdentityOffsets(spawn);
        var replacement = Encoding.UTF8.GetBytes(name + "\0");
        var delta = replacement.Length - (nameEnd - nameStart);
        var rewritten = GC.AllocateUninitializedArray<byte>(checked(spawn.Length + delta));
        spawn[..nameStart].CopyTo(rewritten);
        replacement.CopyTo(rewritten.AsSpan(nameStart));
        spawn[nameEnd..].CopyTo(rewritten.AsSpan(nameStart + replacement.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(rewritten.AsSpan(objectIdOffset + delta), objectId);
        BinaryPrimitives.WriteUInt16LittleEndian(rewritten.AsSpan(parentIdOffset + delta), parentId);
        return rewritten;
    }

    private static (int NameStart, int NameEnd, int ObjectIdOffset, int ParentIdOffset) ReadIdentityOffsets(
        ReadOnlySpan<byte> spawn)
    {
        if (spawn.Length < sizeof(ushort) ||
            BinaryPrimitives.ReadUInt16LittleEndian(spawn) != 1)
        {
            throw Error("Registry template SPAWN does not begin with M_SPAWN.");
        }

        var position = sizeof(ushort);
        var nameStart = position;
        SkipZeroTerminatedString(spawn, ref position);
        var nameEnd = position;
        SkipZeroTerminatedString(spawn, ref position);
        const int identityPrefixLength = 2 + (6 * sizeof(float)) + sizeof(ushort);
        EnsureRange(spawn.Length, position, identityPrefixLength + (2 * sizeof(ushort)));
        position += identityPrefixLength;
        return (nameStart, nameEnd, position, position + sizeof(ushort));
    }

    private static void SkipZeroTerminatedString(ReadOnlySpan<byte> bytes, ref int position)
    {
        var terminator = bytes[position..].IndexOf((byte)0);
        if (terminator < 0)
        {
            throw Error("Registry template SPAWN contains an unterminated string.");
        }

        position = checked(position + terminator + 1);
    }

    internal static byte[] FrameRecord(ReadOnlySpan<byte> spawn, ReadOnlySpan<byte> update)
    {
        if (spawn.Length > ushort.MaxValue || update.Length > ushort.MaxValue)
        {
            throw Error("New object SPAWN or UPDATE packet exceeds the u16 framing limit.");
        }

        var record = GC.AllocateUninitializedArray<byte>(
            checked((2 * sizeof(ushort)) + spawn.Length + update.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(record, checked((ushort)spawn.Length));
        spawn.CopyTo(record.AsSpan(sizeof(ushort)));
        var updateSizeOffset = sizeof(ushort) + spawn.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(updateSizeOffset), checked((ushort)update.Length));
        update.CopyTo(record.AsSpan(updateSizeOffset + sizeof(ushort)));
        return record;
    }

    internal static byte[] ResetAddedState(
        ReadOnlySpan<byte> raw,
        XRayRegistryObject item,
        bool keepUpgrades = false)
    {
        var spawnOffset = checked(item.RecordOffset + sizeof(ushort));
        var spawnLength = BinaryPrimitives.ReadUInt16LittleEndian(raw[item.RecordOffset..]);
        var spawn = raw.Slice(spawnOffset, spawnLength).ToArray();
        var changed = false;

        if (item.Version > 123 && !keepUpgrades)
        {
            if (!XRayTrilogyReader.TryReadUpgrades(
                    raw,
                    item.Version,
                    item.StateOffset,
                    item.StateLength,
                    out var upgrades,
                    out var upgradesOffset,
                    out var upgradesLength))
            {
                throw Error($"Object 0x{item.ObjectId:X4} has no confirmed upgrade-vector boundary.");
            }

            if (upgrades.Length > 0)
            {
                var vectorOffset = upgradesOffset - spawnOffset;
                var stateStart = item.StateOffset - spawnOffset;
                EnsureRange(spawn.Length, vectorOffset, upgradesLength);
                EnsureRange(spawn.Length, stateStart - sizeof(ushort), sizeof(ushort));
                var delta = sizeof(uint) - upgradesLength;
                var resized = GC.AllocateUninitializedArray<byte>(checked(spawn.Length + delta));
                spawn.AsSpan(0, vectorOffset).CopyTo(resized);
                BinaryPrimitives.WriteUInt32LittleEndian(resized.AsSpan(vectorOffset), 0);
                spawn.AsSpan(vectorOffset + upgradesLength).CopyTo(resized.AsSpan(vectorOffset + sizeof(uint)));
                var stateSize = checked(item.StateLength + sizeof(ushort) + delta);
                if (stateSize is < 2 or > ushort.MaxValue)
                {
                    throw Error($"Object 0x{item.ObjectId:X4} STATE size exceeds the u16 boundary.");
                }

                BinaryPrimitives.WriteUInt16LittleEndian(
                    resized.AsSpan(stateStart - sizeof(ushort)),
                    checked((ushort)stateSize));
                spawn = resized;
                changed = true;
            }
        }

        if (item.ClientDataOffset is { } clientOffset && item.ClientDataLength >= 2)
        {
            var clientStart = clientOffset - spawnOffset;
            EnsureRange(spawn.Length, clientStart, item.ClientDataLength);
            if (item.ClientDataLength >= ClientPlaceOffset + sizeof(ushort) &&
                TryReadPlacement(spawn.AsSpan(clientStart + ClientPlaceOffset), out var place))
            {
                var backpackPlace = (ushort)((place & 0xFFF0) | 3);
                if (place != backpackPlace)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        spawn.AsSpan(clientStart + ClientPlaceOffset),
                        backpackPlace);
                    changed = true;
                }
            }
            else if (spawn[clientStart] == 2 && spawn[clientStart + 1] is 1 or 2 or 3)
            {
                if (spawn[clientStart + 1] != 3)
                {
                    spawn[clientStart + 1] = 3;
                    changed = true;
                }
            }
        }

        if (!changed)
        {
            return raw.Slice(item.RecordOffset, item.RecordLength).ToArray();
        }

        return FrameRecord(spawn, raw.Slice(item.UpdateOffset, item.UpdateLength));
    }

    internal static bool TryReadPlacement(ReadOnlySpan<byte> data, out ushort place)
    {
        place = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var placeType = place & 0x0F;
        var slot = (place >> 4) & 0x3F;
        var baseSlot = (place >> 10) & 0x3F;
        return placeType switch
        {
            1 => slot is >= 1 and < SlotCount && baseSlot is >= 1 and < SlotCount,
            2 or 3 => true,
            _ => false,
        };
    }

    private static byte[] SetAmmoCount(XRayTrilogySave save, XRayRegistryObject item, uint quantity)
    {
        if (!XRayTrilogyReader.TryReadAmmoCount(
                save.Container.Raw.Span,
                item.Version,
                item.StateOffset,
                item.StateLength,
                item.UpdateOffset,
                item.UpdateLength,
                out _,
                out var stateOffset,
                out var updateOffset))
        {
            throw Error($"Ammo template clone 0x{item.ObjectId:X4} does not expose confirmed count fields.");
        }

        var raw = save.Container.Raw.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(stateOffset), checked((ushort)quantity));
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(updateOffset), checked((ushort)quantity));
        return save.Container.Build(raw);
    }

    private static byte[] AppendRecord(XRayContainer container, ReadOnlySpan<byte> record)
    {
        var objectChunk = GetObjectChunk(container);
        var data = objectChunk.Data.Span;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count >= MaximumObjects)
        {
            throw Error("OBJECT registry reached its maximum object count.");
        }

        var payload = GC.AllocateUninitializedArray<byte>(checked(data.Length + record.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(payload, count + 1);
        data[sizeof(uint)..].CopyTo(payload.AsSpan(sizeof(uint)));
        record.CopyTo(payload.AsSpan(data.Length));
        return container.Build(RebuildRaw(container, objectChunk, payload));
    }

    internal static byte[] ReplaceRecord(XRayContainer container, XRayRegistryObject item, ReadOnlySpan<byte> record)
    {
        var objectChunk = GetObjectChunk(container);
        var data = objectChunk.Data.Span;
        var recordStart = checked(item.RecordOffset - (objectChunk.Offset + 2 * sizeof(uint)));
        var recordEnd = checked(recordStart + item.RecordLength);
        if (recordStart < sizeof(uint) || recordEnd > data.Length || recordStart >= recordEnd)
        {
            throw Error($"Object 0x{item.ObjectId:X4} has invalid registry record boundaries.");
        }

        var payload = GC.AllocateUninitializedArray<byte>(checked(data.Length - item.RecordLength + record.Length));
        data[..recordStart].CopyTo(payload);
        record.CopyTo(payload.AsSpan(recordStart));
        data[recordEnd..].CopyTo(payload.AsSpan(recordStart + record.Length));
        return container.Build(RebuildRaw(container, objectChunk, payload));
    }

    private static byte[] RebuildRaw(XRayContainer container, XRayChunk replacedChunk, ReadOnlySpan<byte> replacement)
    {
        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[2 * sizeof(uint)];
        foreach (var chunk in container.Chunks)
        {
            var payload = chunk.Offset == replacedChunk.Offset ? replacement : chunk.Data.Span;
            BinaryPrimitives.WriteUInt32LittleEndian(header, chunk.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(header[sizeof(uint)..], checked((uint)payload.Length));
            output.Write(header);
            output.Write(payload);
        }

        return output.ToArray();
    }

    private static XRayChunk GetObjectChunk(XRayContainer container)
    {
        XRayChunk? match = null;
        foreach (var chunk in container.Chunks)
        {
            if (chunk.Type != 2) continue;
            if (match is not null) throw Error("OBJECT chunk occurs more than once.");
            match = chunk;
        }

        return match ?? throw Error("OBJECT chunk is missing.");
    }

    private static ushort AllocateObjectId(IReadOnlyList<XRayRegistryObject> objects)
    {
        var used = objects.Select(item => item.ObjectId).ToHashSet();
        var highest = used.Count == 0 ? 0 : used.Max();
        for (var candidate = highest + 1; candidate < ushort.MaxValue; candidate++)
        {
            if (!used.Contains(checked((ushort)candidate))) return checked((ushort)candidate);
        }

        for (var candidate = 1; candidate <= highest; candidate++)
        {
            if (!used.Contains(checked((ushort)candidate))) return checked((ushort)candidate);
        }

        throw Error("No free object id remains in the u16 registry.");
    }

    private static bool CatalogMatches(string formatId, string catalogId)
    {
        if (string.Equals(formatId, catalogId, StringComparison.Ordinal)) return true;
        var originalId = formatId switch
        {
            "stalker-soc-ee" => "stalker-soc",
            "stalker-cs-ee" => "stalker-cs",
            "stalker-cop-ee" => "stalker-cop",
            _ => formatId,
        };
        return string.Equals(originalId, catalogId, StringComparison.Ordinal);
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

    private static void EnsureFormat(XRayTrilogySave save, string expectedFormatId)
    {
        if (!string.Equals(save.FormatId, expectedFormatId, StringComparison.Ordinal))
        {
            throw Error("Add operation changed the detected X-Ray release.");
        }
    }

    private static void EnsureRange(int length, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > length - count)
        {
            throw Error("Registry template contains an out-of-range serialized field.");
        }
    }

    private static XRayFormatException Error(string message) => new($"X-Ray add edit: {message}");

    private sealed record AddedObject(ushort ObjectId, ushort ParentId, string ItemKey, uint Quantity, string Family);
}
