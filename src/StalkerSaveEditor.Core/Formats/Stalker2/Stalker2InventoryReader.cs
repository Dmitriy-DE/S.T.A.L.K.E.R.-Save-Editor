using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public sealed record Stalker2GridCell(uint Handle, ushort X, ushort Y);

public sealed class Stalker2InventoryLayout
{
    internal Stalker2InventoryLayout(
        int anchorOffset,
        int moneyOffset,
        int ownedFlagOffset,
        int ownedCountOffset,
        IEnumerable<uint> ownedHandles,
        int gridCountOffset,
        int gridOffset,
        IEnumerable<Stalker2GridCell> gridCells,
        int gridEndOffset,
        int declaredGridCount,
        int gridHandleCount,
        IEnumerable<uint> unresolvedHandles,
        IEnumerable<string> warnings)
    {
        AnchorOffset = anchorOffset;
        MoneyOffset = moneyOffset;
        OwnedFlagOffset = ownedFlagOffset;
        OwnedCountOffset = ownedCountOffset;
        OwnedHandles = Array.AsReadOnly(ownedHandles.ToArray());
        GridCountOffset = gridCountOffset;
        GridOffset = gridOffset;
        GridCells = Array.AsReadOnly(gridCells.ToArray());
        GridEndOffset = gridEndOffset;
        DeclaredGridCount = declaredGridCount;
        GridHandleCount = gridHandleCount;
        UnresolvedHandles = Array.AsReadOnly(unresolvedHandles.Distinct().Order().ToArray());
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }

    public int AnchorOffset { get; }

    public int MoneyOffset { get; }

    public int OwnedFlagOffset { get; }

    public int OwnedCountOffset { get; }

    public IReadOnlyList<uint> OwnedHandles { get; }

    public int GridCountOffset { get; }

    public int GridOffset { get; }

    public IReadOnlyList<Stalker2GridCell> GridCells { get; }

    public int GridEndOffset { get; }

    public int DeclaredGridCount { get; }

    public int GridHandleCount { get; }

    public IReadOnlyList<uint> UnresolvedHandles { get; }

    public IReadOnlyList<string> Warnings { get; }
}

public sealed class Stalker2InventoryItem
{
    internal Stalker2InventoryItem(
        uint handle,
        int? x,
        int? y,
        int? width,
        int? height,
        IEnumerable<Stalker2GridCell> cells,
        uint count,
        float totalWeight,
        float unitWeight,
        byte kindCode,
        string category,
        int recordOffset,
        int recordEndGuess,
        string fingerprint,
        string typeKey,
        bool editableCount,
        string? displayName,
        string? positionLabel,
        string? sizeLabel,
        int countMax,
        string? storage,
        string observationSource,
        float? condition,
        bool conditionEditable,
        int? conditionOffset,
        IEnumerable<string>? modules,
        IEnumerable<string>? upgrades)
    {
        Handle = handle;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Cells = Array.AsReadOnly(cells.ToArray());
        Count = count;
        TotalWeight = totalWeight;
        UnitWeight = unitWeight;
        KindCode = kindCode;
        Category = category;
        RecordOffset = recordOffset;
        RecordEndGuess = recordEndGuess;
        Fingerprint = fingerprint;
        TypeKey = typeKey;
        EditableCount = editableCount;
        DisplayName = displayName;
        PositionLabel = positionLabel;
        SizeLabel = sizeLabel;
        CountMax = countMax;
        Storage = storage;
        ObservationSource = observationSource;
        Condition = condition;
        ConditionEditable = conditionEditable;
        ConditionOffset = conditionOffset;
        Modules = modules is null ? null : Array.AsReadOnly(modules.ToArray());
        Upgrades = upgrades is null ? null : Array.AsReadOnly(upgrades.ToArray());
    }

    public uint Handle { get; }

    public int? X { get; }

    public int? Y { get; }

    public int? Width { get; }

    public int? Height { get; }

    public IReadOnlyList<Stalker2GridCell> Cells { get; }

    public uint Count { get; }

    public float TotalWeight { get; }

    public float UnitWeight { get; }

    public byte KindCode { get; }

    public string Category { get; }

    public int RecordOffset { get; }

    public int RecordEndGuess { get; }

    public string Fingerprint { get; }

    public string TypeKey { get; }

    public bool EditableCount { get; }

    public string? DisplayName { get; }

    public string? PositionLabel { get; }

    public string? SizeLabel { get; }

    public int CountMax { get; }

    public string? Storage { get; }

    public string ObservationSource { get; }

    public float? Condition { get; }

    public bool ConditionEditable { get; }

    public int? ConditionOffset { get; }

    public IReadOnlyList<string>? Modules { get; }

    public IReadOnlyList<string>? Upgrades { get; }
}

public sealed record Stalker2OrphanItem(
    uint Handle,
    int RecordOffset,
    ushort X,
    ushort Y,
    uint Count,
    float TotalWeight,
    byte KindCode,
    string Category,
    string TypeKey);

public sealed class Stalker2InventoryResult
{
    internal Stalker2InventoryResult(
        Stalker2InventoryLayout layout,
        IEnumerable<Stalker2InventoryItem> items,
        IEnumerable<Stalker2OrphanItem> orphans,
        IEnumerable<uint> unresolvedHandles,
        IEnumerable<string> warnings,
        Stalker2NameTables? nameTables)
    {
        Layout = layout;
        Items = Array.AsReadOnly(items.ToArray());
        Orphans = Array.AsReadOnly(orphans.ToArray());
        UnresolvedHandles = Array.AsReadOnly(unresolvedHandles.Distinct().Order().ToArray());
        Warnings = Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray());
        NameTables = nameTables;
    }

    public Stalker2InventoryLayout Layout { get; }

    public IReadOnlyList<Stalker2InventoryItem> Items { get; }

    public IReadOnlyList<Stalker2OrphanItem> Orphans { get; }

    public IReadOnlyList<uint> UnresolvedHandles { get; }

    public IReadOnlyList<string> Warnings { get; }

    public Stalker2NameTables? NameTables { get; }
}

public static class Stalker2InventoryReader
{
    private const int GridRecordSize = 8;
    private const int GridWidth = 8;
    private const int MaximumOwnedHandles = 4096;
    private const int MaximumGridCells = 8192;
    private const int ObjectPositionXOffset = 11;
    private const int ObjectPositionYOffset = 13;
    private const int StackMarkerOffset = 18;
    private const int StackCountOffset = 19;
    private const int StackWeightOffset = 24;
    private const int StackKindOffset = 31;
    private const int MinimumObjectCandidateLength = 36;
    private const uint Tombstone = uint.MaxValue;
    private static readonly HashSet<byte> KnownKinds = [0, 1, 2, 4, 5, 6, 7, 8, 10, 11];
    private static readonly HashSet<byte> EquipmentKinds = [0, 1, 2];
    private static readonly HashSet<byte> CarriedKinds = [6, 8, 10, 11];
    private static readonly HashSet<byte> StackKinds = [4, 5, 7, 8];
    private static readonly HashSet<byte> SingleStackKinds = [4, 5, 7];

    public static Stalker2InventoryLayout LocateLayout(ReadOnlySpan<byte> raw)
    {
        var anchor = Stalker2SaveReader.GetWalletAnchor();
        var anchors = FindAll(raw, anchor);
        if (anchors.Count != 1)
        {
            throw Error($"Inventory wallet anchor occurs {anchors.Count} times; expected exactly one.");
        }

        var anchorOffset = anchors[0];
        var moneyOffset = checked(anchorOffset + anchor.Length);
        var ownedFlagOffset = checked(moneyOffset + sizeof(uint));
        var ownedCountOffset = checked(moneyOffset + 2 * sizeof(uint));
        RequireRange(raw, ownedCountOffset, sizeof(ushort), "Owned-handle header is truncated.");
        var ownedCount = BinaryPrimitives.ReadUInt16LittleEndian(raw[ownedCountOffset..]);
        if (ownedCount > MaximumOwnedHandles)
        {
            throw Error($"Suspicious owned-handle count {ownedCount}.");
        }

        var ownedHandlesOffset = checked(ownedCountOffset + sizeof(ushort));
        var ownedBytes = checked(ownedCount * sizeof(uint));
        RequireRange(raw, ownedHandlesOffset, ownedBytes + sizeof(ushort), "Owned-handle array is truncated.");
        var owned = new uint[ownedCount];
        for (var index = 0; index < owned.Length; index++)
        {
            owned[index] = BinaryPrimitives.ReadUInt32LittleEndian(raw[(ownedHandlesOffset + index * sizeof(uint))..]);
        }

        if (owned.Length > 0 && owned.Count(handle => (handle >> 24) == 0x30) < Math.Max(4, owned.Length / 2))
        {
            throw Error("Owned-handle array does not resemble player object handles.");
        }

        var gridCountOffset = checked(ownedHandlesOffset + ownedBytes);
        var gridCount = BinaryPrimitives.ReadUInt16LittleEndian(raw[gridCountOffset..]);
        if (gridCount > MaximumGridCells)
        {
            throw Error($"Suspicious grid-cell count {gridCount}.");
        }

        var gridOffset = checked(gridCountOffset + sizeof(ushort));
        var gridBytes = checked(gridCount * GridRecordSize);
        RequireRange(raw, gridOffset, gridBytes, "Inventory grid is truncated.");

        var cells = new List<Stalker2GridCell>(gridCount);
        var allGridHandles = new HashSet<uint>();
        var unresolved = new HashSet<uint>();
        var warnings = new List<string>();
        var ownedSet = owned.ToHashSet();
        var duplicateOwned = owned
            .Where(handle => handle != Tombstone)
            .GroupBy(handle => handle)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateOwned.Length > 0)
        {
            unresolved.UnionWith(duplicateOwned);
            warnings.Add("Owned handle list содержит дубликаты: " +
                string.Join(", ", duplicateOwned.Order().Select(FormatHandle)));
        }

        var gridPositions = new Dictionary<(ushort X, ushort Y), uint>();
        for (var index = 0; index < gridCount; index++)
        {
            var offset = checked(gridOffset + index * GridRecordSize);
            var handle = BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
            var x = BinaryPrimitives.ReadUInt16LittleEndian(raw[(offset + sizeof(uint))..]);
            var y = BinaryPrimitives.ReadUInt16LittleEndian(raw[(offset + sizeof(uint) + sizeof(ushort))..]);
            allGridHandles.Add(handle);
            if (!ownedSet.Contains(handle))
            {
                unresolved.Add(handle);
                warnings.Add($"Grid cell #{index} ссылается на handle, которого нет в owned list: {FormatHandle(handle)}");
                continue;
            }

            if ((handle >> 24) != 0x30 || x >= GridWidth || y >= 128)
            {
                unresolved.Add(handle);
                warnings.Add($"Grid cell #{index} вне поддерживаемых границ: handle={FormatHandle(handle)}, x={x}, y={y}");
                continue;
            }

            if (gridPositions.TryGetValue((x, y), out var previous))
            {
                unresolved.Add(previous);
                unresolved.Add(handle);
                warnings.Add($"Duplicate grid position {x},{y}: handles {FormatHandle(previous)} и {FormatHandle(handle)}");
            }
            else
            {
                gridPositions.Add((x, y), handle);
            }

            cells.Add(new Stalker2GridCell(handle, x, y));
        }

        if (gridCount == 0) warnings.Add("Inventory grid is empty; only owned handles are shown.");
        var gridEnd = checked(gridOffset + gridBytes);
        return new Stalker2InventoryLayout(
            anchorOffset,
            moneyOffset,
            ownedFlagOffset,
            ownedCountOffset,
            owned,
            gridCountOffset,
            gridOffset,
            cells,
            gridEnd,
            gridCount,
            allGridHandles.Count,
            unresolved,
            warnings);
    }

    public static Stalker2InventoryResult Read(
        ReadOnlySpan<byte> raw,
        Stalker2InventoryLayout? layout = null)
    {
        layout ??= LocateLayout(raw);
        var cellsByHandle = new Dictionary<uint, List<Stalker2GridCell>>();
        foreach (var cell in layout.GridCells)
        {
            if (!cellsByHandle.TryGetValue(cell.Handle, out var cells))
            {
                cells = [];
                cellsByHandle.Add(cell.Handle, cells);
            }

            cells.Add(cell);
        }

        var starts = new Dictionary<uint, int>();
        foreach (var handle in layout.OwnedHandles)
        {
            var candidates = FindObjectCandidates(raw, handle);
            if (candidates.Count == 1) starts.TryAdd(handle, candidates[0].Offset);
        }

        var ends = RecordEndGuesses(raw.Length, starts);
        var typeKeys = new List<byte[]>();
        foreach (var offset in starts.Values)
        {
            if (offset + 11 <= raw.Length) typeKeys.Add(raw.Slice(offset + 8, 3).ToArray());
        }
        var names = Stalker2NameTableReader.Locate(raw, typeKeys);
        var items = new List<Stalker2InventoryItem>();
        var unresolved = layout.UnresolvedHandles.ToHashSet();
        var warnings = layout.Warnings.ToList();

        foreach (var pair in cellsByHandle)
        {
            var candidates = FindObjectCandidates(raw, pair.Key);
            if (candidates.Count != 1)
            {
                unresolved.Add(pair.Key);
                warnings.Add($"Inventory handle {FormatHandle(pair.Key)}: object record candidates={candidates.Count}");
                continue;
            }

            var record = candidates[0];
            var cells = pair.Value;
            var x0 = cells.Min(cell => cell.X);
            var y0 = cells.Min(cell => cell.Y);
            var width = cells.Max(cell => cell.X) - x0 + 1;
            var height = cells.Max(cell => cell.Y) - y0 + 1;
            var expected = new HashSet<(int X, int Y)>();
            for (var y = y0; y < y0 + height; y++)
            {
                for (var x = x0; x < x0 + width; x++) expected.Add((x, y));
            }

            if (cells.Select(cell => ((int)cell.X, (int)cell.Y)).Distinct().Count() != cells.Count ||
                !expected.SetEquals(cells.Select(cell => ((int)cell.X, (int)cell.Y))))
            {
                unresolved.Add(pair.Key);
                warnings.Add($"Handle {FormatHandle(pair.Key)}: footprint/коллизия grid cells не образует полный прямоугольник");
            }

            if (!KnownKinds.Contains(record.Kind))
            {
                unresolved.Add(pair.Key);
                warnings.Add($"Handle {FormatHandle(pair.Key)}: неизвестный object kind={record.Kind}, только read-only");
            }

            var editable = !unresolved.Contains(pair.Key) &&
                ((record.Count > 1 && StackKinds.Contains(record.Kind)) ||
                 (record.Count >= 1 && SingleStackKinds.Contains(record.Kind)));
            var typeKeyBytes = raw.Slice(record.Offset + 8, 3);
            var displayName = DisplayName(names, typeKeyBytes);
            var weaponState = record.Kind == 0 && names is not null
                ? Stalker2ItemState.ReadWeaponCondition(
                    raw,
                    pair.Key,
                    record.Offset,
                    ends.GetValueOrDefault(pair.Key),
                    record.Kind,
                    names)
                : null;
            items.Add(new Stalker2InventoryItem(
                pair.Key,
                x0,
                y0,
                width,
                height,
                cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X),
                record.Count,
                record.Weight,
                record.Weight / record.Count,
                record.Kind,
                CategoryName(record.Kind, displayName),
                record.Offset,
                ends.GetValueOrDefault(pair.Key, Math.Min(raw.Length, record.Offset + 512)),
                Hex(raw.Slice(record.Offset + 4, 14)),
                Hex(typeKeyBytes),
                editable,
                displayName,
                null,
                null,
                1_000_000,
                "inventory",
                "grid",
                weaponState?.Value,
                weaponState is not null,
                weaponState?.ValueOffset,
                weaponState?.Modules,
                weaponState?.Upgrades));
        }

        var gridHandles = layout.GridCells.Select(cell => cell.Handle).ToHashSet();
        foreach (var handle in layout.OwnedHandles)
        {
            if (handle == Tombstone || gridHandles.Contains(handle) || unresolved.Contains(handle)) continue;
            var candidates = FindObjectCandidates(raw, handle);
            if (candidates.Count != 1) continue;
            var record = candidates[0];
            var carried = CarriedKinds.Contains(record.Kind);
            if (!carried && (!EquipmentKinds.Contains(record.Kind) || !HasEquipmentShape(raw, handle, record.Offset, record.Kind))) continue;

            var typeKeyBytes = raw.Slice(record.Offset + 8, 3);
            var displayName = DisplayName(names, typeKeyBytes);
            var armorState = !carried && record.Kind == 1
                ? Stalker2ItemState.ReadArmorCondition(raw, handle, record.Offset, record.Kind)
                : null;
            var armorConditionEditable = armorState is not null && Stalker2ItemState.IsArmorName(displayName);
            var armorUpgrades = armorState is not null && names is not null
                ? Stalker2ItemState.ReadArmorUpgrades(raw, armorState, names)
                : null;
            if (armorUpgrades is { Count: 0 }) armorUpgrades = null;
            var weaponState = record.Kind == 0 && names is not null
                ? Stalker2ItemState.ReadWeaponCondition(
                    raw,
                    handle,
                    record.Offset,
                    ends.GetValueOrDefault(handle),
                    record.Kind,
                    names)
                : null;
            items.Add(new Stalker2InventoryItem(
                handle,
                null,
                null,
                null,
                null,
                Array.Empty<Stalker2GridCell>(),
                record.Count,
                record.Weight,
                record.Weight / record.Count,
                record.Kind,
                CategoryName(record.Kind, displayName),
                record.Offset,
                ends.GetValueOrDefault(handle, Math.Min(raw.Length, record.Offset + 512)),
                Hex(raw.Slice(record.Offset + 4, 14)),
                Hex(typeKeyBytes),
                false,
                displayName,
                carried ? "у персонажа" : "экипировано",
                "неизвестно",
                1_000_000,
                "equipped",
                carried ? "carried" : "equipped",
                armorState?.Value ?? weaponState?.Value,
                armorConditionEditable || weaponState is not null,
                armorState?.ValueOffset ?? weaponState?.ValueOffset,
                weaponState?.Modules,
                weaponState?.Upgrades ?? armorUpgrades));

            if (!carried && record.Kind == 1 && armorState is null)
            {
                warnings.Add($"Equipped handle {FormatHandle(handle)}: S2 armor condition не подтверждён");
            }
        }

        items = items
            .OrderBy(item => item.Y is null)
            .ThenBy(item => item.Y ?? 0)
            .ThenBy(item => item.X is null)
            .ThenBy(item => item.X ?? 0)
            .ThenBy(item => item.Handle)
            .ToList();
        if (items.Count == 0 && layout.DeclaredGridCount > 0)
        {
            warnings.Add("Не удалось сопоставить ни одной grid cell с object record");
        }

        foreach (var handle in layout.OwnedHandles)
        {
            if (handle == Tombstone || gridHandles.Contains(handle) || unresolved.Contains(handle)) continue;
            if (FindObjectCandidates(raw, handle).Count == 1) continue;
            unresolved.Add(handle);
            warnings.Add($"Owned handle {FormatHandle(handle)}: отсутствует однозначный object record");
        }

        var orphans = LocateOrphans(raw, layout, gridHandles, unresolved);
        foreach (var orphan in orphans)
        {
            if (!KnownKinds.Contains(orphan.KindCode))
            {
                unresolved.Add(orphan.Handle);
                warnings.Add($"Handle {FormatHandle(orphan.Handle)}: неизвестный orphan object kind={orphan.KindCode}, только read-only");
            }
        }

        return new Stalker2InventoryResult(
            layout,
            items,
            orphans,
            unresolved,
            warnings,
            names);
    }

    private static ReadOnlyCollection<Stalker2OrphanItem> LocateOrphans(
        ReadOnlySpan<byte> raw,
        Stalker2InventoryLayout layout,
        HashSet<uint> gridHandles,
        HashSet<uint> unresolved)
    {
        var result = new List<Stalker2OrphanItem>();
        foreach (var handle in layout.OwnedHandles)
        {
            if (handle == Tombstone || gridHandles.Contains(handle) || unresolved.Contains(handle)) continue;
            var candidates = FindObjectCandidates(raw, handle);
            if (candidates.Count != 1) continue;
            var record = candidates[0];
            if (HasEquipmentShape(raw, handle, record.Offset, record.Kind)) continue;
            var x = BinaryPrimitives.ReadUInt16LittleEndian(raw[(record.Offset + ObjectPositionXOffset)..]);
            var y = BinaryPrimitives.ReadUInt16LittleEndian(raw[(record.Offset + ObjectPositionYOffset)..]);
            result.Add(new Stalker2OrphanItem(
                handle,
                record.Offset,
                x,
                y,
                record.Count,
                record.Weight,
                record.Kind,
                CategoryName(record.Kind, null),
                Hex(raw.Slice(record.Offset + 8, 3))));
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private static List<ObjectCandidate> FindObjectCandidates(ReadOnlySpan<byte> raw, uint handle)
    {
        Span<byte> needle = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(needle, handle);
        var candidates = new List<ObjectCandidate>();
        var searchFrom = 0;
        while (searchFrom <= raw.Length - needle.Length)
        {
            var relative = raw[searchFrom..].IndexOf(needle);
            if (relative < 0) break;
            var offset = searchFrom + relative;
            searchFrom = offset + 1;
            if (raw.Length - offset < MinimumObjectCandidateLength || raw[offset + StackMarkerOffset] != 0x38) continue;
            var count = BinaryPrimitives.ReadUInt32LittleEndian(raw[(offset + StackCountOffset)..]);
            var weight = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(raw[(offset + StackWeightOffset)..]));
            var kind = raw[offset + StackKindOffset];
            if (count is < 1 or > 10_000_000 || !float.IsFinite(weight) || weight < 0 || weight > 10_000_000) continue;
            candidates.Add(new ObjectCandidate(offset, count, weight, kind));
        }

        return candidates;
    }

    private static Dictionary<uint, int> RecordEndGuesses(int rawLength, Dictionary<uint, int> starts)
    {
        var pairs = starts.Select(pair => (Offset: pair.Value, Handle: pair.Key)).OrderBy(pair => pair.Offset).ToArray();
        var result = new Dictionary<uint, int>();
        for (var index = 0; index < pairs.Length; index++)
        {
            var (offset, handle) = pairs[index];
            var next = index + 1 < pairs.Length ? pairs[index + 1].Offset : Math.Min(rawLength, offset + 4096);
            result[handle] = Math.Min(next, offset + 65536);
        }

        return result;
    }

    private static bool HasEquipmentShape(ReadOnlySpan<byte> raw, uint handle, int offset, byte kindCode) =>
        Stalker2ItemState.HasEquipmentShape(raw, handle, offset, kindCode);

    private static string? DisplayName(Stalker2NameTables? names, ReadOnlySpan<byte> typeKey) =>
        names?.Resolve(typeKey);

    private static string CategoryName(byte kind, string? displayName)
    {
        var normalized = (displayName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("nvg_", StringComparison.Ordinal) ||
            normalized.StartsWith("binocular", StringComparison.Ordinal) ||
            normalized.StartsWith("пнв", StringComparison.Ordinal) ||
            normalized.StartsWith("бинокль", StringComparison.Ordinal) ||
            normalized.StartsWith("бинокл", StringComparison.Ordinal)) return "Устройство";
        if (normalized.Contains("_upgrade_", StringComparison.Ordinal) ||
            normalized.Contains("_attachment_", StringComparison.Ordinal)) return "Модуль/улучшение";
        if (normalized.EndsWith("_armor", StringComparison.Ordinal) ||
            normalized.EndsWith("_helmet", StringComparison.Ordinal)) return "Броня/экипировка";
        if (normalized.Contains("_armor_", StringComparison.Ordinal) ||
            normalized.Contains("_helmet_", StringComparison.Ordinal) ||
            normalized.StartsWith("gunbucket_", StringComparison.Ordinal)) return "Разное";

        return kind switch
        {
            0 => "Оружие",
            1 => "Броня/экипировка",
            2 => "Артефакт",
            4 => "Расходник",
            5 => "Патроны",
            6 => "Детектор",
            7 => "Гранаты",
            8 => "Разное",
            10 => "ПНВ",
            11 => "Бинокль",
            _ => $"Тип {kind}",
        };
    }

    private static List<int> FindAll(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
    {
        var offsets = new List<int>();
        var searchFrom = 0;
        while (searchFrom <= data.Length - needle.Length)
        {
            var relative = data[searchFrom..].IndexOf(needle);
            if (relative < 0) break;
            var offset = searchFrom + relative;
            offsets.Add(offset);
            searchFrom = offset + 1;
        }

        return offsets;
    }

    private static void RequireRange(ReadOnlySpan<byte> raw, int offset, int length, string message)
    {
        if (offset < 0 || length < 0 || offset > raw.Length || raw.Length - offset < length) throw Error(message);
    }

    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string FormatHandle(uint handle) => $"0x{handle:X8}";

    private static Stalker2FormatException Error(string message) => new($"S.T.A.L.K.E.R. 2 inventory: {message}");

    private sealed record ObjectCandidate(int Offset, uint Count, float Weight, byte Kind);
}
