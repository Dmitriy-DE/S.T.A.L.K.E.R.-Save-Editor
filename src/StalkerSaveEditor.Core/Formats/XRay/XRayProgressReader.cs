using System.Buffers.Binary;
using System.Text;

namespace StalkerSaveEditor.Core.Formats.XRay;

public enum XRayTaskState
{
    Failed = 0,
    InProgress = 1,
    Completed = 2,
    Skipped = 3,
    Unknown = -1,
}

/// <summary>One PDA task of the actor. Times are the save's game-time values (milliseconds, 0 = not set).</summary>
public sealed record XRayTask(
    string Id,
    string Title,
    XRayTaskState State,
    ulong ReceiveTime,
    ulong FinishTime,
    IReadOnlyList<XRayTaskObjective> Objectives);

public sealed record XRayTaskObjective(int Index, string Description, XRayTaskState State);

/// <summary>One line of the actor statistics (PDA "Statistics"): e.g. section "stalkerkills", key "bandit_novice".</summary>
public sealed record XRayStatisticLine(string Section, string Key, int Count, int Points);

public sealed record XRayProgress(IReadOnlyList<XRayTask> Tasks, IReadOnlyList<XRayStatisticLine> Statistics)
{
    /// <summary>
    /// False when only the statistics were read and the task registry was not found: <see cref="Tasks"/> is then
    /// empty because it is unknown, not because the player has no tasks.
    /// </summary>
    public bool TasksKnown { get; init; } = true;

    public int Count(string section) => Statistics.Where(line => line.Section == section).Sum(line => line.Count);
}

/// <summary>
/// Reads the actor's game tasks and statistics — the last two registries of the ALife registry chunk (9):
/// CGameTaskRegistry then CActorStatisticRegistry, in every trilogy game (alife_registry_container_composition.h).
/// Earlier registries (news, map spots…) are not parsed; the task registry is located as the offset from which
/// both registries parse completely and end exactly at the chunk end, so a wrong guess cannot pass.
/// Formats: SoC SGameTaskKey/SGameTaskObjective (1.0006), CS/CoP CGameTask::save_task (1.5.10 / 1.6.02).
/// </summary>
public static class XRayProgressReader
{
    private const int MaxString = 4096;
    private const int MaxCount = 100_000;

    public static XRayProgress? Read(XRayTrilogySave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var registry = save.Container.Chunks.Where(chunk => chunk.Type == 9).ToArray();
        if (registry.Length != 1) return null;
        return Parse(
            registry[0].Data.Span,
            save.FormatId.StartsWith("stalker-soc", StringComparison.Ordinal),
            save.ActorId,
            save.RelationRegistry?.InfoSectionEnd ?? 0);
    }

    internal static XRayProgress? Parse(ReadOnlySpan<byte> data, bool soc, ushort actorId, int from)
    {

        // 1) The statistics registry: the offset from which it parses exactly to the end of the chunk.
        var statisticsStart = -1;
        List<XRayStatisticLine>? statistics = null;
        for (var start = data.Length - 10; start >= from && statistics is null; start--)
        {
            if (!ActorRegistryPrefix(data, start, actorId)) continue;
            var reader = new Reader(data, start);
            try
            {
                var lines = ReadStatistics(ref reader, actorId);
                if (reader.Position == data.Length)
                {
                    statistics = lines;
                    statisticsStart = start;
                }
            }
            catch (FormatException)
            {
            }
        }

        if (statistics is null)
        {
            // Call of Pripyat has no statistics registry: the task registry is the last one.
            statistics = [];
            statisticsStart = data.Length;
        }

        // 2) The task registry: parses completely and ends at the statistics, allowing the short trailer SoC
        //    keeps after the task vector (at most 128 bytes).
        for (var start = statisticsStart - 10; start >= from; start--)
        {
            if (!ActorRegistryPrefix(data, start, actorId)) continue;
            // Enhanced Edition CoP appends one byte to every task record.
            foreach (var trailingByte in soc ? [false] : new[] { false, true })
            {
                var reader = new Reader(data, start);
                try
                {
                    var tasks = ReadTasks(ref reader, soc, actorId, trailingByte);
                    var gap = statisticsStart - reader.Position;
                    if (gap is >= 0 and <= 128) return new XRayProgress(tasks, statistics);
                }
                catch (FormatException)
                {
                }
            }
        }

        return statistics.Count > 0 ? new XRayProgress([], statistics) { TasksKnown = false } : null;
    }

    private static bool ActorRegistryPrefix(ReadOnlySpan<byte> data, int start, ushort actorId)
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[start..]);
        return count is > 0 and <= 16 && BinaryPrimitives.ReadUInt16LittleEndian(data[(start + 4)..]) == actorId;
    }

    private static List<XRayTask> ReadTasks(ref Reader reader, bool soc, ushort actorId, bool trailingByte)
    {
        var result = new List<XRayTask>();
        var objects = reader.Count();
        for (var o = 0; o < objects; o++)
        {
            var owner = reader.U16();
            var keys = reader.Count();
            for (var k = 0; k < keys; k++)
            {
                var task = soc ? ReadSocTask(ref reader) : ReadCsTask(ref reader);
                if (trailingByte) _ = reader.U8();
                if (owner == actorId) result.Add(task);
            }
        }

        return result;
    }

    private static XRayTask ReadSocTask(ref Reader reader)
    {
        var id = reader.Str();
        var receive = reader.U64();
        var finish = reader.U64();
        _ = reader.U64();
        var title = reader.Str();
        var count = reader.Count();
        var objectives = new List<XRayTaskObjective>();
        for (var i = 0; i < count; i++)
        {
            var index = reader.I32();
            var state = reader.I32();
            var description = reader.Str();
            _ = reader.Str();          // map location
            _ = reader.U16();          // object id
            _ = reader.I32();          // task state (repeated)
            _ = reader.U8();           // default location enabled
            _ = reader.Str();          // map hint
            _ = reader.Str();          // icon texture
            reader.Skip(16);           // icon rect
            _ = reader.Str();          // article id
            for (var v = 0; v < 4; v++) reader.StrVector();
            if (reader.U8() != 0)
            {
                for (var v = 0; v < 4; v++) reader.StrVector();
            }

            objectives.Add(new XRayTaskObjective(index, description, ToState(state)));
        }

        // SoC has no task-level state: objective 0 is the task itself.
        var taskState = objectives.Count > 0 ? objectives[0].State : XRayTaskState.Unknown;
        return new XRayTask(id, title, taskState, receive, finish, objectives);
    }

    private static XRayTask ReadCsTask(ref Reader reader)
    {
        var id = reader.Str();
        var state = reader.I32();
        _ = reader.I32();              // task type
        var receive = reader.U64();
        var finish = reader.U64();
        _ = reader.U64();              // time to complete
        _ = reader.U64();              // timer finish
        var title = reader.Str();
        _ = reader.Str();              // description
        for (var v = 0; v < 4; v++) reader.StrVector();
        _ = reader.Str();              // icon texture
        _ = reader.Str();              // map hint
        _ = reader.Str();              // map location
        _ = reader.U16();              // map object id
        _ = reader.U32();              // priority
        return new XRayTask(id, title, ToState(state), receive, finish, []);
    }

    private static List<XRayStatisticLine> ReadStatistics(ref Reader reader, ushort actorId)
    {
        var result = new List<XRayStatisticLine>();
        var objects = reader.Count();
        for (var o = 0; o < objects; o++)
        {
            var owner = reader.U16();
            var sections = reader.Count();
            for (var s = 0; s < sections; s++)
            {
                var details = reader.Count();
                var lines = new List<(string Key, int Count, int Points)>();
                for (var d = 0; d < details; d++)
                {
                    var key = reader.Str();
                    var count = reader.I32();
                    var points = reader.I32();
                    _ = reader.Str();
                    lines.Add((key, count, points));
                }

                var section = reader.Str();
                if (owner == actorId) result.AddRange(lines.Select(line => new XRayStatisticLine(section, line.Key, line.Count, line.Points)));
            }
        }

        return result;
    }

    private static XRayTaskState ToState(int value) => value switch
    {
        0 => XRayTaskState.Failed,
        1 => XRayTaskState.InProgress,
        2 => XRayTaskState.Completed,
        3 => XRayTaskState.Skipped,
        _ => XRayTaskState.Unknown,
    };

    private ref struct Reader(ReadOnlySpan<byte> data, int position)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; } = position;

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || Position + length > _data.Length) throw new FormatException("truncated");
            var slice = _data.Slice(Position, length);
            Position += length;
            return slice;
        }

        public void Skip(int length) => Take(length);

        public byte U8() => Take(1)[0];

        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

        public int Count()
        {
            var value = U32();
            if (value > MaxCount) throw new FormatException("count");
            return (int)value;
        }

        public string Str()
        {
            var rest = _data[Position..];
            var end = rest[..Math.Min(rest.Length, MaxString)].IndexOf((byte)0);
            if (end < 0) throw new FormatException("string");
            foreach (var b in rest[..end])
            {
                if (b < 0x20) throw new FormatException("control character");
            }

            var text = Encoding.Latin1.GetString(rest[..end]);
            Position += end + 1;
            return text;
        }

        public void StrVector()
        {
            var count = Count();
            for (var i = 0; i < count; i++) _ = Str();
        }
    }
}
