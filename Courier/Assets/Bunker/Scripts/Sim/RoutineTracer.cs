using System.Collections.Generic;

namespace Bunker.Sim
{
    public struct TraceStep
    {
        public Command Command;
        public Cell Position;   // after this tick
        public Facing Facing;   // after this tick
        public bool Blocked;    // a Step into something solid, or a Go to with no route
        public bool CantDo;     // a job this kind of bot can't do (it fails and the routine moves on)
        public int Depth;       // 0 = the bot's own routine, 1+ = inside custom commands
    }

    public sealed class TraceResult
    {
        public readonly List<TraceStep> Steps = new List<TraceStep>();
        public readonly List<Cell> PickedTiles = new List<Cell>();
        public Cell Start;
        public Facing StartFacing;
        public Cell End;
        public Facing EndFacing;
        public int BlockedCount;

        /// <summary>Jobs in the routine (custom commands included) that this kind of bot can't do.</summary>
        public int CantDoCount;
        public bool HitDepthLimit;

        /// <summary>Ticks one pass takes if every task succeeds (Run is free).</summary>
        public int Ticks => Steps.Count;

        public bool ReturnsToStart => End == Start && EndFacing == StartFacing;
    }

    /// <summary>
    /// Dry-runs a routine's movement and timing on the map (custom commands expanded) without touching
    /// the surface. Drives the route line and the footer hints. Tiles the routine clears count as open
    /// for later Steps (not for Go to, which routes over the map as it is now). For the exact day, use
    /// DayPreview.
    /// </summary>
    public static class RoutineTracer
    {
        const int MaxSteps = 4000;

        sealed class State
        {
            public World World;
            public BotKind Kind;
            public Cell Pos;
            public Facing Dir;
            public readonly HashSet<Cell> Cleared = new HashSet<Cell>();
        }

        public static TraceResult Trace(World world, Cell start, Facing facing, IReadOnlyList<Command> routine,
            BotKind kind = BotKind.Farmhand)
        {
            var result = new TraceResult { Start = start, StartFacing = facing };
            var st = new State { World = world, Kind = kind, Pos = start, Dir = facing };
            Walk(st, routine, 0, result);
            result.End = st.Pos;
            result.EndFacing = st.Dir;
            return result;
        }

        /// <summary>One loop of the bot's routine starting where it stands (at the hatch each morning; mid-day, where an edited bot restarts).</summary>
        public static TraceResult Trace(World world, Bot b) => Trace(world, b.Position, b.Facing, b.Routine, b.Kind);

        static bool Open(State st, Cell c) => st.World.Map.IsWalkable(c) ||
                                              (st.Cleared.Contains(c) && st.World.Map[c].Structure == Structure.None && !st.World.Map[c].IsWater);

        static void Walk(State st, IReadOnlyList<Command> routine, int depth, TraceResult result)
        {
            var world = st.World;
            var map = world.Map;
            for (int i = 0; i < routine.Count; i++)
            {
                if (result.Steps.Count >= MaxSteps) return;
                var c = routine[i];

                if (!BotKinds.CanDo(st.Kind, c))
                {
                    // The real bot refuses it in a single tick and moves on.
                    result.CantDoCount++;
                    result.Steps.Add(new TraceStep { Command = c, Position = st.Pos, Facing = st.Dir, Depth = depth, CantDo = true });
                    continue;
                }

                switch (c.Type)
                {
                    case CommandType.Run:
                    {
                        var macro = world.Library.Get(c.MacroId);
                        if (macro == null) continue;
                        if (depth >= CommandLibrary.MaxDepth)
                        {
                            result.HitDepthLimit = true;
                            continue;
                        }
                        Walk(st, macro.Routine, depth + 1, result);
                        continue;
                    }

                    case CommandType.GoTo:
                    {
                        if (c.NeedsTile) result.PickedTiles.Add(c.Tile);
                        var dist = world.DistanceTo(c.Target, c.Tile);
                        int d = map.InBounds(st.Pos) ? dist[st.Pos.X, st.Pos.Y] : -1;
                        if (d < 0)
                        {
                            result.BlockedCount++;
                            Add(result, c, st, depth, true);
                            continue;
                        }
                        if (d == 0)
                        {
                            FaceTarget(st, c.Target);
                            Add(result, c, st, depth, false);
                            continue;
                        }
                        int perTile = BotKinds.Stats(st.Kind).TicksPerTile;
                        while (Pathing.NextStep(map, dist, st.Pos, out var next, out var stepDir) && result.Steps.Count < MaxSteps)
                        {
                            st.Pos = next;
                            st.Dir = stepDir;
                            for (int k = 1; k < perTile; k++) Add(result, c, st, depth, false); // crossing the tile
                            if (dist[next.X, next.Y] == 0) FaceTarget(st, c.Target);
                            Add(result, c, st, depth, false);
                        }
                        continue;
                    }

                    case CommandType.Step:
                    {
                        st.Dir = c.Dir;
                        var next = st.Pos.Step(st.Dir);
                        bool blocked = !Open(st, next);
                        if (blocked) result.BlockedCount++;
                        else
                        {
                            st.Pos = next;
                            for (int k = 1; k < BotKinds.Stats(st.Kind).TicksPerTile; k++) Add(result, c, st, depth, false);
                        }
                        Add(result, c, st, depth, blocked);
                        continue;
                    }

                    case CommandType.Face:
                        st.Dir = c.Dir;
                        Add(result, c, st, depth, false);
                        continue;

                    case CommandType.Clear:
                    {
                        var ahead = st.Pos.Step(st.Dir);
                        var t = map.TileAt(ahead);
                        int ticks = 1;
                        if (t != null && t.IsOvergrown && !st.Cleared.Contains(ahead))
                        {
                            ticks = Balance.ClearTicks(t.Debris);
                            st.Cleared.Add(ahead);
                        }
                        for (int k = 0; k < ticks; k++) Add(result, c, st, depth, false);
                        continue;
                    }

                    default:
                    {
                        int ticks = System.Math.Max(1, CommandInfo.Duration(c.Type));
                        for (int k = 0; k < ticks; k++) Add(result, c, st, depth, false);
                        continue;
                    }
                }
            }
        }

        static void FaceTarget(State st, GoTarget target)
        {
            if (target != GoTarget.Tile && Pathing.TryFace(st.World.Map, st.Pos, target, out var dir)) st.Dir = dir;
        }

        static void Add(TraceResult r, Command c, State st, int depth, bool blocked) =>
            r.Steps.Add(new TraceStep { Command = c, Position = st.Pos, Facing = st.Dir, Blocked = blocked, Depth = depth });
    }
}
