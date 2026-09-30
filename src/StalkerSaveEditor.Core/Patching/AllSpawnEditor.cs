using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Patching;

public enum SpawnEditKind
{
    /// <summary>A waypoint of a patrol path: its name (with flags such as <c>|a=guard</c>), position and graph ids.</summary>
    PatrolPoint,

    /// <summary>The <c>custom_data</c> (logic ini) of a spawn object, found by its unique object name.</summary>
    CustomData,
}

/// <summary>
/// One edit of an X-Ray all.spawn (Clear Sky format). <see cref="Target"/> is the patrol path name or the spawn object
/// name. The expected values must match the file exactly, so an edit made against a different all.spawn is refused.
/// Strings are Latin-1 so every byte round-trips.
/// </summary>
public sealed record SpawnEditOperation(string RelativePath, SpawnEditKind Kind, string Target)
{
    public string? ExpectedFileSha256 { get; init; }

    /// <summary>Waypoint index inside the path (<see cref="SpawnEditKind.PatrolPoint"/>).</summary>
    public int Point { get; init; }

    /// <summary>Current waypoint name, or current custom data.</summary>
    public string Expected { get; init; } = string.Empty;

    /// <summary>New waypoint name, or new custom data; null keeps the name (waypoint position edits).</summary>
    public string? Replacement { get; init; }

    public float[]? Position { get; init; }
    public uint? LevelVertexId { get; init; }
    public ushort? GameVertexId { get; init; }
}

/// <summary>
/// Edits an all.spawn in place. The file is nested chunks (u32 id, u32 size); a spawn object is
/// chunk(object){ chunk 0: u16 id, chunk 1: { chunk 0: u16 packet size + M_SPAWN packet, chunk 1: update packet } }, and
/// inside the packet the object's state starts with a u16 size that counts itself. A patrol point is
/// name\0 + 3 floats + u32 flags + u32 level vertex + u16 game vertex. Changing a string's length only needs the
/// enclosing size fields adjusted: there are no absolute offsets in the file.
/// </summary>
public static class AllSpawnEditor
{
    private const uint SpawnObjectsChunk = 1;
    private const uint PatrolPathsChunk = 3;
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private readonly record struct SizeField(int Offset, int Width);

    private sealed record Splice(int Start, int Length, byte[] Bytes, IReadOnlyList<SizeField> Sizes);

    public static byte[] Apply(byte[] allSpawn, IReadOnlyList<SpawnEditOperation> edits)
    {
        ArgumentNullException.ThrowIfNull(allSpawn);
        ArgumentNullException.ThrowIfNull(edits);
        var splices = edits.Select(edit => edit.Kind switch
        {
            SpawnEditKind.PatrolPoint => PatrolPoint(allSpawn, edit),
            SpawnEditKind.CustomData => CustomData(allSpawn, edit),
            _ => throw new ArgumentException("Unknown all.spawn edit kind.", nameof(edits)),
        }).OrderByDescending(splice => splice.Start).ToList();
        for (var i = 1; i < splices.Count; i++)
        {
            if (splices[i].Start + splices[i].Length > splices[i - 1].Start)
                throw new InvalidDataException("Two all.spawn edits overlap.");
        }

        var result = allSpawn;
        foreach (var splice in splices) // back to front: size fields of earlier edits sit before later ones and do not move
        {
            var delta = splice.Bytes.Length - splice.Length;
            var next = new byte[result.Length + delta];
            result.AsSpan(0, splice.Start).CopyTo(next);
            splice.Bytes.CopyTo(next, splice.Start);
            result.AsSpan(splice.Start + splice.Length).CopyTo(next.AsSpan(splice.Start + splice.Bytes.Length));
            if (delta != 0)
            {
                foreach (var size in splice.Sizes) AddToSize(next, size, delta);
            }
            result = next;
        }
        return result;
    }

    private static Splice PatrolPoint(byte[] data, SpawnEditOperation edit)
    {
        var sizes = new List<SizeField>();
        var (patrolStart, patrolSize) = TopChunk(data, PatrolPathsChunk, sizes);
        var (listStart, listSize) = Child(data, patrolStart, patrolSize, 1, sizes, "patrol path list");
        var key = Latin1.GetBytes(edit.Target + "\0");
        (int Start, int Size)? path = null;
        foreach (var (_, header, start, size) in Children(data, listStart, listSize))
        {
            var nameChunk = Children(data, start, size).FirstOrDefault(child => child.Id == 0);
            if (nameChunk.Size == key.Length && data.AsSpan(nameChunk.Start, nameChunk.Size).SequenceEqual(key))
            {
                if (path is not null) throw new InvalidDataException($"Patrol path {edit.Target} appears more than once.");
                path = (start, size);
                sizes.Add(new SizeField(header + 4, 4));
            }
        }
        if (path is not { } found) throw new InvalidDataException($"Patrol path {edit.Target} is not in all.spawn.");
        var (graphStart, graphSize) = Child(data, found.Start, found.Size, 1, sizes, "patrol graph");
        var (verticesStart, verticesSize) = Child(data, graphStart, graphSize, 1, sizes, "patrol vertices");
        var (vertexStart, vertexSize) = Child(data, verticesStart, verticesSize, (uint)edit.Point, sizes, "patrol point");
        var (pointStart, pointSize) = Child(data, vertexStart, vertexSize, 1, sizes, "patrol point data");

        var nameEnd = data.AsSpan(pointStart, pointSize).IndexOf((byte)0);
        if (nameEnd < 0 || pointSize != nameEnd + 1 + 22) throw new InvalidDataException($"Unexpected patrol point layout in {edit.Target}.");
        var name = Latin1.GetString(data, pointStart, nameEnd);
        if (name != edit.Expected) throw new InvalidDataException($"Patrol point {edit.Target}[{edit.Point}] is '{name}', expected '{edit.Expected}'.");

        var tail = data.AsSpan(pointStart + nameEnd + 1, 22).ToArray();
        if (edit.Position is { } position)
        {
            if (position.Length != 3 || position.Any(value => !float.IsFinite(value))) throw new ArgumentException("A waypoint position needs three finite numbers.");
            for (var i = 0; i < 3; i++) BinaryPrimitives.WriteSingleLittleEndian(tail.AsSpan(i * 4), position[i]);
        }
        if (edit.LevelVertexId is { } level) BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(16), level);
        if (edit.GameVertexId is { } game) BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(20), game);
        var newName = edit.Replacement ?? name;
        if (newName.Length == 0 || newName.Contains('\0')) throw new ArgumentException("A waypoint name must be nonempty and without NUL.");
        var bytes = Latin1.GetBytes(newName + "\0").Concat(tail).ToArray();
        if (bytes.AsSpan().SequenceEqual(data.AsSpan(pointStart, pointSize))) throw new InvalidDataException($"Edit of {edit.Target}[{edit.Point}] changes nothing.");
        return new Splice(pointStart, pointSize, bytes, sizes);
    }

    private static Splice CustomData(byte[] data, SpawnEditOperation edit)
    {
        if (edit.Replacement is null || edit.Replacement.Contains('\0') || edit.Expected.Contains('\0'))
            throw new ArgumentException("Custom data edits need a replacement and no NUL characters.");
        var topSizes = new List<SizeField>();
        var (objectsStart, objectsSize) = TopChunk(data, SpawnObjectsChunk, topSizes);
        var (listStart, listSize) = Child(data, objectsStart, objectsSize, 1, topSizes, "spawn object list");
        Splice? found = null;
        foreach (var (_, header, start, size) in Children(data, listStart, listSize))
        {
            var candidate = ObjectCustomData(data, start, size, edit.Target);
            if (candidate is null) continue;
            if (found is not null) throw new InvalidDataException($"Spawn object {edit.Target} appears more than once.");
            var sizes = new List<SizeField>(topSizes) { new(header + 4, 4) };
            sizes.AddRange(candidate.Value.Sizes);
            var current = Latin1.GetString(data, candidate.Value.Start, candidate.Value.Length);
            if (current != edit.Expected) throw new InvalidDataException($"Custom data of {edit.Target} does not match the expected text.");
            found = new Splice(candidate.Value.Start, candidate.Value.Length, Latin1.GetBytes(edit.Replacement), sizes);
        }
        return found ?? throw new InvalidDataException($"Spawn object {edit.Target} is not in all.spawn.");
    }

    /// <summary>Finds the custom data of one spawn object if its name matches; returns the string range and the size fields inside the object.</summary>
    private static (int Start, int Length, List<SizeField> Sizes)? ObjectCustomData(byte[] data, int start, int size, string target)
    {
        var sizes = new List<SizeField>();
        var body = Children(data, start, size).FirstOrDefault(child => child.Id == 1);
        if (body.Size == 0) return null;
        var spawn = Children(data, body.Start, body.Size).FirstOrDefault(child => child.Id == 0);
        if (spawn.Size < 4) return null;
        sizes.Add(new SizeField(body.Header + 4, 4));
        sizes.Add(new SizeField(spawn.Header + 4, 4));
        var packetLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(spawn.Start));
        if (packetLength + 2 != spawn.Size) return null;
        sizes.Add(new SizeField(spawn.Start, 2));
        var reader = spawn.Start + 2;
        var end = spawn.Start + spawn.Size;
        reader += 2; // M_SPAWN message id
        var section = ReadString(data, ref reader, end);
        var name = ReadString(data, ref reader, end);
        if (section is null || name != target) return null;
        reader += 1 + 1 + 12 + 12 + 2 + 2 + 2 + 2; // game id, rp, position, angle, respawn, id, parent, phantom
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(reader)); reader += 2;
        var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(reader)); reader += 2;
        if (version < 118) throw new InvalidDataException($"Unsupported spawn object version {version} for {target}.");
        reader += 2; // game type (version > 120 in CS; always present for CS all.spawn)
        reader += 2; // script version
        var clientData = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(reader)); reader += 2 + clientData;
        reader += 2; // spawn id
        _ = flags;
        var stateSizeOffset = reader;
        var stateSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(stateSizeOffset));
        if (stateSizeOffset + stateSize != end) throw new InvalidDataException($"Unexpected spawn packet layout for {target}.");
        sizes.Add(new SizeField(stateSizeOffset, 2));
        reader += 2 + 2 + 4 + 4 + 4 + 4; // state size, graph id, distance, direct control, node id, object flags
        var customStart = reader;
        var custom = ReadString(data, ref reader, end) ?? throw new InvalidDataException($"Missing custom data for {target}.");
        return (customStart, Latin1.GetByteCount(custom), sizes);
    }

    private static string? ReadString(byte[] data, ref int offset, int end)
    {
        var length = data.AsSpan(offset, end - offset).IndexOf((byte)0);
        if (length < 0) return null;
        var value = Latin1.GetString(data, offset, length);
        offset += length + 1;
        return value;
    }

    private static (int Start, int Size) TopChunk(byte[] data, uint id, List<SizeField> sizes)
    {
        var chunk = Children(data, 0, data.Length).Where(child => child.Id == id).ToList();
        if (chunk.Count != 1) throw new InvalidDataException($"all.spawn chunk {id} not found.");
        sizes.Add(new SizeField(chunk[0].Header + 4, 4));
        return (chunk[0].Start, chunk[0].Size);
    }

    private static (int Start, int Size) Child(byte[] data, int start, int size, uint id, List<SizeField> sizes, string what)
    {
        var chunk = Children(data, start, size).Where(child => child.Id == id).ToList();
        if (chunk.Count != 1) throw new InvalidDataException($"all.spawn: {what} not found.");
        sizes.Add(new SizeField(chunk[0].Header + 4, 4));
        return (chunk[0].Start, chunk[0].Size);
    }

    private static IEnumerable<(uint Id, int Header, int Start, int Size)> Children(byte[] data, int start, int size)
    {
        var offset = start;
        var end = start + size;
        while (offset < end)
        {
            if (end - offset < 8) throw new InvalidDataException("Truncated all.spawn chunk header.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            if (length > (uint)(end - offset - 8)) throw new InvalidDataException("all.spawn chunk runs past its parent.");
            yield return (id, offset, offset + 8, (int)length);
            offset += 8 + (int)length;
        }
    }

    private static void AddToSize(byte[] data, SizeField field, int delta)
    {
        if (field.Width == 4)
        {
            var value = (long)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(field.Offset)) + delta;
            if (value is < 0 or > uint.MaxValue) throw new InvalidDataException("all.spawn chunk size out of range after edit.");
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field.Offset), (uint)value);
        }
        else
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(field.Offset)) + delta;
            if (value is < 0 or > ushort.MaxValue) throw new InvalidDataException("Spawn packet larger than 64 KB after edit.");
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(field.Offset), (ushort)value);
        }
    }
}
