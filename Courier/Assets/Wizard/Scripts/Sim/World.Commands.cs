using System;

namespace Wizard.Sim
{
    // Command execution: how constructs move and trade goods with buildings.
    public sealed partial class World
    {
        readonly Cell[] _candidates = new Cell[4];

        /// <summary>A construct handed goods to a building (any building, not just the tower).</summary>
        public event Action<Construct, Building, Resource, int> Delivered;

        void RunNextCommand(Construct c)
        {
            c.PreviousPosition = c.Position;
            c.PreviousFacing = c.Facing;

            if (!c.TryFetchNext(Spellbook, out var cmd, out var error))
            {
                c.LastResult = error == CommandResult.None ? CommandResult.Idle : error;
                if (CommandInfo.IsProblem(c.LastResult)) c.ProblemsToday++;
                return;
            }

            c.LastResult = Execute(c, cmd);
            if (CommandInfo.IsProblem(c.LastResult)) c.ProblemsToday++;
        }

        /// <summary>Executes a single (non-Cast) command immediately. Public so tests can drive constructs directly.</summary>
        public CommandResult Execute(Construct c, Command cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.MoveForward:
                {
                    var next = c.Position.Step(c.Facing);
                    if (!Grid.IsDrivable(next)) return CommandResult.Blocked;
                    c.Position = next;
                    return CommandResult.Ok;
                }
                case CommandType.TurnLeft:
                    c.Facing = c.Facing.TurnLeft();
                    return CommandResult.Ok;
                case CommandType.TurnRight:
                    c.Facing = c.Facing.TurnRight();
                    return CommandResult.Ok;
                case CommandType.TurnAround:
                    c.Facing = c.Facing.Reverse();
                    return CommandResult.Ok;
                case CommandType.PickUp:
                    return PickUp(c, cmd.Side);
                case CommandType.DropOff:
                    return DropOff(c, cmd.Side);
                case CommandType.Wait:
                    return CommandResult.Ok;
                default:
                    return CommandResult.None;
            }
        }

        /// <summary>Cells a PickUp/DropOff will look at, in priority order.</summary>
        public static int CandidateCells(Cell pos, Facing facing, InteractSide side, Cell[] buffer)
        {
            switch (side)
            {
                case InteractSide.Ahead:
                    buffer[0] = pos.Step(facing);
                    return 1;
                case InteractSide.Left:
                    buffer[0] = pos.Step(facing.TurnLeft());
                    return 1;
                case InteractSide.Right:
                    buffer[0] = pos.Step(facing.TurnRight());
                    return 1;
                default:
                    buffer[0] = pos.Step(facing);
                    buffer[1] = pos.Step(facing.TurnLeft());
                    buffer[2] = pos.Step(facing.TurnRight());
                    buffer[3] = pos.Step(facing.Reverse());
                    return 4;
            }
        }

        CommandResult PickUp(Construct c, InteractSide side)
        {
            int n = CandidateCells(c.Position, c.Facing, side, _candidates);
            bool foundBuilding = false;

            for (int i = 0; i < n; i++)
            {
                var b = BuildingAt(_candidates[i]);
                if (b == null) continue;
                foundBuilding = true;

                var r = b.CollectableOutput(c.Cargo);
                if (r == Resource.None) continue;
                if (c.FreeCapacity <= 0) return CommandResult.CargoFull;

                c.AddCargo(r, b.Collect(r, c.FreeCapacity));
                return CommandResult.Ok;
            }

            if (!foundBuilding) return CommandResult.NoBuilding;
            return c.FreeCapacity <= 0 ? CommandResult.CargoFull : CommandResult.NothingToCollect;
        }

        CommandResult DropOff(Construct c, InteractSide side)
        {
            if (!c.IsCarrying) return CommandResult.EmptyHanded;

            int n = CandidateCells(c.Position, c.Facing, side, _candidates);
            bool foundBuilding = false;

            for (int i = 0; i < n; i++)
            {
                var b = BuildingAt(_candidates[i]);
                if (b == null) continue;
                foundBuilding = true;

                var r = c.Cargo;
                int given = Math.Min(c.CargoCount, AcceptSpace(b, r));
                if (given <= 0) continue;

                c.RemoveCargo(given);
                Receive(b, r, given);
                if (b.Type.IsTower) c.DeliveredToday += given;
                Delivered?.Invoke(c, b, r, given);
                return CommandResult.Ok;
            }

            return foundBuilding ? CommandResult.NotAccepted : CommandResult.NoBuilding;
        }

        /// <summary>How many of <paramref name="r"/> this building will take right now.</summary>
        public int AcceptSpace(Building b, Resource r)
        {
            if (r == Resource.None || r == Resource.Gold) return 0;
            if (b.Type.IsCottage) return DeliverySpace(b, r);
            return b.BufferSpace(r);
        }

        void Receive(Building b, Resource r, int amount)
        {
            if (b.Type.IsTower)
            {
                Stockpile.Add(r, amount);
                DeliveredToday.Add(r, amount);
                CreditCollectJobs(r, amount);
            }
            else if (b.Type.IsCottage)
            {
                CreditDeliverJobs(b, r, amount);
            }
            else
            {
                b.Input.Add(r, amount);
            }
        }
    }
}
