using System;
using System.Collections.Generic;

namespace Bunker.Sim
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

        public PreviewFrame(Bot b, int tick, bool initial, ExecPoint[] reuse = null)
        {
            Tick = tick;
            Position = b.Position;
            Facing = b.Facing;
            LoadMain = b.Load.Main;
            LoadWeight = b.Load.Weight;
            Capacity = b.Capacity;
            Result = initial ? CommandResult.None : b.LastResult;
            int n = initial ? 0 : b.LastPath.Count;
            if (reuse != null && reuse.Length == n && SamePath(reuse, b.LastPath))
            {
                Path = reuse;
                return;
            }
            var path = new ExecPoint[n];
            for (int i = 0; i < n; i++) path[i] = b.LastPath[i];
            Path = path;
        }

        static bool SamePath(ExecPoint[] a, IReadOnlyList<ExecPoint> b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i].MacroId != b[i].MacroId || a[i].Index != b[i].Index) return false;
            return true;
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
    /// "What would happen today?" Runs the rest of the current day, up to and including the night,
    /// on a copy of the surface and records every bot's moves, jobs and problems, plus the night's results.
    /// Your robot stands still in the copy. The real surface is untouched. The sim is deterministic, so the preview is exact.
    /// </summary>
    public sealed class DayPreview
    {
        const int SafetyTicks = 20000;

        readonly Dictionary<int, List<PreviewFrame>> _frames = new Dictionary<int, List<PreviewFrame>>();
        readonly Dictionary<int, Dictionary<CommandType, int>> _tasks = new Dictionary<int, Dictionary<CommandType, int>>();
        readonly Dictionary<int, List<PreviewProblem>> _problems = new Dictionary<int, List<PreviewProblem>>();
        readonly Dictionary<int, Inventory> _emptied = new Dictionary<int, Inventory>();

        public int Day { get; private set; }
        public int StartTick { get; private set; }
        public int EndTick { get; private set; }

        /// <summary>Nightfall results: deliveries, crops ripened today, and crops in wet vs dry soil.</summary>
        public DayReport Night { get; private set; }

        /// <param name="restarting">Bots that will restart their routine from line 1 when time runs again (edited mid-day).</param>
        public static DayPreview Run(World source, Func<Bot, bool> restarting = null)
        {
            var preview = new DayPreview();
            var world = source.Clone();
            preview.Day = world.Day;
            if (world.IsGameOver) return preview;
            if (restarting != null)
            {
                for (int i = 0; i < source.Bots.Count; i++)
                    if (restarting(source.Bots[i])) world.RestartRoutine(world.Bots[i]);
            }

            preview.StartTick = preview.EndTick = world.Tick;

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
            bool night = false;
            world.DayEnded += r =>
            {
                preview.Night = r;
                night = true;
            };
            world.Ticked += () =>
            {
                preview.EndTick = world.Tick;
                foreach (var b in world.Bots) preview.Record(world, b);
            };

            int guard = 0;
            while (!night && !world.IsGameOver && guard++ < SafetyTicks) world.Step();

            // Most frequent problems first (stable, so ties keep the order they first happened in).
            foreach (var list in preview._problems.Values)
            {
                var sorted = new List<PreviewProblem>(list);
                sorted.Sort((x, y) => y.Count != x.Count ? y.Count.CompareTo(x.Count) : x.FirstTick.CompareTo(y.FirstTick));
                list.Clear();
                list.AddRange(sorted);
            }
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

        /// <summary>What the bot handed in when night fell.</summary>
        public Inventory EmptiedAtNight(int botId) => _emptied.TryGetValue(botId, out var e) ? e : new Inventory();

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
            frames.Add(new PreviewFrame(b, world.Tick, false, frames.Count > 0 ? frames[frames.Count - 1].Path : null));

            if (!CommandInfo.IsProblem(b.LastResult) || b.LastPath.Count == 0) return;
            var point = b.LastPath[b.LastPath.Count - 1];
            var list = _problems[b.Id];
            foreach (var p in list)
            {
                if (p.Point.MacroId == point.MacroId && p.Point.Index == point.Index && p.Result == b.LastResult)
                {
                    p.Count++;
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
