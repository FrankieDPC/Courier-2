using System.Collections.Generic;

namespace Farm.Sim
{
    public struct TraceStep
    {
        public Command Command;
        public Cell Position;   // after this tick
        public Facing Facing;   // after this tick
        public bool Blocked;    // a Step into something solid, or a Go to with no route
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
        public bool HitDepthLimit;

        /// <summary>Ticks one pass takes if every task succeeds (Run is free).</summary>
        public int Ticks => Steps.Count;

        public bool ReturnsToStart => End == Start && EndFacing == StartFacing;
    }

    /// <summary>
    /// Dry-runs a routine's movement and timing on the map (custom commands expanded) without touching
    /// the farm. Drives the route line and the footer hints. For the exact day, use DayPreview.
    /// </summary>
    public static class RoutineTracer
    {
        const int MaxSteps = 4000;

        public static TraceResult Trace(World world, Cell start, Facing facing, IReadOnlyList<Command> routine)
        {
            var result = new TraceResult { Start = start, StartFacing = facing };
            var pos = start;
            var dir = facing;
            Walk(world, routine, 0, ref pos, ref dir, result);
            result.End = pos;
            result.EndFacing = dir;
            return result;
        }

        public static TraceResult Trace(World world, Bot b) => Trace(world, b.Home, b.HomeFacing, b.Routine);

        static void Walk(World world, IReadOnlyList<Command> routine, int depth, ref Cell pos, ref Facing dir, TraceResult result)
        {
            var map = world.Map;
            for (int i = 0; i < routine.Count; i++)
            {
                if (result.Steps.Count >= MaxSteps) return;
                var c = routine[i];

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
                        Walk(world, macro.Routine, depth + 1, ref pos, ref dir, result);
                        continue;
                    }

                    case CommandType.GoTo:
                    {
                        if (c.NeedsTile) result.PickedTiles.Add(c.Tile);
                        var dist = world.DistanceTo(c.Target, c.Tile);
                        int d = map.InBounds(pos) ? dist[pos.X, pos.Y] : -1;
                        if (d < 0)
                        {
                            result.BlockedCount++;
                            Add(result, c, pos, dir, depth, true);
                            continue;
                        }
                        if (d == 0)
                        {
                            Add(result, c, pos, dir, depth, false);
                            continue;
                        }
                        while (Pathing.NextStep(map, dist, pos, out var next, out var stepDir) && result.Steps.Count < MaxSteps)
                        {
                            pos = next;
                            dir = stepDir;
                            Add(result, c, pos, dir, depth, false);
                        }
                        continue;
                    }

                    case CommandType.Step:
                    {
                        dir = c.Dir;
                        var next = pos.Step(dir);
                        bool blocked = !map.IsWalkable(next);
                        if (blocked) result.BlockedCount++;
                        else pos = next;
                        Add(result, c, pos, dir, depth, blocked);
                        continue;
                    }

                    default:
                    {
                        int ticks = System.Math.Max(1, CommandInfo.Duration(c.Type));
                        for (int t = 0; t < ticks; t++) Add(result, c, pos, dir, depth, false);
                        continue;
                    }
                }
            }
        }

        static void Add(TraceResult r, Command c, Cell pos, Facing dir, int depth, bool blocked) =>
            r.Steps.Add(new TraceStep { Command = c, Position = pos, Facing = dir, Blocked = blocked, Depth = depth });
    }
}
