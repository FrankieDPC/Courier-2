using System;
using System.Collections.Generic;

namespace Farm.Sim
{
    /// <summary>A bot's state after a given tick of the previewed day.</summary>
    public readonly struct PreviewFrame
    {
        public readonly int Tick;
        public readonly Cell Position;
        public readonly Facing Facing;
        public readonly Item LoadMain;
        public readonly int LoadWeight;
        public readonly int Capacity;
        public readonly CommandResult Result;
        public readonly ExecPoint[] Path;

        public PreviewFrame(Bot b, int tick, bool initial)
        {
            Tick = tick;
            Position = b.Position;
            Facing = b.Facing;
            LoadMain = b.Load.Main;
            LoadWeight = b.Load.Weight;
            Capacity = b.Capacity;
            Result = initial ? CommandResult.None : b.LastResult;
            var path = new ExecPoint[initial ? 0 : b.LastPath.Count];
            for (int i = 0; i < path.Length; i++) path[i] = b.LastPath[i];
            Path = path;
        }

        /// <summary>Line run in a routine on this tick (deepest match), or -1. MacroId -1 = the bot's own routine.</summary>
        public int IndexIn(int macroId)
        {
            for (int i = Path.Length - 1; i >= 0; i--)
                if (Path[i].MacroId == macroId) return Path[i].Index;
            return -1;
        }
    }

    /// <summary>A command that didn't do its job, grouped by where it is and what went wrong.</summary>
    public sealed class PreviewProblem
    {
        public ExecPoint Point;
        public Command Command;
        public CommandResult Result;
        public int Count;
        public int FirstTick;
    }

    /// <summary>
    /// "What would happen today?" Runs the rest of the current day (or the whole next day, if planning)
    /// on a copy of the farm and records every bot's moves, tasks and problems, plus what sells overnight.
    /// The real farm is untouched. The sim is deterministic, so the preview is exact.
    /// </summary>
    public sealed class DayPreview
    {
        const int SafetyTicks = 5000;

        readonly Dictionary<int, List<PreviewFrame>> _frames = new Dictionary<int, List<PreviewFrame>>();
        readonly Dictionary<int, Dictionary<CommandType, int>> _tasks = new Dictionary<int, Dictionary<CommandType, int>>();
        readonly Dictionary<int, List<PreviewProblem>> _problems = new Dictionary<int, List<PreviewProblem>>();
        readonly Dictionary<int, Inventory> _emptied = new Dictionary<int, Inventory>();

        public int Day { get; private set; }
        public int StartTick { get; private set; }
        public int EndTick { get; private set; }

        /// <summary>The tick work stopped and bots started walking home (-1 if the preview started after that).</summary>
        public int DuskTick { get; private set; } = -1;

        /// <summary>Overnight results: sales, crops watered vs dry.</summary>
        public DayReport Night { get; private set; }

        public static DayPreview Run(World source)
        {
            var preview = new DayPreview();
            var world = source.Clone();
            preview.Day = world.Day;
            if (world.IsGameOver) return preview;

            if (world.Phase == DayPhase.Planning) world.StartDay();
            preview.StartTick = preview.EndTick = world.Tick;
            if (world.IsDusk) preview.DuskTick = world.Tick;

            foreach (var b in world.Bots)
            {
                preview._frames[b.Id] = new List<PreviewFrame> { new PreviewFrame(b, world.Tick, true) };
                preview._tasks[b.Id] = new Dictionary<CommandType, int>();
                preview._problems[b.Id] = new List<PreviewProblem>();
                preview._emptied[b.Id] = new Inventory();
            }

            world.TaskDone += (b, type, cell) =>
            {
                var t = preview._tasks[b.Id];
                t.TryGetValue(type, out int n);
                t[type] = n + 1;
            };
            world.LoadEmptied += (b, load) =>
            {
                foreach (var a in load.ToList()) preview._emptied[b.Id].Add(a.Item, a.Amount);
            };
            world.DuskStarted += () => preview.DuskTick = world.Tick;
            world.DayEnded += r => preview.Night = r;
            world.Ticked += () =>
            {
                preview.EndTick = world.Tick;
                foreach (var b in world.Bots) preview.Record(world, b);
            };

            int guard = 0;
            while (world.Phase != DayPhase.Planning && guard++ < SafetyTicks) world.Step();
            return preview;
        }

        public IReadOnlyList<PreviewFrame> FramesFor(int botId) =>
            _frames.TryGetValue(botId, out var f) ? f : (IReadOnlyList<PreviewFrame>)Array.Empty<PreviewFrame>();

        /// <summary>How many times the bot completed each kind of task.</summary>
        public int TasksDone(int botId, CommandType type) =>
            _tasks.TryGetValue(botId, out var t) && t.TryGetValue(type, out int n) ? n : 0;

        /// <summary>Problems for one bot, most frequent first.</summary>
        public IReadOnlyList<PreviewProblem> ProblemsFor(int botId) =>
            _problems.TryGetValue(botId, out var p) ? p : (IReadOnlyList<PreviewProblem>)Array.Empty<PreviewProblem>();

        /// <summary>What the bot emptied out when it got home at dusk.</summary>
        public Inventory EmptiedAtDusk(int botId) => _emptied.TryGetValue(botId, out var e) ? e : new Inventory();

        public bool TryGetFrame(int botId, int tick, out PreviewFrame frame)
        {
            frame = default;
            if (!_frames.TryGetValue(botId, out var frames) || frames.Count == 0) return false;
            int i = Math.Max(0, Math.Min(frames.Count - 1, tick - StartTick));
            frame = frames[i];
            return true;
        }

        void Record(World world, Bot b)
        {
            if (!_frames.TryGetValue(b.Id, out var frames)) return;
            frames.Add(new PreviewFrame(b, world.Tick, false));

            if (!CommandInfo.IsProblem(b.LastResult) || b.LastPath.Count == 0) return;
            var point = b.LastPath[b.LastPath.Count - 1];
            var list = _problems[b.Id];
            foreach (var p in list)
            {
                if (p.Point.MacroId == point.MacroId && p.Point.Index == point.Index && p.Result == b.LastResult)
                {
                    p.Count++;
                    list.Sort((x, y) => y.Count.CompareTo(x.Count));
                    return;
                }
            }
            list.Add(new PreviewProblem
            {
                Point = point,
                Command = CommandAt(world, b, point),
                Result = b.LastResult,
                Count = 1,
                FirstTick = world.Tick,
            });
        }

        static Command CommandAt(World world, Bot b, ExecPoint point)
        {
            var routine = point.IsMain ? b.Routine : world.Library.Get(point.MacroId)?.Routine;
            return routine != null && point.Index >= 0 && point.Index < routine.Count ? routine[point.Index] : default;
        }
    }
}
