using System;
using System.Collections.Generic;

namespace Wizard.Sim
{
    public enum DayPhase
    {
        /// <summary>Between days: the player edits routines and spends. Time is stopped.</summary>
        Planning,

        /// <summary>The day is running (on the doom day, this is the siege).</summary>
        Running,

        /// <summary>
        /// The day's work is over: constructs walk home along the roads, empty their cargo into the
        /// tower and line up at their start position. Nothing else happens. Skipped on the doom day.
        /// </summary>
        Dusk,
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
        public List<Job> JobsDone = new List<Job>();
        public List<Job> JobsFailed = new List<Job>();
    }

    /// <summary>
    /// The whole simulation: village, constructs, spellbook, job board and the doom. Deterministic
    /// and engine-free. Call <see cref="StartDay"/>, then <see cref="Step"/> once per tick until
    /// <see cref="Phase"/> returns to Planning.
    /// </summary>
    public sealed partial class World
    {
        readonly List<Building> _buildings = new List<Building>();
        readonly List<Construct> _constructs = new List<Construct>();
        readonly Dictionary<Cell, Building> _buildingAt = new Dictionary<Cell, Building>();
        int _nextBuildingId = 1;
        int _nextConstructId = 1;

        public TownGrid Grid { get; }
        public Spellbook Spellbook { get; } = new Spellbook();
        public DoomPlan Doom { get; }

        public IReadOnlyList<Building> Buildings => _buildings;
        public IReadOnlyList<Construct> Constructs => _constructs;

        public Building Tower { get; private set; }

        /// <summary>The wizard's goods (the tower's input buffer).</summary>
        public Inventory Stockpile => Tower.Input;

        /// <summary>Where constructs start each day. Must be a road cell next to the tower.</summary>
        public Cell Depot { get; set; }
        public Facing DepotFacing { get; set; }

        public int Day { get; private set; } = 1;
        public int Tick { get; private set; }
        public int TicksPerDay { get; set; } = Balance.TicksPerDay;
        public DayPhase Phase { get; private set; } = DayPhase.Planning;

        public bool IsDoomDay => Day >= Doom.Day;
        public bool IsSiege => IsDoomDay && Phase == DayPhase.Running;
        public bool IsDusk => Phase == DayPhase.Dusk;

        /// <summary>True from the moment a day starts until it has fully ended (work, then dusk).</summary>
        public bool IsDayInProgress => Phase != DayPhase.Planning;

        public int DaysUntilDoom => Math.Max(0, Doom.Day - Day);
        public float DayProgress01 => IsDoomDay ? SiegeProgress01 : (TicksPerDay > 0 ? Math.Min(1f, Tick / (float)TicksPerDay) : 0f);

        public int TowerMaxHp { get; private set; } = Balance.TowerBaseHp;
        public int TowerHp { get; private set; } = Balance.TowerBaseHp;
        public int FortifyLevel { get; private set; }
        public int ExpansionLevel { get; private set; }
        public Outcome Outcome { get; private set; }
        public bool IsGameOver => Outcome != Outcome.None;

        /// <summary>Goods delivered to the tower during the current day.</summary>
        public Inventory DeliveredToday { get; } = new Inventory();

        public event Action<Building> BuildingAdded;
        public event Action<Construct> ConstructAdded;
        public event Action DayStarted;
        public event Action<DayReport> DayEnded;
        public event Action Ticked;
        public event Action<Outcome> GameOver;

        public World(TownGrid grid, DoomPlan doom, int seed = 1)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Doom = doom ?? throw new ArgumentNullException(nameof(doom));
            _rng = new Random(seed);
        }

        // ------------------------------------------------------------------ queries

        public Building BuildingAt(Cell c) => _buildingAt.TryGetValue(c, out var b) ? b : null;

        public bool CanAfford(IReadOnlyList<ResourceAmount> cost) => Tower != null && Stockpile.Has(cost);

        public IReadOnlyList<ResourceAmount> NextConstructCost => Balance.ConstructCost(_constructs.Count);
        public IReadOnlyList<ResourceAmount> NextExpansionCost => Balance.ExpansionCost(ExpansionLevel);
        public IReadOnlyList<ResourceAmount> NextFortifyCost => Balance.FortifyCost(FortifyLevel);

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
        public Building AddBuilding(BuildingType type, Cell cell, string owner = null)
        {
            var check = CheckPlacement(type, cell, ignoreCost: true);
            if (check != PlacementCheck.Ok)
                throw new InvalidOperationException($"Can't place {type} at {cell}: {check}");

            var b = new Building(_nextBuildingId++, type, cell) { Owner = owner };
            _buildings.Add(b);
            _buildingAt[cell] = b;
            if (type.IsTower && Tower == null) Tower = b;
            _distanceDirty = true;
            BuildingAdded?.Invoke(b);
            return b;
        }

        public Building TryBuild(BuildingType type, Cell cell, out PlacementCheck result)
        {
            result = IsGameOver ? PlacementCheck.NotBuildable : CheckPlacement(type, cell);
            if (result != PlacementCheck.Ok) return null;
            Stockpile.TryRemove(type.BuildCost);
            return AddBuilding(type, cell);
        }

        /// <summary>Adds a construct at the depot without paying for it (level setup).</summary>
        public Construct AddConstruct(string name = null)
        {
            int id = _nextConstructId++;
            var c = new Construct(id, name ?? Balance.ConstructName(id), Depot, DepotFacing, Balance.ConstructCapacity);
            _constructs.Add(c);
            ConstructAdded?.Invoke(c);
            return c;
        }

        public Construct TrySummonConstruct()
        {
            var cost = NextConstructCost;
            if (IsGameOver || !CanAfford(cost)) return null;
            Stockpile.TryRemove(cost);
            return AddConstruct();
        }

        public bool TryExpand()
        {
            if (IsGameOver || !Grid.CanExpand) return false;
            var cost = NextExpansionCost;
            if (!CanAfford(cost)) return false;
            Stockpile.TryRemove(cost);
            ExpansionLevel++;
            Grid.ExpandRing();
            return true;
        }

        public bool TryFortify()
        {
            if (IsGameOver) return false;
            var cost = NextFortifyCost;
            if (!CanAfford(cost)) return false;
            Stockpile.TryRemove(cost);
            FortifyLevel++;
            TowerMaxHp += Balance.FortifyHp;
            TowerHp += Balance.FortifyHp;
            return true;
        }

        /// <summary>Deletes a spell and removes every Cast of it from every routine.</summary>
        public void DeleteSpell(Spell spell)
        {
            if (spell == null) return;
            foreach (var c in _constructs) c.Routine.RemoveAll(cmd => cmd.IsCast && cmd.SpellId == spell.Id);
            Spellbook.Remove(spell); // a construct mid-way through it finishes the copy it's running
        }

        // ------------------------------------------------------------------ day loop

        public void StartDay()
        {
            if (Phase != DayPhase.Planning || IsGameOver) return;
            Phase = DayPhase.Running;
            Tick = 0;
            DeliveredToday.Clear();
            _doneToday.Clear();
            foreach (var b in _buildings) b.ResetDailyStats();
            foreach (var c in _constructs)
            {
                c.ReturnHome();
                c.ResetDailyStats();
            }
            if (IsDoomDay) BeginSiege();
            DayStarted?.Invoke();
        }

        /// <summary>
        /// One tick: constructs act, buildings produce, then (on the doom day) the siege advances.
        /// After the day's last tick comes dusk, when constructs walk home (one cell per tick).
        /// </summary>
        public void Step()
        {
            if (Phase == DayPhase.Dusk)
            {
                DuskStep();
                Tick++;
                Ticked?.Invoke();
                if (AllHome()) EndDay();
                return;
            }
            if (Phase != DayPhase.Running) return;

            foreach (var c in _constructs) RunNextCommand(c);
            foreach (var b in _buildings) b.Produce();
            if (IsDoomDay) SiegeStep();

            Tick++;
            Ticked?.Invoke();

            if (IsDoomDay)
            {
                if (Outcome != Outcome.None) EndSiege();
            }
            else if (Tick >= TicksPerDay)
            {
                BeginDusk();
                if (AllHome()) EndDay();
            }
        }

        /// <summary>Runs the rest of the current day instantly. Handy for tests and "skip".</summary>
        public DayReport RunFullDay()
        {
            DayReport report = null;
            void Capture(DayReport r) => report = r;
            DayEnded += Capture;
            StartDay();
            int guard = 0;
            while (Phase != DayPhase.Planning && guard++ < 100000) Step();
            DayEnded -= Capture;
            return report;
        }

        void EndDay()
        {
            int problems = 0;
            foreach (var c in _constructs) problems += c.ProblemsToday;

            var report = new DayReport { Day = Day, Delivered = DeliveredToday.Clone(), Problems = problems };
            report.JobsDone.AddRange(_doneToday);
            ExpireJobs(Day, report.JobsFailed);

            Phase = DayPhase.Planning;
            Day++;
            foreach (var c in _constructs) c.ReturnHome(); // already home after dusk; this resets their programs
            if (!IsDoomDay) PostJobs();
            DayEnded?.Invoke(report);
        }

        void EndSiege()
        {
            Phase = DayPhase.Planning;
            foreach (var c in _constructs) c.ReturnHome();
            GameOver?.Invoke(Outcome);
        }
    }
}
