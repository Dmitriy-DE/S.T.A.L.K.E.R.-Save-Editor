using System.Buffers.Binary;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;

namespace StalkerSaveEditor.Core.Formats.XRay;

/// <summary>Where the actor stands: spawn-header position and angle, game vertex and level vertex (STATE).</summary>
public sealed record XRayActorLocation(XRayVector3 Position, XRayVector3 Angle, ushort GameVertexId, uint LevelVertexId);

/// <summary>
/// A place the game itself puts the actor: the destination of a level changer found in the save. After a
/// transition the actor's position, game vertex and level vertex equal these values byte for byte.
/// </summary>
public sealed record XRayRelocationAnchor(
    string ChangerName,
    string DestinationLevel,
    string DestinationPoint,
    ushort GameVertexId,
    uint LevelVertexId,
    XRayVector3 Position,
    XRayVector3 Direction);

/// <summary>
/// Experimental actor relocation (TP-3/TP-4) for the original trilogy. Only level-changer destinations are
/// offered, never free coordinates. The record length never changes: the writer overwrites the spawn-header
/// position and angle, the STATE game vertex and level vertex, and the UPDATE position.
/// </summary>
public static class XRayRelocation
{
    private const int StateGameVertexOffset = 0;
    private const int StateLevelVertexOffset = 10;
    private const int UpdatePositionOffset = 11;
    private static readonly string[] SupportedFormats = ["stalker-soc", "stalker-cs", "stalker-cop"];

    public static bool IsSupported(string formatId) => SupportedFormats.Contains(formatId, StringComparer.Ordinal);

    public static XRayActorLocation ReadActorLocation(XRayTrilogySave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var actor = Actor(save);
        var raw = save.Container.Raw.Span;
        var header = SpawnPositionOffset(raw, actor);
        return new XRayActorLocation(
            ReadVector(raw, header),
            ReadVector(raw, header + 12),
            BinaryPrimitives.ReadUInt16LittleEndian(raw[(actor.StateOffset + StateGameVertexOffset)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(raw[(actor.StateOffset + StateLevelVertexOffset)..]));
    }

    /// <summary>Where each level changer of the save leads, by object handle; changers whose destination is not read are absent.</summary>
    public static IReadOnlyDictionary<ushort, XRayLevelChangerStateSuffix> ReadDestinations(XRayTrilogySave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var raw = save.Container.Raw.Span;
        var result = new Dictionary<ushort, XRayLevelChangerStateSuffix>();
        foreach (var changer in save.RegistryObjects.Where(item => item.Name == "level_changer"))
        {
            if (FindDestination(raw.Slice(changer.StateOffset, changer.StateLength), changer.Version) is { } suffix)
            {
                result[changer.ObjectId] = suffix;
            }
        }

        return result;
    }

    /// <summary>Distinct destinations of the level changers in the save, ordered by level and point.</summary>
    public static IReadOnlyList<XRayRelocationAnchor> ReadAnchors(XRayTrilogySave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var raw = save.Container.Raw.Span;
        var anchors = new List<XRayRelocationAnchor>();
        foreach (var changer in save.RegistryObjects.Where(item => item.Name == "level_changer"))
        {
            if (FindDestination(raw.Slice(changer.StateOffset, changer.StateLength), changer.Version) is { } suffix &&
                suffix is { DestGameVertexId: { } gv, DestLevelVertexId: { } lv, DestPosition: { } position, DestDirection: { } direction })
            {
                anchors.Add(new XRayRelocationAnchor(changer.NameReplace, suffix.DestLevelName, suffix.DestLevelPointName, gv, lv, position, direction));
            }
        }

        return anchors
            .GroupBy(anchor => (anchor.DestinationLevel, anchor.DestinationPoint, anchor.GameVertexId, anchor.LevelVertexId))
            .Select(group => group.First())
            .OrderBy(anchor => anchor.DestinationLevel, StringComparer.Ordinal)
            .ThenBy(anchor => anchor.DestinationPoint, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Moves the actor to <paramref name="anchor"/>; the result is read back before it is returned.</summary>
    public static PreparedEdit Prepare(ReadOnlySpan<byte> source, XRayRelocationAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        var sourceBytes = source.ToArray();
        var save = XRayTrilogyReader.FromBytes(sourceBytes);
        if (!IsSupported(save.FormatId))
        {
            throw new XRayFormatException($"Relocation is not enabled for {save.FormatId}.");
        }

        if (!ReadAnchors(save).Contains(anchor))
        {
            throw new XRayFormatException("The destination is not a level-changer destination of this save.");
        }

        var actor = Actor(save);
        var raw = save.Container.Raw.ToArray();
        var header = SpawnPositionOffset(raw, actor);
        if (header + 24 > actor.RecordOffset + actor.RecordLength || actor.StateLength < StateLevelVertexOffset + 4)
        {
            throw new XRayFormatException("The actor record is too short for the verified position layout.");
        }

        WriteVector(raw, header, anchor.Position);
        WriteVector(raw, header + 12, anchor.Direction);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(actor.StateOffset + StateGameVertexOffset), anchor.GameVertexId);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(actor.StateOffset + StateLevelVertexOffset), anchor.LevelVertexId);
        if (actor.UpdateLength < UpdatePositionOffset + 12)
        {
            throw new XRayFormatException("The actor UPDATE packet is too short to hold a position.");
        }

        var updatePosition = actor.UpdateOffset + UpdatePositionOffset;
        if (ReadVector(save.Container.Raw.Span, updatePosition) != ReadVector(save.Container.Raw.Span, header))
        {
            throw new XRayFormatException("The actor UPDATE position does not match the spawn position; the layout is not the verified one.");
        }

        WriteVector(raw, updatePosition, anchor.Position);
        var written = save.Container.Build(raw);
        var check = ReadActorLocation(XRayTrilogyReader.FromBytes(written));
        if (check.Position != anchor.Position || check.GameVertexId != anchor.GameVertexId || check.LevelVertexId != anchor.LevelVertexId)
        {
            throw new XRayFormatException("The relocated save does not read back at the destination.");
        }

        return PreparedEdit.Replacing(Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(), written);
    }

    private static XRayRegistryObject Actor(XRayTrilogySave save) =>
        save.RegistryObjects.FirstOrDefault(item => item.ObjectId == save.ActorId)
        ?? throw new XRayFormatException("The actor object is missing from the registry.");

    /// <summary>Spawn header: u16 size, u16 M_SPAWN, section\0, name\0, u8 game id, u8 RP, position, angle.</summary>
    private static int SpawnPositionOffset(ReadOnlySpan<byte> raw, XRayRegistryObject actor)
    {
        var position = actor.RecordOffset + 2 + 2;
        for (var strings = 0; strings < 2; strings++)
        {
            var end = raw[position..(actor.RecordOffset + actor.RecordLength)].IndexOf((byte)0);
            if (end < 0) throw new XRayFormatException("The actor spawn header is truncated.");
            position += end + 1;
        }

        return position + 2;
    }

    /// <summary>
    /// The level-changer STATE ends with the destination block, after the restrictor shapes whose length varies.
    /// The block is accepted only where everything around it agrees: the shape list (count, then spheres of 16 and
    /// boxes of 48 bytes, then the restrictor type) ends exactly there, the names are identifiers, the position is
    /// finite and only the known trailer follows. One such place must exist; with none or several the destination
    /// is not reported, because the move writes these numbers into the actor.
    /// </summary>
    internal static XRayLevelChangerStateSuffix? FindDestination(ReadOnlySpan<byte> state, int version)
    {
        XRayLevelChangerStateSuffix? found = null;
        for (var start = 2; start + 32 < state.Length; start++)
        {
            if (!ShapesEndAt(state, start)) continue;
            XRayLevelChangerStateSuffix suffix;
            try
            {
                suffix = XRayLevelChangerReader.ParseStateSuffix(state[start..], version);
            }
            catch (XRayFormatException)
            {
                continue;
            }

            if (!IsIdentifier(suffix.DestLevelName) || !IsIdentifier(suffix.DestLevelPointName)) continue;
            if (suffix.DestPosition is not { } p || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) continue;
            if (!IsStringTail(state[(start + suffix.ConsumedBytes)..])) continue;
            if (found is not null) return null;
            found = suffix;
        }

        return found;
    }

    /// <summary>A restrictor shape list (u8 count; per shape u8 kind, sphere 16 or box 48 bytes; u8 restrictor type) ends at <paramref name="end"/>.</summary>
    private static bool ShapesEndAt(ReadOnlySpan<byte> state, int end)
    {
        const int MaximumShapes = 32, Sphere = 16, Box = 48, LastRestrictorType = 5;
        if (state[end - 1] > LastRestrictorType) return false;
        for (var count = 1; count <= MaximumShapes; count++)
        {
            // Shortest and longest list of this many shapes; every start between them in steps of the size difference.
            for (var boxes = 0; boxes <= count; boxes++)
            {
                var first = end - 2 - count - boxes * Box - (count - boxes) * Sphere;
                if (first < 0) break;
                if (state[first] != count) continue;
                var position = first + 1;
                var valid = true;
                for (var shape = 0; shape < count && valid; shape++)
                {
                    var kind = state[position];
                    valid = kind <= 1;
                    position += 1 + (kind == 1 ? Box : Sphere);
                    if (position > end - 1) valid = false;
                }

                if (valid && position == end - 1) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What may follow the destination block: nothing (SoC), or in CS/CoP an optional flag byte, up to three
    /// zero-terminated printable strings (hint, logic name) and a trailing u16.
    /// </summary>
    private static bool IsStringTail(ReadOnlySpan<byte> tail)
    {
        if (tail.IsEmpty) return true;
        var position = tail[0] <= 1 ? 1 : 0;
        for (var strings = 0; strings < 3 && position < tail.Length; strings++)
        {
            var end = tail[position..].IndexOf((byte)0);
            if (end < 0) break;
            foreach (var b in tail.Slice(position, end))
            {
                if (b is < 0x20 or > 0x7e) return false;
            }

            position += end + 1;
            if (tail.Length - position is 0 or 2) return true;
        }

        return tail.Length - position is 0 or 2;
    }

    private static bool IsIdentifier(string text) =>
        text.Length is > 0 and < 128 && text.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_');

    private static XRayVector3 ReadVector(ReadOnlySpan<byte> raw, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(raw[offset..]),
        BinaryPrimitives.ReadSingleLittleEndian(raw[(offset + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(raw[(offset + 8)..]));

    private static void WriteVector(Span<byte> raw, int offset, XRayVector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(raw[offset..], value.X);
        BinaryPrimitives.WriteSingleLittleEndian(raw[(offset + 4)..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(raw[(offset + 8)..], value.Z);
    }
}
