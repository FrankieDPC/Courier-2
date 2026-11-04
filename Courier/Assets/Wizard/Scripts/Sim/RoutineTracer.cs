using System.Collections.Generic;

namespace Wizard.Sim
{
    public struct TraceStep
    {
        public Command Command;
        public Cell Position;   // after the command
        public Facing Facing;   // after the command
        public bool Blocked;    // a MoveForward that hit a non-road cell
        public int Depth;       // 0 = the construct's own routine, 1+ = inside spells
    }

    public sealed class TraceResult
    {
        public readonly List<TraceStep> Steps = new List<TraceStep>();
        public Cell Start;
        public Facing StartFacing;
        public Cell End;
        public Facing EndFacing;
        public int BlockedCount;
        public bool HitDepthLimit;

        /// <summary>Ticks one pass takes (casts are free).</summary>
        public int Ticks => Steps.Count;

        /// <summary>True when one pass ends where it began, so looping repeats the same route.</summary>
        public bool ReturnsToStart => End == Start && EndFacing == StartFacing;
    }

    /// <summary>
    /// Dry-runs a routine's movement on the grid, expanding spells inline, without touching the world.
    /// Drives the route preview and the "does this loop close?" hint.
    /// </summary>
    public static class RoutineTracer
    {
        const int MaxSteps = 4000;

        public static TraceResult Trace(TownGrid grid, Spellbook book, Cell start, Facing facing, IReadOnlyList<Command> routine)
        {
            var result = new TraceResult { Start = start, StartFacing = facing };
            var pos = start;
            var dir = facing;
            Walk(grid, book, routine, 0, ref pos, ref dir, result);
            result.End = pos;
            result.EndFacing = dir;
            return result;
        }

        public static TraceResult Trace(World world, Construct c) =>
            Trace(world.Grid, world.Spellbook, c.Home, c.HomeFacing, c.Routine);

        static void Walk(TownGrid grid, Spellbook book, IReadOnlyList<Command> routine, int depth,
            ref Cell pos, ref Facing dir, TraceResult result)
        {
            for (int i = 0; i < routine.Count; i++)
            {
                if (result.Steps.Count >= MaxSteps) return;
                var c = routine[i];

                if (c.IsCast)
                {
                    var spell = book.Get(c.SpellId);
                    if (spell == null) continue;
                    if (depth >= Spellbook.MaxDepth)
                    {
                        result.HitDepthLimit = true;
                        continue;
                    }
                    Walk(grid, book, spell.Routine, depth + 1, ref pos, ref dir, result);
                    continue;
                }

                if (c.Type == CommandType.MoveForward)
                {
                    // One step per tick until the mode says stop (or the road does).
                    while (result.Steps.Count < MaxSteps)
                    {
                        var next = pos.Step(dir);
                        bool stuck = !grid.IsDrivable(next);
                        if (!stuck) pos = next;
                        else result.BlockedCount++;
                        result.Steps.Add(new TraceStep { Command = c, Position = pos, Facing = dir, Blocked = stuck, Depth = depth });
                        if (stuck || grid.ForwardStopsAt(pos, dir, c.Mode)) break;
                    }
                    continue;
                }

                switch (c.Type)
                {
                    case CommandType.TurnLeft: dir = dir.TurnLeft(); break;
                    case CommandType.TurnRight: dir = dir.TurnRight(); break;
                    case CommandType.TurnAround: dir = dir.Reverse(); break;
                }
                result.Steps.Add(new TraceStep { Command = c, Position = pos, Facing = dir, Depth = depth });
            }
        }
    }
}
