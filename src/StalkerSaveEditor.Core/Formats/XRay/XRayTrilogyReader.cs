using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static partial class XRayTrilogyReader
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

    private static XRayTrilogySave Read(ReadOnlySpan<byte> data, bool enhanced) =>
        enhanced
            ? ParseSession.GetOrParse<XRayTrilogySave>("xray-ee", data, static bytes => ReadUncached(bytes, enhanced: true))
            : ParseSession.GetOrParse<XRayTrilogySave>("xray", data, static bytes => ReadUncached(bytes, enhanced: false));

    private static XRayTrilogySave ReadUncached(ReadOnlySpan<byte> data, bool enhanced)
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

}
