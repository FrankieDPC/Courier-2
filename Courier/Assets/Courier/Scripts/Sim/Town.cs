using System;
using System.Collections.Generic;

namespace Courier.Sim
{
    public enum DayPhase
    {
        /// <summary>Between days: the player edits schedules. Time is stopped.</summary>
        Planning,

        /// <summary>The day is running; bots execute their schedules.</summary>
        Running,
    }

    public enum PlacementCheck
    {
        Ok,
        OutOfBounds,
        NotALot,
        Locked,
        Occupied,
        NoRoadAccess,
        CannotAfford,
        NotBuildable,
    }

    public sealed class DayReport
    {
        public int Day;
        public Inventory Delivered;
        public int Problems;
    }

    /// <summary>
    /// The whole simulation. Deterministic and engine-free: call <see cref="StartDay"/>, then
    /// <see cref="Step"/> once per tick until <see cref="Phase"/> returns to Planning.
    /// </summary>
    public sealed class Town
    {
        readonly List<Building> _buildings = new List<Building>();
        readonly List<CourierBot> _bots = new List<CourierBot>();
        readonly Dictionary<Cell, Building> _buildingAt = new Dictionary<Cell, Building>();
        int _nextBuildingId = 1;
        int _nextBotId = 1;

        public TownGrid Grid { get; }
        public IReadOnlyList<Building> Buildings => _buildings;
        public IReadOnlyList<CourierBot> Bots => _bots;

        public Building TownHall { get; private set; }

        /// <summary>The town's spendable goods (the town hall's input buffer).</summary>
        public Inventory Stockpile => TownHall.Input;

        /// <summary>Where new bots are parked. Must be a road cell.</summary>
        public Cell Depot { get; set; }
        public Facing DepotFacing { get; set; }

        public int Day { get; private set; } = 1;
        public int Tick { get; private set; }
        public int TicksPerDay { get; set; } = Balance.TicksPerDay;
        public DayPhase Phase { get; private set; } = DayPhase.Planning;
        public float DayProgress01 => TicksPerDay > 0 ? Tick / (float)TicksPerDay : 0f;

        /// <summary>Goods delivered to the town hall during the current day.</summary>
        public Inventory DeliveredToday { get; } = new Inventory();

        public int ExpansionLevel { get; private set; }

        public event Action<Building> BuildingAdded;
        public event Action<CourierBot> BotAdded;
        public event Action DayStarted;
        public event Action<DayReport> DayEnded;
        public event Action Ticked;

        public Town(TownGrid grid)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        // ------------------------------------------------------------------ queries

        public Building BuildingAt(Cell c) => _buildingAt.TryGetValue(c, out var b) ? b : null;

        public bool CanAfford(IReadOnlyList<ResourceAmount> cost) => TownHall != null && Stockpile.Has(cost);

        public IReadOnlyList<ResourceAmount> NextBotCost => Balance.BotCost(_bots.Count);
        public IReadOnlyList<ResourceAmount> NextExpansionCost => Balance.ExpansionCost(ExpansionLevel);

        public PlacementCheck CheckPlacement(BuildingType type, Cell cell, bool ignoreCost = false)
        {
            if (type == null || (!type.PlayerCanBuild && !ignoreCost)) return PlacementCheck.NotBuildable;
            if (!Grid.InBounds(cell)) return PlacementCheck.OutOfBounds;
            if (Grid.KindAt(cell) != CellKind.Lot) return PlacementCheck.NotALot;
            if (!Grid.IsUnlocked(cell)) return PlacementCheck.Locked;
            if (BuildingAt(cell) != null) return PlacementCheck.Occupied;
            if (!Grid.HasRoadAccess(cell)) return PlacementCheck.NoRoadAccess;
            if (!ignoreCost && !CanAfford(type.BuildCost)) return PlacementCheck.CannotAfford;
            return PlacementCheck.Ok;
        }

        // ------------------------------------------------------------------ setup & player actions

        /// <summary>Places a building without paying for it (level setup).</summary>
        public Building AddBuilding(BuildingType type, Cell cell)
        {
            var check = CheckPlacement(type, cell, ignoreCost: true);
            if (check != PlacementCheck.Ok)
                throw new InvalidOperationException($"Can't place {type} at {cell}: {check}");

            var b = new Building(_nextBuildingId++, type, cell);
            _buildings.Add(b);
            _buildingAt[cell] = b;
            if (type.IsTownHall && TownHall == null) TownHall = b;
            BuildingAdded?.Invoke(b);
            return b;
        }

        public Building TryBuild(BuildingType type, Cell cell, out PlacementCheck result)
        {
            result = CheckPlacement(type, cell);
            if (result != PlacementCheck.Ok) return null;
            Stockpile.TryRemove(type.BuildCost);
            return AddBuilding(type, cell);
        }

        /// <summary>Adds a bot at the depot without paying for it (level setup).</summary>
        public CourierBot AddBot(string name = null)
        {
            int id = _nextBotId++;
            var bot = new CourierBot(id, name ?? $"Bot {id}", Depot, DepotFacing, Balance.BotCapacity);
            _bots.Add(bot);
            BotAdded?.Invoke(bot);
            return bot;
        }

        public CourierBot TryBuyBot()
        {
            var cost = NextBotCost;
            if (!CanAfford(cost)) return null;
            Stockpile.TryRemove(cost);
            return AddBot();
        }

        public bool TryExpand()
        {
            if (!Grid.CanExpand) return false;
            var cost = NextExpansionCost;
            if (!CanAfford(cost)) return false;
            Stockpile.TryRemove(cost);
            ExpansionLevel++;
            Grid.ExpandRing();
            return true;
        }

        // ------------------------------------------------------------------ day loop

        public void StartDay()
        {
            if (Phase == DayPhase.Running) return;
            Phase = DayPhase.Running;
            Tick = 0;
            DeliveredToday.Clear();
            foreach (var b in _buildings) b.ResetDailyStats();
            foreach (var bot in _bots)
            {
                bot.ReturnHome();
                bot.ResetDailyStats();
            }
            DayStarted?.Invoke();
        }

        /// <summary>Advances one tick: every bot runs one command, then every building produces.</summary>
        public void Step()
        {
            if (Phase != DayPhase.Running) return;

            foreach (var bot in _bots) RunNextCommand(bot);
            foreach (var b in _buildings) b.Tick();

            Tick++;
            Ticked?.Invoke();

            if (Tick >= TicksPerDay) EndDay();
        }

        /// <summary>Runs a whole day instantly. Useful for tests and "skip day".</summary>
        public DayReport RunFullDay()
        {
            DayReport report = null;
            void Capture(DayReport r) => report = r;
            DayEnded += Capture;
            StartDay();
            while (Phase == DayPhase.Running) Step();
            DayEnded -= Capture;
            return report;
        }

        void EndDay()
        {
            int problems = 0;
            foreach (var bot in _bots) problems += bot.ProblemsToday;

            var report = new DayReport { Day = Day, Delivered = DeliveredToday.Clone(), Problems = problems };

            Phase = DayPhase.Planning;
            Day++;
            // Bots drive back to the depot overnight. Cargo stays on board.
            foreach (var bot in _bots) bot.ReturnHome();
            DayEnded?.Invoke(report);
        }

        // ------------------------------------------------------------------ command execution

        void RunNextCommand(CourierBot bot)
        {
            bot.PreviousPosition = bot.Position;
            bot.PreviousFacing = bot.Facing;

            var schedule = bot.Schedule;
            if (schedule.Count == 0)
            {
                bot.LastExecutedIndex = -1;
                bot.LastResult = CommandResult.Idle;
                return;
            }

            if (bot.ProgramCounter >= schedule.Count)
            {
                if (!bot.Loop)
                {
                    bot.LastExecutedIndex = -1;
                    bot.LastResult = CommandResult.Idle;
                    return;
                }
                bot.ProgramCounter = 0;
            }

            int index = bot.ProgramCounter;
            bot.LastExecutedIndex = index;
            bot.LastResult = Execute(bot, schedule[index]);
            if (CommandInfo.IsProblem(bot.LastResult)) bot.ProblemsToday++;
            bot.ProgramCounter = index + 1;
        }

        /// <summary>Executes a single command immediately. Public so tools/tests can drive bots directly.</summary>
        public CommandResult Execute(CourierBot bot, Command cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.MoveForward:
                {
                    var next = bot.Position.Step(bot.Facing);
                    if (!Grid.IsDrivable(next)) return CommandResult.Blocked;
                    bot.Position = next;
                    return CommandResult.Ok;
                }
                case CommandType.TurnLeft:
                    bot.Facing = bot.Facing.TurnLeft();
                    return CommandResult.Ok;
                case CommandType.TurnRight:
                    bot.Facing = bot.Facing.TurnRight();
                    return CommandResult.Ok;
                case CommandType.TurnAround:
                    bot.Facing = bot.Facing.Reverse();
                    return CommandResult.Ok;
                case CommandType.PickUp:
                    return PickUp(bot, cmd.Side);
                case CommandType.DropOff:
                    return DropOff(bot, cmd.Side);
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

        readonly Cell[] _candidates = new Cell[4];

        CommandResult PickUp(CourierBot bot, InteractSide side)
        {
            int n = CandidateCells(bot.Position, bot.Facing, side, _candidates);
            bool foundBuilding = false;

            for (int i = 0; i < n; i++)
            {
                var b = BuildingAt(_candidates[i]);
                if (b == null) continue;
                foundBuilding = true;

                var r = b.CollectableOutput(bot.Cargo);
                if (r == Resource.None) continue;
                if (bot.FreeCapacity <= 0) return CommandResult.CargoFull;

                int got = b.Collect(r, bot.FreeCapacity);
                bot.AddCargo(r, got);
                return CommandResult.Ok;
            }

            if (!foundBuilding) return CommandResult.NoBuilding;
            return bot.FreeCapacity <= 0 ? CommandResult.CargoFull : CommandResult.NothingToCollect;
        }

        CommandResult DropOff(CourierBot bot, InteractSide side)
        {
            if (!bot.IsCarrying) return CommandResult.EmptyHanded;

            int n = CandidateCells(bot.Position, bot.Facing, side, _candidates);
            bool foundBuilding = false;

            for (int i = 0; i < n; i++)
            {
                var b = BuildingAt(_candidates[i]);
                if (b == null) continue;
                foundBuilding = true;

                var r = bot.Cargo;
                int given = b.Receive(r, bot.CargoCount);
                if (given <= 0) continue;

                bot.RemoveCargo(given);
                if (b.Type.IsTownHall)
                {
                    DeliveredToday.Add(r, given);
                    bot.DeliveredToday += given;
                }
                return CommandResult.Ok;
            }

            return foundBuilding ? CommandResult.NotAccepted : CommandResult.NoBuilding;
        }
    }
}
