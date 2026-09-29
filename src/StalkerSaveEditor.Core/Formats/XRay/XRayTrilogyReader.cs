using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static class XRayTrilogyReader
{
    private const int MaximumObjects = 1_000_000;
    private const int MaximumVectorLength = 1_000_000;
    private const int MaximumStringLength = 1 << 20;
    private const ushort SpawnMessage = 1;
    private const ushort UpdateMessage = 0;
    private const ushort SpawnHasVersion = 1 << 5;
    private static readonly FormatDefinition[] OriginalFormats =
    [
        new("stalker-soc", 3, 3, [118], [], []),
        new("stalker-cs", 5, 5, [122, 123, 124], [], []),
        new("stalker-cop", 6, 6, [128], [], []),
    ];
    private static readonly FormatDefinition[] EnhancedFormats =
    [
        new("stalker-soc-ee", 3, 51, [118], [], []),
        new("stalker-cs-ee", 6, 54, [128], ["marsh"u8.ToArray()], ["zaton"u8.ToArray()]),
        new("stalker-cop-ee", 6, 54, [128], ["zaton"u8.ToArray()], ["marsh"u8.ToArray()]),
    ];
    private static readonly Dictionary<string, string> StashLevels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["esc"] = "Кордон",
            ["gar"] = "Свалка",
            ["mar"] = "Болота",
            ["val"] = "Тёмная долина",
            ["agr"] = "Агропром",
            ["red"] = "Рыжий лес",
            ["yan"] = "Янтарь",
            ["mil"] = "Армейские склады",
            ["lim"] = "Лиманск",
            ["hos"] = "Госпиталь",
            ["bar"] = "Бар",
            ["ros"] = "Дикая территория",
            ["zat"] = "Затон",
            ["jup"] = "Юпитер",
            ["pri"] = "Припять",
            ["l01"] = "Кордон",
            ["l02"] = "Свалка",
            ["l03"] = "Агропром",
            ["l04"] = "Тёмная долина",
            ["l05"] = "Бар",
            ["l06"] = "Дикая территория",
            ["l07"] = "Армейские склады",
            ["l08"] = "Янтарь",
            ["l10"] = "Рыжий лес",
            ["l11"] = "Припять",
        };

    public static XRayTrilogySave FromBytes(ReadOnlySpan<byte> data) => Read(data, enhanced: false);

    internal static XRayTrilogySave FromEnhancedBytes(ReadOnlySpan<byte> data) => Read(data, enhanced: true);

    private static XRayTrilogySave Read(ReadOnlySpan<byte> data, bool enhanced)
    {
        var container = XRayContainer.FromBytes(data);
        var chunks = container.Chunks;
        var alife = GetRequiredChunk(chunks, 0).Data.Span;
        if (alife.Length != sizeof(uint))
        {
            throw Error("ALIFE header chunk должен содержать ровно u32 version");
        }

        var alifeVersion = BinaryPrimitives.ReadUInt32LittleEndian(alife);

        var timeChunk = GetRequiredChunk(chunks, 5).Data.Span;
        if (timeChunk.Length < 16)
        {
            throw Error("GAME_TIME chunk короче 16 байт");
        }

        var gameTime = BinaryPrimitives.ReadUInt64LittleEndian(timeChunk);
        var timeFactor = ReadSingle(timeChunk[8..]);
        var normalTimeFactor = ReadSingle(timeChunk[12..]);
        if (!float.IsFinite(timeFactor) || !float.IsFinite(normalTimeFactor))
        {
            throw Error("GAME_TIME содержит нечисловой time factor");
        }

        var objectChunk = GetRequiredChunk(chunks, 2);
        var format = GetSupportedFormat(
            container.Version,
            alifeVersion,
            objectChunk.Data.Span,
            enhanced);
        var records = ParseObjects(container.Raw.Span, objectChunk);
        ObjectRecord? actor = null;
        var actorCount = 0;
        foreach (var record in records)
        {
            if (!string.Equals(record.Name, "actor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actor = record;
            actorCount++;
        }

        if (actorCount != 1)
        {
            throw Error($"найдено actor объектов: {actorCount}, ожидался ровно один");
        }

        var selectedActor = actor!;
        if (!format.ActorVersions.Contains(selectedActor.Version))
        {
            var expected = string.Join(", ", format.ActorVersions.Order());
            throw Error(
                $"actor spawn version {selectedActor.Version} не подтверждён для {format.Id}; " +
                $"ожидалось {expected}");
        }

        var raw = container.Raw.Span;
        var actorState = ParseActorState(
            raw.Slice(selectedActor.StateOffset, selectedActor.StateLength),
            selectedActor.Version,
            selectedActor.StateOffset);
        var factionRelations = Array.Empty<XRayFactionRelation>();
        XRayRelationRegistry? relationRegistry = null;
        if (format.Id is "stalker-soc" or "stalker-cs" or "stalker-cop" or
            "stalker-cs-ee" or "stalker-cop-ee")
        {
            XRayChunk? relationChunk = null;
            var duplicateRelationChunk = false;
            foreach (var chunk in chunks)
            {
                if (chunk.Type != 9)
                {
                    continue;
                }

                if (relationChunk is not null)
                {
                    duplicateRelationChunk = true;
                    break;
                }

                relationChunk = chunk;
            }

            if (relationChunk is not null && !duplicateRelationChunk)
            {
                if (XRayRelationRegistry.TryParse(
                    relationChunk.Data.Span,
                    infoPortionsHaveTimestamp: format.Id != "stalker-cop",
                    out var candidate))
                {
                    var actorRelations = candidate!.ForCharacter(selectedActor.ObjectId);
                    if (actorRelations is not null)
                    {
                        relationRegistry = candidate;
                        factionRelations = new XRayFactionRelation[actorRelations.Communities.Count];
                        for (var index = 0; index < factionRelations.Length; index++)
                        {
                            var relation = actorRelations.Communities[index];
                            factionRelations[index] = new XRayFactionRelation(
                                relation.CommunityId,
                                relation.Goodwill);
                        }
                    }
                }
            }
        }

        var items = new List<XRayInventoryItem>();
        foreach (var record in records)
        {
            if (record.ParentId != selectedActor.ObjectId || record.ObjectId == selectedActor.ObjectId)
            {
                continue;
            }

            items.Add(ToInventoryItem(raw, record, actorOwned: true));
        }

        items.Sort(static (left, right) => left.Handle.CompareTo(right.Handle));
        var registryObjects = new XRayRegistryObject[records.Count];
        List<XRayLevelChanger>? levelChangers = null;
        List<ObjectRecord>? stashBoxes = null;
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            registryObjects[index] = ToRegistryObject(record);
            if (string.Equals(record.Name, "level_changer", StringComparison.OrdinalIgnoreCase))
            {
                (levelChangers ??= []).Add(new XRayLevelChanger(
                    record.ObjectId,
                    record.ParentId,
                    record.Version,
                    record.Name,
                    record.NameReplace));
            }

            if (string.Equals(record.Name, "inventory_box", StringComparison.Ordinal))
            {
                (stashBoxes ??= []).Add(record);
            }
        }

        IReadOnlyList<XRayStash> stashes = Array.Empty<XRayStash>();
        if (stashBoxes is { Count: > 0 })
        {
            var childrenByParent = records
                .GroupBy(record => record.ParentId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var parsedStashes = new List<XRayStash>(stashBoxes.Count);
            foreach (var box in stashBoxes)
            {
                if (!childrenByParent.TryGetValue(box.ObjectId, out var stashItems) || stashItems.Length == 0)
                {
                    continue;
                }

                var children = new XRayInventoryItem[stashItems.Length];
                for (var index = 0; index < stashItems.Length; index++)
                {
                    children[index] = ToInventoryItem(raw, stashItems[index], actorOwned: false);
                }

                var prefix = box.NameReplace.Split('_', 2)[0];
                var level = StashLevels.TryGetValue(prefix, out var levelName) ? levelName : null;
                parsedStashes.Add(new XRayStash(
                    box.ObjectId,
                    box.NameReplace,
                    level,
                    Array.AsReadOnly(children)));
            }

            if (parsedStashes.Count > 0)
            {
                parsedStashes.Sort(static (left, right) =>
                {
                    var levelPresence = (left.Level is null).CompareTo(right.Level is null);
                    if (levelPresence != 0) return levelPresence;
                    var level = string.Compare(left.Level, right.Level, StringComparison.Ordinal);
                    return level != 0 ? level : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
                });
                stashes = Array.AsReadOnly(parsedStashes.ToArray());
            }
        }

        return new XRayTrilogySave(
            format.Id,
            container.Version,
            container,
            selectedActor.Version,
            selectedActor.ObjectId,
            actorState.Money,
            actorState.MoneyOffset,
            actorState.PlayerFactionIndex,
            actorState.PlayerFactionOffset,
            relationRegistry,
            Array.AsReadOnly(factionRelations),
            actorState.Health,
            actorState.Rank,
            actorState.Reputation,
            actorState.Name,
            Array.AsReadOnly(registryObjects),
            levelChangers is null ? Array.Empty<XRayLevelChanger>() : Array.AsReadOnly(levelChangers.ToArray()),
            gameTime,
            timeFactor,
            normalTimeFactor,
            Array.AsReadOnly(items.ToArray()),
            stashes);
    }

    private static XRayInventoryItem ToInventoryItem(
        ReadOnlySpan<byte> raw,
        ObjectRecord record,
        bool actorOwned)
    {
        var (kindCode, category) = CategoryForName(record.Name);
        var isAmmo = record.Name.StartsWith("ammo_", StringComparison.OrdinalIgnoreCase);
        ushort? count = null;
        int? stackStateCountOffset = null;
        int? stackUpdateCountOffset = null;
        var editableCount = false;
        if (isAmmo && TryReadAmmoCount(
            raw,
            record,
            out var parsedCount,
            out var parsedStateCountOffset,
            out var parsedUpdateCountOffset))
        {
            count = parsedCount;
            stackStateCountOffset = parsedStateCountOffset;
            stackUpdateCountOffset = parsedUpdateCountOffset;
            editableCount = true;
        }

        IReadOnlyList<string>? upgrades = null;
        if (record.Version > 123 &&
            TryReadUpgrades(raw, record, out var parsedUpgrades, out _, out _))
        {
            upgrades = Array.AsReadOnly(parsedUpgrades);
        }

        float? condition = null;
        int? conditionStateOffset = null;
        int? conditionUpdateOffset = null;
        int? clientConditionOffset = null;
        if (actorOwned && HasConditionFamily(record.Name) && TryReadCondition(
            raw,
            record,
            out var parsedCondition,
            out var parsedConditionStateOffset,
            out var parsedConditionUpdateOffset,
            out var parsedClientConditionOffset))
        {
            condition = parsedCondition;
            conditionStateOffset = parsedConditionStateOffset;
            conditionUpdateOffset = parsedConditionUpdateOffset;
            clientConditionOffset = parsedClientConditionOffset;
        }

        var placement = actorOwned ? TryReadPlacement(raw, record) : null;

        return new XRayInventoryItem(
            record.ObjectId,
            record.ParentId,
            record.Name,
            kindCode,
            category,
            count,
            editableCount,
            upgrades,
            stackStateCountOffset,
            stackUpdateCountOffset,
            condition,
            conditionStateOffset,
            conditionUpdateOffset,
            clientConditionOffset,
            placement);
    }

    private static XRayPlacementAnchor? TryReadPlacement(ReadOnlySpan<byte> raw, ObjectRecord record)
    {
        if (record.ClientDataOffset is not { } clientDataOffset ||
            record.ClientDataLength < 1 + sizeof(ushort))
        {
            return null;
        }

        var offset = checked(clientDataOffset + 1);
        if (offset < clientDataOffset || offset > raw.Length - sizeof(ushort) ||
            offset > clientDataOffset + record.ClientDataLength - sizeof(ushort) ||
            !XRayAddWriter.TryReadPlacement(raw[offset..], out var value))
        {
            return null;
        }

        return new XRayPlacementAnchor(value, offset);
    }

    private static bool HasConditionFamily(string name)
    {
        var key = name.ToLowerInvariant();
        return key.StartsWith("wpn_", StringComparison.Ordinal) ||
            key.StartsWith("weapon_", StringComparison.Ordinal) ||
            key.StartsWith("outfit_", StringComparison.Ordinal) ||
            key.StartsWith("scientific_", StringComparison.Ordinal) ||
            key.StartsWith("helm_", StringComparison.Ordinal) ||
            key.StartsWith("armor_", StringComparison.Ordinal) ||
            key.EndsWith("_outfit", StringComparison.Ordinal) ||
            key.EndsWith("_helmet", StringComparison.Ordinal) ||
            key.EndsWith("_helm", StringComparison.Ordinal) ||
            key.EndsWith("_armor", StringComparison.Ordinal);
    }

    private static bool TryReadCondition(
        ReadOnlySpan<byte> raw,
        ObjectRecord record,
        out float condition,
        out int stateOffset,
        out int? updateOffset,
        out int? clientOffset)
    {
        condition = 0;
        stateOffset = -1;
        updateOffset = null;
        clientOffset = null;
        if (record.Version <= 52)
        {
            return false;
        }

        try
        {
            var stateReader = new SpanReader(
                raw.Slice(record.StateOffset, record.StateLength),
                "inventory condition STATE");
            ReadDynamicVisualState(ref stateReader, record.Version);
            if (stateReader.Remaining < sizeof(float))
            {
                return false;
            }

            stateOffset = checked(record.StateOffset + stateReader.Position);
            condition = ReadSingle(raw[stateOffset..]);
            if (!float.IsFinite(condition) || condition is < 0 or > 1)
            {
                return false;
            }

            Span<int> updateMatches = stackalloc int[2];
            var updateMatchCount = 0;
            ReadOnlySpan<int> updateCandidates = [3, 4];
            foreach (var relativeOffset in updateCandidates)
            {
                if (relativeOffset >= record.UpdateLength)
                {
                    continue;
                }

                var candidateOffset = record.UpdateOffset + relativeOffset;
                var decoded = raw[candidateOffset] / 255f;
                if (MathF.Abs(decoded - condition) <= (1f / 255f) + 1e-6f)
                {
                    updateMatches[updateMatchCount++] = candidateOffset;
                }
            }

            if (updateMatchCount == 1)
            {
                updateOffset = updateMatches[0];
            }

            if (record.ClientDataOffset is not { } clientStart)
            {
                return true;
            }

            var clientEnd = checked(clientStart + record.ClientDataLength);
            Span<int> clientMatches = stackalloc int[2];
            var clientMatchCount = 0;
            for (var candidateOffset = clientStart + 2; candidateOffset < clientEnd - 3; candidateOffset++)
            {
                var decoded = ReadSingle(raw[candidateOffset..]);
                if (!float.IsFinite(decoded) || MathF.Abs(decoded - condition) > 1e-6f)
                {
                    continue;
                }

                var place = BinaryPrimitives.ReadUInt16LittleEndian(raw[(candidateOffset - 2)..]);
                if (HasRecognizedStorage(place))
                {
                    if (clientMatchCount < clientMatches.Length)
                    {
                        clientMatches[clientMatchCount] = candidateOffset;
                    }

                    clientMatchCount++;
                }
            }

            if (clientMatchCount == 1)
            {
                clientOffset = clientMatches[0];
            }

            return true;
        }
        catch (XRayFormatException)
        {
            return false;
        }
    }

    private static bool HasRecognizedStorage(ushort place)
    {
        var placeType = place & 0x0F;
        if (placeType is 2 or 3)
        {
            return true;
        }

        if (placeType != 1)
        {
            return false;
        }

        var slotId = (place >> 4) & 0x3F;
        var baseSlotId = (place >> 10) & 0x3F;
        return slotId < 14 && baseSlotId < 14;
    }

    private static FormatDefinition GetSupportedFormat(
        uint containerVersion,
        uint alifeVersion,
        ReadOnlySpan<byte> objectData,
        bool enhanced)
    {
        var formats = enhanced ? EnhancedFormats : OriginalFormats;
        foreach (var format in formats)
        {
            if (format.ContainerVersion != containerVersion || format.AlifeVersion != alifeVersion)
            {
                continue;
            }

            var hasRequiredMarkers = true;
            foreach (var marker in format.RequiredObjectMarkers)
            {
                if (objectData.IndexOf(marker) < 0)
                {
                    hasRequiredMarkers = false;
                    break;
                }
            }

            var hasForbiddenMarkers = false;
            foreach (var marker in format.ForbiddenObjectMarkers)
            {
                if (objectData.IndexOf(marker) >= 0)
                {
                    hasForbiddenMarkers = true;
                    break;
                }
            }

            if (hasRequiredMarkers && !hasForbiddenMarkers)
            {
                return format;
            }
        }

        var target = enhanced ? "Enhanced Edition" : "original trilogy";
        throw Error(
            $"container version {containerVersion}, ALIFE version {alifeVersion}, " +
            $"and OBJECT markers do not identify a supported {target} save");
    }

    private static XRayChunk GetRequiredChunk(IReadOnlyList<XRayChunk> chunks, uint type)
    {
        XRayChunk? match = null;
        foreach (var chunk in chunks)
        {
            if (chunk.Type != type)
            {
                continue;
            }

            if (match is not null)
            {
                throw Error($"chunk type={type} встречается больше одного раза");
            }

            match = chunk;
        }

        return match ?? throw Error($"обязательный chunk type={type} отсутствует");
    }

    private static List<ObjectRecord> ParseObjects(
        ReadOnlySpan<byte> raw,
        XRayChunk chunk)
    {
        var reader = new SpanReader(chunk.Data.Span, "OBJECT chunk");
        var count = reader.ReadUInt32();
        if (count == 0 || count > MaximumObjects)
        {
            throw Error($"OBJECT count={count} вне диапазона");
        }

        var dataOffset = checked(chunk.Offset + 8);
        var records = new List<ObjectRecord>(checked((int)count));
        var seenIds = new HashSet<ushort>();
        for (var index = 0; index < count; index++)
        {
            var recordRelativeOffset = reader.Position;
            var spawnSize = reader.ReadUInt16();
            var spawnRelativeOffset = reader.Position;
            var spawnPacket = reader.ReadBytes(spawnSize);
            var spawn = ParseSpawn(spawnPacket, dataOffset + spawnRelativeOffset);
            var updateSize = reader.ReadUInt16();
            var updateRelativeOffset = reader.Position;
            var updatePacket = reader.ReadBytes(updateSize);
            if (updatePacket.Length < sizeof(ushort) ||
                BinaryPrimitives.ReadUInt16LittleEndian(updatePacket) != UpdateMessage)
            {
                throw Error($"объект '{spawn.Name}': UPDATE не начинается с M_UPDATE");
            }

            if (!seenIds.Add(spawn.ObjectId))
            {
                throw Error($"повторяющийся object id 0x{spawn.ObjectId:X4}");
            }

            records.Add(new ObjectRecord(
                spawn.Name,
                spawn.NameReplace,
                spawn.ObjectId,
                spawn.ParentId,
                spawn.Version,
                spawn.StateOffset,
                spawn.StateLength,
                dataOffset + updateRelativeOffset,
                updateSize,
                dataOffset + recordRelativeOffset,
                reader.Position - recordRelativeOffset,
                spawn.ClientDataOffset,
                spawn.ClientDataLength));
        }

        if (reader.Remaining != 0)
        {
            throw Error($"OBJECT chunk имеет {reader.Remaining} лишних байт после {count} объектов");
        }

        return records;
    }

    private static SpawnRecord ParseSpawn(ReadOnlySpan<byte> packet, int packetOffset)
    {
        var reader = new SpanReader(packet, "SPAWN packet");
        if (reader.ReadUInt16() != SpawnMessage)
        {
            throw Error("объект начинается не с M_SPAWN");
        }

        var name = reader.ReadZeroTerminatedString();
        var nameReplace = reader.ReadZeroTerminatedString();
        reader.Skip(2); // game id, respawn point
        reader.Skip(6 * sizeof(float)); // position and angles
        reader.Skip(sizeof(ushort)); // respawn time
        var objectId = reader.ReadUInt16();
        var parentId = reader.ReadUInt16();
        reader.Skip(sizeof(ushort)); // phantom id
        var flags = reader.ReadUInt16();
        if ((flags & SpawnHasVersion) == 0)
        {
            throw Error($"объект '{name}': отсутствует M_SPAWN_VERSION");
        }

        var version = reader.ReadUInt16();
        if (version < 112)
        {
            throw Error($"объект '{name}': spawn version {version} ниже безопасно поддерживаемого 112");
        }

        if (version > 120)
        {
            reader.Skip(sizeof(ushort)); // game type
        }

        if (version > 69)
        {
            reader.Skip(sizeof(ushort)); // script version
        }

        int? clientDataOffset = null;
        var clientDataLength = 0;
        if (version > 70)
        {
            clientDataLength = version > 93 ? reader.ReadUInt16() : reader.ReadByte();
            clientDataOffset = checked(packetOffset + reader.Position);
            reader.Skip(clientDataLength);
        }

        if (version > 79)
        {
            reader.Skip(sizeof(ushort)); // spawn id
        }

        var stateSize = reader.ReadUInt16();
        if (stateSize < sizeof(ushort))
        {
            throw Error($"объект '{name}': STATE size={stateSize} меньше 2");
        }

        var stateLength = stateSize - sizeof(ushort);
        if (reader.Remaining != stateLength)
        {
            throw Error($"объект '{name}': STATE выходит за SPAWN packet или после него есть лишние байты");
        }

        var stateOffset = checked(packetOffset + reader.Position);
        reader.Skip(stateLength);
        return new SpawnRecord(
            name,
            nameReplace,
            objectId,
            parentId,
            version,
            stateOffset,
            stateLength,
            clientDataOffset,
            clientDataLength);
    }

    private static XRayRegistryObject ToRegistryObject(ObjectRecord record) => new(
            record.Name,
            record.NameReplace,
            record.ObjectId,
            record.ParentId,
            record.Version,
            record.RecordOffset,
            record.RecordLength,
            record.StateOffset,
            record.StateLength,
            record.UpdateOffset,
            record.UpdateLength,
            record.ClientDataOffset,
            record.ClientDataLength);

    /// <summary>Reads the shared creature prefix (health, killer, death time) of a registry object's STATE; null when it is not laid out as a creature.</summary>
    internal static XRayCreatureVitals? ReadCreatureVitals(ReadOnlySpan<byte> raw, XRayRegistryObject item)
    {
        if (item.Version <= 18 || item.StateLength <= 0 || item.StateOffset < 0 || item.StateOffset + item.StateLength > raw.Length)
        {
            return null;
        }

        try
        {
            var version = item.Version;
            var reader = new SpanReader(raw.Slice(item.StateOffset, item.StateLength), "creature STATE");
            if (version < 105)
            {
                return null;
            }

            SkipTraderState(ref reader, version);
            ReadDynamicVisualState(ref reader, version);
            reader.Skip(3);
            var healthOffset = checked(item.StateOffset + reader.Position);
            var health = reader.ReadSingle();
            if (version < 32)
            {
                _ = reader.ReadZeroTerminatedString();
            }

            if (version > 87)
            {
                SkipUInt16Vector(ref reader);
                SkipUInt16Vector(ref reader);
            }

            ushort? killer = null;
            if (version > 94)
            {
                killer = reader.ReadUInt16();
            }

            ulong? deathTime = null;
            if (version > 115)
            {
                deathTime = reader.ReadUInt64();
            }

            return float.IsFinite(health) && health >= -1f && health <= 1f
                ? new XRayCreatureVitals(item.ObjectId, item.Name, health, killer, deathTime) { HealthOffset = healthOffset }
                : null;
        }
        catch (XRayFormatException)
        {
            return null;
        }
    }

    // Human stalkers serialise the trader block before the visual/creature block (the actor does the opposite).
    private static void SkipTraderState(ref SpanReader reader, ushort version)
    {
        reader.Skip(sizeof(uint)); // money
        _ = reader.ReadZeroTerminatedString(); // specific character
        reader.Skip(sizeof(uint)); // trader flags
        _ = reader.ReadZeroTerminatedString(); // character profile
        reader.Skip(sizeof(int) * 3); // community, rank, reputation
        _ = reader.ReadZeroTerminatedString(); // display name
        if (version > 124)
        {
            reader.Skip(2);
        }
    }

    private static ActorState ParseActorState(ReadOnlySpan<byte> state, ushort version, int stateOffset)
    {
        var reader = new SpanReader(state, "actor STATE");
        ReadDynamicVisualState(ref reader, version);
        reader.Skip(3); // team, squad, group
        float? health = null;
        if (version > 18)
        {
            var value = reader.ReadSingle();
            health = value is >= 0 and <= 1 ? value : null;
        }

        if (version < 32)
        {
            _ = reader.ReadZeroTerminatedString();
        }

        if (version > 87)
        {
            SkipUInt16Vector(ref reader);
            SkipUInt16Vector(ref reader);
        }

        if (version > 94)
        {
            reader.Skip(sizeof(ushort)); // killer id
        }

        if (version > 115)
        {
            reader.Skip(sizeof(ulong)); // game death time
        }

        if (version > 19 && version < 108)
        {
            reader.Skip(sizeof(uint)); // legacy events
        }

        if (version <= 62)
        {
            throw Error($"actor spawn version {version}: money field не сериализуется");
        }

        var moneyOffset = checked(stateOffset + reader.Position);
        var money = reader.ReadUInt32();
        if (version > 75 && version < 98)
        {
            reader.Skip(sizeof(int)); // legacy specific-character index
        }
        else if (version >= 98)
        {
            _ = reader.ReadZeroTerminatedString();
        }

        if (version > 77)
        {
            reader.Skip(sizeof(uint)); // trader flags
        }

        if (version > 81 && version < 96)
        {
            reader.Skip(sizeof(int)); // legacy character profile index
        }
        else if (version > 95)
        {
            _ = reader.ReadZeroTerminatedString();
        }

        int? faction = null;
        int? factionOffset = null;
        if (version > 85)
        {
            factionOffset = checked(stateOffset + reader.Position);
            faction = reader.ReadInt32();
        }

        int? rank = null;
        int? reputation = null;
        if (version > 86)
        {
            rank = reader.ReadInt32();
            reputation = reader.ReadInt32();
        }

        string? name = null;
        if (version > 104)
        {
            // Player-visible text: X-Ray writes it in cp1251 (the Python oracle decodes the same way).
            name = reader.ReadDisplayString();
            if (name.Length == 0)
            {
                name = null;
            }
        }

        if (version > 124)
        {
            reader.Skip(2); // deadbody can take, closed
        }

        return new ActorState(money, moneyOffset, faction, factionOffset, health, rank, reputation, name);
    }

    private static void ReadDynamicVisualState(ref SpanReader reader, ushort version)
    {
        if (version >= 1)
        {
            if (version > 24)
            {
                if (version < 83)
                {
                    reader.Skip(sizeof(float));
                }
            }
            else
            {
                reader.Skip(sizeof(byte));
            }

            if (version < 4)
            {
                reader.Skip(sizeof(ushort));
            }

            reader.Skip(sizeof(ushort)); // graph id
            reader.Skip(sizeof(float)); // distance
        }

        if (version >= 4)
        {
            reader.Skip(sizeof(uint)); // direct control
        }

        if (version >= 8)
        {
            reader.Skip(sizeof(uint)); // node id
        }

        if (version is > 22 and <= 79)
        {
            reader.Skip(sizeof(ushort)); // legacy spawn id
        }

        if (version is > 23 and < 84)
        {
            _ = reader.ReadZeroTerminatedString(); // legacy spawn control
        }

        if (version > 49)
        {
            reader.Skip(sizeof(uint)); // ALife flags
        }

        if (version > 57)
        {
            _ = reader.ReadZeroTerminatedString(); // ini string
        }

        if (version > 61)
        {
            reader.Skip(sizeof(uint)); // story id
        }

        if (version > 111)
        {
            reader.Skip(sizeof(uint)); // spawn story id
        }

        if (version > 31)
        {
            _ = reader.ReadZeroTerminatedString(); // visual name
            if (version > 103)
            {
                reader.Skip(sizeof(byte)); // visual flags
            }
        }
    }

    private static bool TryReadAmmoCount(
        ReadOnlySpan<byte> raw,
        ObjectRecord record,
        out ushort count,
        out int stateCountOffset,
        out int updateCountOffset) => TryReadAmmoCount(
            raw,
            record.Version,
            record.StateOffset,
            record.StateLength,
            record.UpdateOffset,
            record.UpdateLength,
            out count,
            out stateCountOffset,
            out updateCountOffset);

    internal static bool TryReadAmmoCount(
        ReadOnlySpan<byte> raw,
        ushort version,
        int stateOffset,
        int stateLength,
        int updateOffset,
        int updateLength,
        out ushort count,
        out int stateCountOffset,
        out int updateCountOffset)
    {
        count = 0;
        stateCountOffset = -1;
        updateCountOffset = -1;
        try
        {
            var reader = new SpanReader(raw.Slice(stateOffset, stateLength), "ammo STATE");
            ReadDynamicVisualState(ref reader, version);
            if (version > 52)
            {
                reader.Skip(sizeof(float)); // condition
            }

            if (version > 123)
            {
                SkipStringVector(ref reader);
            }

            count = reader.ReadUInt16();
            stateCountOffset = checked(stateOffset + reader.Position - sizeof(ushort));
            if (updateLength < 5)
            {
                return false;
            }

            updateCountOffset = updateOffset + updateLength - sizeof(ushort);
            _ = BinaryPrimitives.ReadUInt16LittleEndian(raw[updateCountOffset..]);
            return true;
        }
        catch (XRayFormatException)
        {
            return false;
        }
    }

    private static bool TryReadUpgrades(
        ReadOnlySpan<byte> raw,
        ObjectRecord record,
        out string[] upgrades,
        out int upgradesOffset,
        out int upgradesLength) => TryReadUpgrades(
            raw,
            record.Version,
            record.StateOffset,
            record.StateLength,
            out upgrades,
            out upgradesOffset,
            out upgradesLength);

    internal static bool TryReadUpgrades(
        ReadOnlySpan<byte> raw,
        ushort version,
        int stateOffset,
        int stateLength,
        out string[] upgrades,
        out int upgradesOffset,
        out int upgradesLength)
    {
        upgrades = [];
        upgradesOffset = -1;
        upgradesLength = 0;
        try
        {
            var reader = new SpanReader(raw.Slice(stateOffset, stateLength), "inventory STATE");
            ReadDynamicVisualState(ref reader, version);
            if (version > 52)
            {
                reader.Skip(sizeof(float)); // condition
            }

            var start = reader.Position;
            upgrades = ReadStringVector(ref reader);
            upgradesOffset = checked(stateOffset + start);
            upgradesLength = reader.Position - start;
            return true;
        }
        catch (XRayFormatException)
        {
            return false;
        }
    }

    private static void SkipUInt16Vector(ref SpanReader reader)
    {
        var count = reader.ReadUInt32();
        if (count > MaximumVectorLength)
        {
            throw Error($"vector count={count} слишком велик");
        }

        reader.Skip(checked((int)count * sizeof(ushort)));
    }

    private static void SkipStringVector(ref SpanReader reader) => _ = ReadStringVector(ref reader);

    private static string[] ReadStringVector(ref SpanReader reader)
    {
        var count = reader.ReadUInt32();
        if (count > MaximumVectorLength)
        {
            throw Error($"upgrades count={count} слишком велик");
        }

        var values = new string[checked((int)count)];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = reader.ReadZeroTerminatedString();
        }

        return values;
    }

    private static (int KindCode, string Category) CategoryForName(string name)
    {
        var key = name.ToLowerInvariant();
        if (key.StartsWith("ammo_", StringComparison.Ordinal)) return (5, "Патроны");
        if (key.StartsWith("wpn_", StringComparison.Ordinal) || key.StartsWith("weapon_", StringComparison.Ordinal)) return (0, "Оружие");
        if (key.StartsWith("outfit_", StringComparison.Ordinal) || key.StartsWith("scientific_", StringComparison.Ordinal) ||
            key.StartsWith("helm_", StringComparison.Ordinal) || key.StartsWith("armor_", StringComparison.Ordinal) ||
            key.EndsWith("_outfit", StringComparison.Ordinal) || key.EndsWith("_helmet", StringComparison.Ordinal) ||
            key.EndsWith("_helm", StringComparison.Ordinal) || key.EndsWith("_armor", StringComparison.Ordinal))
        {
            return (1, "Броня/экипировка");
        }

        if (key.StartsWith("af_", StringComparison.Ordinal) || key.StartsWith("artifact_", StringComparison.Ordinal)) return (2, "Артефакт");
        if (key.StartsWith("device_", StringComparison.Ordinal) || key.StartsWith("detector_", StringComparison.Ordinal)) return (8, "Устройство");
        if (key.StartsWith("grenade", StringComparison.Ordinal) || key.StartsWith("rgd", StringComparison.Ordinal) || key.StartsWith("f1_", StringComparison.Ordinal)) return (7, "Гранаты/стак");
        if (key.StartsWith("medkit", StringComparison.Ordinal) || key.StartsWith("bandage", StringComparison.Ordinal) ||
            key.StartsWith("antirad", StringComparison.Ordinal) || key.StartsWith("drug_", StringComparison.Ordinal) ||
            key.StartsWith("food_", StringComparison.Ordinal) || key.StartsWith("bread", StringComparison.Ordinal) ||
            key.StartsWith("kolbasa", StringComparison.Ordinal) || key.StartsWith("vodka", StringComparison.Ordinal) ||
            key.StartsWith("energy", StringComparison.Ordinal))
        {
            return (4, "Расходник");
        }

        return (8, "Разное");
    }

    private static float ReadSingle(ReadOnlySpan<byte> data) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));

    private static XRayFormatException Error(string message) => new($"X-Ray save: {message}");

    private sealed record FormatDefinition(
        string Id,
        uint ContainerVersion,
        uint AlifeVersion,
        ushort[] ActorVersions,
        byte[][] RequiredObjectMarkers,
        byte[][] ForbiddenObjectMarkers);

    private sealed record ObjectRecord(
        string Name,
        string NameReplace,
        ushort ObjectId,
        ushort ParentId,
        ushort Version,
        int StateOffset,
        int StateLength,
        int UpdateOffset,
        int UpdateLength,
        int RecordOffset,
        int RecordLength,
        int? ClientDataOffset,
        int ClientDataLength);

    private sealed record SpawnRecord(
        string Name,
        string NameReplace,
        ushort ObjectId,
        ushort ParentId,
        ushort Version,
        int StateOffset,
        int StateLength,
        int? ClientDataOffset,
        int ClientDataLength);

    private sealed record ActorState(
        uint Money,
        int MoneyOffset,
        int? PlayerFactionIndex,
        int? PlayerFactionOffset,
        float? Health,
        int? Rank,
        int? Reputation,
        string? Name);

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly string _label;

        public SpanReader(ReadOnlySpan<byte> data, string label)
        {
            _data = data;
            _label = label;
            Position = 0;
        }

        public int Position { get; private set; }

        public int Remaining => _data.Length - Position;

        public byte ReadByte()
        {
            Ensure(sizeof(byte));
            return _data[Position++];
        }

        public ushort ReadUInt16()
        {
            Ensure(sizeof(ushort));
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
            Position += sizeof(ushort);
            return value;
        }

        public ulong ReadUInt64()
        {
            Ensure(sizeof(ulong));
            var value = BinaryPrimitives.ReadUInt64LittleEndian(_data[Position..]);
            Position += sizeof(ulong);
            return value;
        }

        public uint ReadUInt32()
        {
            Ensure(sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
            Position += sizeof(uint);
            return value;
        }

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public float ReadSingle()
        {
            Ensure(sizeof(float));
            var value = XRayTrilogyReader.ReadSingle(_data[Position..]);
            Position += sizeof(float);
            return value;
        }

        public byte[] ReadBytes(int length)
        {
            Ensure(length);
            var bytes = _data.Slice(Position, length).ToArray();
            Position += length;
            return bytes;
        }

        public string ReadZeroTerminatedString()
        {
            var available = Math.Min(Remaining, MaximumStringLength + 1);
            var terminator = _data.Slice(Position, available).IndexOf((byte)0);
            if (terminator < 0)
            {
                throw Error($"{_label}: zero-terminated string отсутствует или слишком длинная");
            }

            var value = Encoding.UTF8.GetString(_data.Slice(Position, terminator));
            Position += terminator + 1;
            return value;
        }

        /// <summary>Zero-terminated player-visible text: strict UTF-8, otherwise cp1251.</summary>
        public string ReadDisplayString()
        {
            var available = Math.Min(Remaining, MaximumStringLength + 1);
            var terminator = _data.Slice(Position, available).IndexOf((byte)0);
            if (terminator < 0)
            {
                throw Error($"{_label}: zero-terminated string отсутствует или слишком длинная");
            }

            var value = Content.LtxDocument.Decode(_data.Slice(Position, terminator));
            Position += terminator + 1;
            return value;
        }

        public void Skip(int length)
        {
            Ensure(length);
            Position += length;
        }

        private void Ensure(int length)
        {
            if (length < 0 || length > Remaining)
            {
                throw Error($"{_label}: ожидалось {length} байт, осталось {Remaining}");
            }
        }
    }
}
