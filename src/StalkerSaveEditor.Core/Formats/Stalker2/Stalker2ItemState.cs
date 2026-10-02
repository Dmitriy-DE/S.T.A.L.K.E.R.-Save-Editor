using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace StalkerSaveEditor.Core.Formats.Stalker2;

public sealed record Stalker2ArmorConditionAnchor(
    uint Handle,
    int RecordOffset,
    int NestedOffset,
    int ValueOffset,
    float Value);

public sealed record Stalker2WeaponConditionAnchor(
    uint Handle,
    int RecordOffset,
    int ValueOffset,
    float Value,
    int UpgradesOffset,
    int UpgradesCount,
    int UpgradesEnd,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Upgrades);

public static class Stalker2ItemState
{
    private const byte ArmorKind = 1;
    private const byte WeaponKind = 0;
    private const int ArmorNestedRelativeOffset = 0x23;
    private const int ArmorConditionRelativeOffset = 4;
    private const int WeaponScanLimit = 0x800;
    private const int WeaponPrimaryStateLimit = 0x400;
    private const int MinimumUpgradeCount = 2;
    private const int MaximumUpgradeCount = 64;
    private const int DirectModuleMinimumOffset = 0x30;

    public static bool HasEquipmentShape(
        ReadOnlySpan<byte> raw,
        uint handle,
        int recordOffset,
        byte kindCode)
    {
        if (kindCode is not (0 or 1 or 2) || recordOffset < 0 ||
            raw.Length - recordOffset < ArmorNestedRelativeOffset + sizeof(uint))
        {
            return false;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(raw[recordOffset..]) == handle &&
            BinaryPrimitives.ReadUInt32LittleEndian(raw[(recordOffset + ArmorNestedRelativeOffset)..]) == handle;
    }

    public static bool IsArmorName(string? displayName)
    {
        var normalized = (displayName ?? string.Empty).Trim().Replace(' ', '_');
        return normalized.EndsWith("_armor", StringComparison.OrdinalIgnoreCase);
    }

    public static Stalker2ArmorConditionAnchor? ReadArmorCondition(
        ReadOnlySpan<byte> raw,
        uint handle,
        int recordOffset,
        byte kindCode)
    {
        if (kindCode != ArmorKind || recordOffset < 0 ||
            raw.Length - recordOffset < ArmorNestedRelativeOffset + sizeof(uint))
        {
            return null;
        }

        var nestedOffset = checked(recordOffset + ArmorNestedRelativeOffset);
        var valueOffset = checked(nestedOffset + ArmorConditionRelativeOffset);
        if (raw.Length - valueOffset < sizeof(float) ||
            BinaryPrimitives.ReadUInt32LittleEndian(raw[recordOffset..]) != handle ||
            BinaryPrimitives.ReadUInt32LittleEndian(raw[nestedOffset..]) != handle)
        {
            return null;
        }

        var value = BinaryPrimitives.ReadSingleLittleEndian(raw[valueOffset..]);
        if (!float.IsFinite(value) || value is < 0 or > 1)
        {
            return null;
        }

        return new Stalker2ArmorConditionAnchor(handle, recordOffset, nestedOffset, valueOffset, value);
    }

    public static IReadOnlyList<string>? ReadArmorUpgrades(
        ReadOnlySpan<byte> raw,
        Stalker2ArmorConditionAnchor anchor,
        Stalker2NameTables nameTables)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(nameTables);
        var offset = checked(anchor.ValueOffset + sizeof(float));
        if (offset < 0 || raw.Length - offset < sizeof(ushort))
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var valuesStart = checked(offset + sizeof(ushort));
        if (count > MaximumUpgradeCount || raw.Length - valuesStart < count * 3)
        {
            return null;
        }

        if (raw.Length - anchor.RecordOffset < 11)
        {
            return null;
        }

        var ownName = nameTables.Resolve(raw.Slice(anchor.RecordOffset + 8, 3));
        if (ownName is null)
        {
            return null;
        }

        var values = new string[count];
        for (var index = 0; index < count; index++)
        {
            var start = valuesStart + index * 3;
            var name = nameTables.Resolve(raw.Slice(start, 3));
            if (name is null || !name.StartsWith(ownName + "_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            values[index] = name;
        }

        return Array.AsReadOnly(values);
    }

    public static Stalker2WeaponConditionAnchor? ReadWeaponCondition(
        ReadOnlySpan<byte> raw,
        uint handle,
        int recordOffset,
        int? recordEnd,
        byte kindCode,
        Stalker2NameTables nameTables)
    {
        ArgumentNullException.ThrowIfNull(nameTables);
        if (kindCode != WeaponKind || recordOffset < 0 || raw.Length - recordOffset < sizeof(uint) ||
            BinaryPrimitives.ReadUInt32LittleEndian(raw[recordOffset..]) != handle)
        {
            return null;
        }

        var limit = Math.Min(raw.Length, checked(recordOffset + WeaponScanLimit));
        if (recordEnd is { } end)
        {
            limit = Math.Min(limit, Math.Max(recordOffset, end));
        }

        if (limit <= recordOffset + DirectModuleMinimumOffset)
        {
            return null;
        }

        var candidates = new List<Stalker2WeaponConditionAnchor>();
        for (var valueOffset = recordOffset + DirectModuleMinimumOffset; valueOffset < limit - 6; valueOffset++)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(raw[valueOffset..]);
            if (!float.IsFinite(value) || value is < 0 or > 1 ||
                !TryReadUpgradeVector(raw, valueOffset + sizeof(float), limit, nameTables, out var upgrades, out var upgradesEnd))
            {
                continue;
            }

            var modules = ReadDirectModules(raw, valueOffset, recordOffset, nameTables);
            var countedRun = HasCountedModuleRun(raw, valueOffset, modules.Count, recordOffset);
            var upgradeCount = upgrades.Count;
            if (upgradeCount == 0)
            {
                if (countedRun && modules.Any(module => module.Contains("_mag", StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add(CreateWeaponAnchor(
                        handle, recordOffset, valueOffset, value, upgradesEnd, modules, upgrades));
                }

                continue;
            }

            if ((upgradeCount < MinimumUpgradeCount && !countedRun) || modules.Count == 0)
            {
                continue;
            }

            var separator = upgrades[0].IndexOf("_Upgrade_", StringComparison.OrdinalIgnoreCase);
            if (separator <= 0)
            {
                continue;
            }

            var family = upgrades[0][..separator];
            if (!modules.Any(module => module.StartsWith(family + "_", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            candidates.Add(CreateWeaponAnchor(
                handle, recordOffset, valueOffset, value, upgradesEnd, modules, upgrades));
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        Stalker2WeaponConditionAnchor? primary = null;
        foreach (var candidate in candidates)
        {
            if (candidate.ValueOffset - recordOffset >= WeaponPrimaryStateLimit)
            {
                continue;
            }

            if (primary is not null)
            {
                return null;
            }

            primary = candidate;
        }

        return primary;
    }

    private static Stalker2WeaponConditionAnchor CreateWeaponAnchor(
        uint handle,
        int recordOffset,
        int valueOffset,
        float value,
        int upgradesEnd,
        IReadOnlyList<string> modules,
        IReadOnlyList<string> upgrades) =>
        new(
            handle,
            recordOffset,
            valueOffset,
            value,
            checked(valueOffset + sizeof(float)),
            upgrades.Count,
            upgradesEnd,
            modules,
            upgrades);

    private static bool TryReadUpgradeVector(
        ReadOnlySpan<byte> raw,
        int offset,
        int limit,
        Stalker2NameTables nameTables,
        out IReadOnlyList<string> upgrades,
        out int end)
    {
        upgrades = Array.Empty<string>();
        end = 0;
        if (offset < 0 || offset > limit || limit - offset < sizeof(ushort))
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
        if (count == 0)
        {
            end = offset + sizeof(ushort);
            return true;
        }

        var valuesStart = offset + sizeof(ushort);
        if (count > MaximumUpgradeCount || limit - valuesStart < count * 3)
        {
            return false;
        }

        var values = new string[count];
        for (var index = 0; index < count; index++)
        {
            var name = nameTables.Resolve(raw.Slice(valuesStart + index * 3, 3));
            if (name is null || !name.Contains("_upgrade_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            values[index] = name;
        }

        upgrades = Array.AsReadOnly(values);
        end = valuesStart + count * 3;
        return true;
    }

    private static ReadOnlyCollection<string> ReadDirectModules(
        ReadOnlySpan<byte> raw,
        int valueOffset,
        int recordStart,
        Stalker2NameTables nameTables)
    {
        var values = new List<string>();
        for (var offset = valueOffset - 3; offset >= recordStart + DirectModuleMinimumOffset; offset -= 3)
        {
            var name = nameTables.Resolve(raw.Slice(offset, 3));
            if (name is null || !IsDirectModuleName(name))
            {
                break;
            }

            values.Add(name);
        }

        values.Reverse();
        return Array.AsReadOnly(values.ToArray());
    }

    private static bool IsDirectModuleName(string name)
    {
        if (name.Contains("_upgrade_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.StartsWith("en_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("hp_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("ru_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("toprail", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("_mag", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_screw", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCountedModuleRun(
        ReadOnlySpan<byte> raw,
        int valueOffset,
        int moduleCount,
        int recordStart)
    {
        var countOffset = valueOffset - moduleCount * 3 - sizeof(ushort);
        return moduleCount >= 1 && countOffset >= recordStart + DirectModuleMinimumOffset &&
            BinaryPrimitives.ReadUInt16LittleEndian(raw[countOffset..]) == moduleCount;
    }
}
