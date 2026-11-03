using System.Collections.Generic;

namespace Courier.Sim
{
    public struct TraceStep
    {
        public int CommandIndex;
        public Command Command;
        public Cell Position;   // after the command
        public Facing Facing;   // after the command
        public bool Blocked;    // a MoveForward that hit a non-road cell
    }

    public sealed class TraceResult
    {
        public readonly List<TraceStep> Steps = new List<TraceStep>();
        public Cell Start;
        public Facing StartFacing;
        public Cell End;
        public Facing EndFacing;
        public int BlockedCount;

        /// <summary>True when one pass ends where it began, so looping repeats the same route.</summary>
        public bool ReturnsToStart => End == Start && EndFacing == StartFacing;
    }

    /// <summary>
    /// Dry-runs a schedule's movement on the grid without touching the town. Drives the
    /// route preview and the "does this loop close?" hint in the editor.
    /// </summary>
    public static class ScheduleTracer
    {
        public static TraceResult Trace(TownGrid grid, Cell start, Facing facing, IReadOnlyList<Command> commands)
        {
            var result = new TraceResult { Start = start, StartFacing = facing };
            var pos = start;
            var dir = facing;

            for (int i = 0; i < commands.Count; i++)
            {
                var c = commands[i];
                bool blocked = false;
                switch (c.Type)
                {
                    case CommandType.MoveForward:
                        var next = pos.Step(dir);
                        if (grid.IsDrivable(next)) pos = next;
                        else blocked = true;
                        break;
                    case CommandType.TurnLeft: dir = dir.TurnLeft(); break;
                    case CommandType.TurnRight: dir = dir.TurnRight(); break;
                    case CommandType.TurnAround: dir = dir.Reverse(); break;
                }

                if (blocked) result.BlockedCount++;
                result.Steps.Add(new TraceStep
                {
                    CommandIndex = i,
                    Command = c,
                    Position = pos,
                    Facing = dir,
                    Blocked = blocked,
                });
            }

            result.End = pos;
            result.EndFacing = dir;
            return result;
        }

        public static TraceResult Trace(TownGrid grid, CourierBot bot) =>
            Trace(grid, bot.Home, bot.HomeFacing, bot.Schedule);
    }
}
