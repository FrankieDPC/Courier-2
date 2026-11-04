using System;
using System.Collections.Generic;

namespace Wizard.Sim
{
    /// <summary>A construct's state after a given tick of the previewed day.</summary>
    public readonly struct PreviewFrame
    {
        public readonly int Tick;
        public readonly Cell Position;
        public readonly Facing Facing;
        public readonly Resource Cargo;
        public readonly int CargoCount;
        public readonly CommandResult Result;
        public readonly ExecPoint[] Path;

        public PreviewFrame(Construct c, int tick, bool initial)
        {
            Tick = tick;
            Position = c.Position;
            Facing = c.Facing;
            Cargo = c.Cargo;
            CargoCount = c.CargoCount;
            Result = initial ? CommandResult.None : c.LastResult;
            var path = new ExecPoint[initial ? 0 : c.LastPath.Count];
            for (int i = 0; i < path.Length; i++) path[i] = c.LastPath[i];
            Path = path;
        }

        /// <summary>Line run in a routine on this tick (deepest match), or -1. SpellId -1 = the construct's own routine.</summary>
        public int IndexIn(int spellId)
        {
            for (int i = Path.Length - 1; i >= 0; i--)
                if (Path[i].SpellId == spellId) return Path[i].Index;
            return -1;
        }
    }

    /// <summary>A command that didn't do its job, grouped by where it is and what went wrong.</summary>
    public sealed class PreviewProblem
    {
        public ExecPoint Point;   // deepest point: a line in the construct's routine or in a spell
        public Command Command;
        public CommandResult Result;
        public int Count;
        public int FirstTick;
    }

    public readonly struct PreviewDelivery
    {
        public readonly string Target;
        public readonly Resource Resource;
        public readonly int Amount;

        public PreviewDelivery(string target, Resource resource, int amount)
        {
            Target = target;
            Resource = resource;
            Amount = amount;
        }
    }

    public readonly struct PreviewJob
    {
        public readonly string Title;
        public readonly int Tick;
        public readonly IReadOnlyList<ResourceAmount> Reward;

        public PreviewJob(string title, int tick, IReadOnlyList<ResourceAmount> reward)
        {
            Title = title;
            Tick = tick;
            Reward = reward;
        }
    }

    /// <summary>
    /// "What would happen today?" Runs the rest of the current day (or the whole next day, if
    /// planning) on a copy of the world and records every construct's moves, hand-offs and problems.
    /// The real world is untouched. Because the sim is deterministic, the preview is exact as long as
    /// nothing changes before the day actually runs.
    /// </summary>
    public sealed class DayPreview
    {
        const int SafetyTicks = 5000;

        readonly Dictionary<int, List<PreviewFrame>> _frames = new Dictionary<int, List<PreviewFrame>>();
        readonly Dictionary<int, List<PreviewDelivery>> _deliveries = new Dictionary<int, List<PreviewDelivery>>();
        readonly Dictionary<int, List<PreviewProblem>> _problems = new Dictionary<int, List<PreviewProblem>>();
        readonly List<PreviewJob> _jobsDone = new List<PreviewJob>();
        readonly Dictionary<int, List<ResourceAmount>> _banked = new Dictionary<int, List<ResourceAmount>>();

        public int Day { get; private set; }
        public bool IsDoomDay { get; private set; }

        /// <summary>The tick the preview starts from (0 when planning; the current tick when paused mid-day).</summary>
        public int StartTick { get; private set; }

        /// <summary>The tick the day ends on (after everyone has walked home).</summary>
        public int EndTick { get; private set; }

        /// <summary>The tick work stopped and constructs started walking home (-1 if it never did, e.g. the doom day).</summary>
        public int DuskTick { get; private set; } = -1;

        public IReadOnlyList<PreviewJob> JobsDone => _jobsDone;

        public static DayPreview Run(World source)
        {
            var preview = new DayPreview();
            var world = source.Clone();
            preview.Day = world.Day;
            preview.IsDoomDay = world.IsDoomDay;
            if (world.IsGameOver) return preview;

            if (world.Phase == DayPhase.Planning) world.StartDay();
            preview.StartTick = world.Tick;
            preview.EndTick = world.Tick;
            foreach (var c in world.Constructs)
            {
                preview._frames[c.Id] = new List<PreviewFrame> { new PreviewFrame(c, world.Tick, true) };
                preview._deliveries[c.Id] = new List<PreviewDelivery>();
                preview._problems[c.Id] = new List<PreviewProblem>();
                preview._banked[c.Id] = new List<ResourceAmount>();
            }
            if (world.IsDusk) preview.DuskTick = world.Tick;

            int tickNow = world.Tick;
            world.Delivered += (c, b, r, n) => preview.AddDelivery(c.Id, b.Name, r, n);
            world.JobFinished += j =>
            {
                if (j.State == JobState.Done) preview._jobsDone.Add(new PreviewJob(j.Title, tickNow + 1, j.Reward));
            };
            world.DuskStarted += () => preview.DuskTick = world.Tick;
            world.CargoBanked += (c, r, n) =>
            {
                if (preview._banked.TryGetValue(c.Id, out var list)) list.Add(new ResourceAmount(r, n));
            };
            world.Ticked += () =>
            {
                tickNow = world.Tick;
                preview.EndTick = world.Tick;
                foreach (var c in world.Constructs) preview.Record(world, c);
            };

            int guard = 0;
            while (world.Phase != DayPhase.Planning && guard++ < SafetyTicks)
            {
                tickNow = world.Tick;
                world.Step();
            }
            return preview;
        }

        public IReadOnlyList<PreviewFrame> FramesFor(int constructId) =>
            _frames.TryGetValue(constructId, out var f) ? f : (IReadOnlyList<PreviewFrame>)Array.Empty<PreviewFrame>();

        /// <summary>What the construct was still carrying when work stopped (emptied into the tower at dusk).</summary>
        public IReadOnlyList<ResourceAmount> BankedAtDuskFor(int constructId) =>
            _banked.TryGetValue(constructId, out var b) ? b : (IReadOnlyList<ResourceAmount>)Array.Empty<ResourceAmount>();

        public IReadOnlyList<PreviewDelivery> DeliveriesFor(int constructId) =>
            _deliveries.TryGetValue(constructId, out var d) ? d : (IReadOnlyList<PreviewDelivery>)Array.Empty<PreviewDelivery>();

        /// <summary>Problems for one construct, most frequent first.</summary>
        public IReadOnlyList<PreviewProblem> ProblemsFor(int constructId) =>
            _problems.TryGetValue(constructId, out var p) ? p : (IReadOnlyList<PreviewProblem>)Array.Empty<PreviewProblem>();

        /// <summary>State after <paramref name="tick"/> (clamped to the previewed range).</summary>
        public bool TryGetFrame(int constructId, int tick, out PreviewFrame frame)
        {
            frame = default;
            if (!_frames.TryGetValue(constructId, out var frames) || frames.Count == 0) return false;
            int i = Math.Max(0, Math.Min(frames.Count - 1, tick - StartTick));
            frame = frames[i];
            return true;
        }

        void Record(World world, Construct c)
        {
            if (!_frames.TryGetValue(c.Id, out var frames)) return; // summoned mid-preview: can't happen, but be safe
            frames.Add(new PreviewFrame(c, world.Tick, false));

            if (!CommandInfo.IsProblem(c.LastResult) || c.LastPath.Count == 0) return;
            var point = c.LastPath[c.LastPath.Count - 1];
            var list = _problems[c.Id];
            foreach (var p in list)
            {
                if (p.Point.SpellId == point.SpellId && p.Point.Index == point.Index && p.Result == c.LastResult)
                {
                    p.Count++;
                    list.Sort((a, b) => b.Count.CompareTo(a.Count));
                    return;
                }
            }
            list.Add(new PreviewProblem
            {
                Point = point,
                Command = CommandAt(world, c, point),
                Result = c.LastResult,
                Count = 1,
                FirstTick = world.Tick,
            });
        }

        static Command CommandAt(World world, Construct c, ExecPoint point)
        {
            var routine = point.IsMain ? c.Routine : world.Spellbook.Get(point.SpellId)?.Routine;
            return routine != null && point.Index >= 0 && point.Index < routine.Count ? routine[point.Index] : default;
        }

        void AddDelivery(int constructId, string target, Resource r, int n)
        {
            var list = _deliveries[constructId];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Target == target && list[i].Resource == r)
                {
                    list[i] = new PreviewDelivery(target, r, list[i].Amount + n);
                    return;
                }
            }
            list.Add(new PreviewDelivery(target, r, n));
        }
    }
}
