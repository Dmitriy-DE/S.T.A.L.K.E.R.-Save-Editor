using System.Buffers.Binary;

namespace StalkerSaveEditor.Core.Formats.XRay;

public static partial class XRayTrilogyReader
{
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
}
