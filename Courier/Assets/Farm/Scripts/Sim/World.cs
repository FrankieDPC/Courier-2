using System;
using System.Collections.Generic;

namespace Farm.Sim
{
    public enum DayPhase
    {
        /// <summary>Between days: edit routines, shop. Time is stopped.</summary>
        Planning,

        /// <summary>The working day: bots run their routines.</summary>
        Running,

        /// <summary>Work is over: bots walk home, empty their loads and line up.</summary>
        Dusk,
    }

    public enum Outcome
    {
        None,
        GoalMet,
        GoalMissed,
    }

    public sealed class DayReport
    {
        public int Day;
        public Inventory Sold = new Inventory();
        public int GoldEarned;
        public int CropsWatered;
        public int CropsDry;
        public int CropsRipe;
        public int Problems;
    }

    /// <summary>
    /// The whole farm simulation. Deterministic and engine-free: call <see cref="StartDay"/>, then
    /// <see cref="Step"/> once per tick until <see cref="Phase"/> returns to Planning. Overnight,
    /// shipped produce is sold and watered crops grow a day.
    /// </summary>
    public sealed partial class World
    {
        readonly List<Bot> _bots = new List<Bot>();
        int _nextBotId = 1;

        public FarmMap Map { get; }
        public CommandLibrary Library { get; } = new CommandLibrary();
        public IReadOnlyList<Bot> Bots => _bots;

        public int Gold { get; private set; }

        /// <summary>Seeds waiting in the shed. Bots Grab them from next to the shed.</summary>
        public Inventory SeedStock { get; } = new Inventory();

        /// <summary>Produce shipped today; sold overnight.</summary>
        public Inventory Bin { get; } = new Inventory();

        public int Day { get; private set; } = 1;
        public int Tick { get; private set; }
        public int TicksPerDay { get; set; } = Balance.TicksPerDay;
        public DayPhase Phase { get; private set; } = DayPhase.Planning;
        public bool IsDusk => Phase == DayPhase.Dusk;
        public float DayProgress01 => TicksPerDay > 0 ? Math.Min(1f, Tick / (float)TicksPerDay) : 0f;

        public int SeasonDays { get; set; } = Balance.SeasonDays;
        public int SeasonGoal { get; set; } = Balance.SeasonGoal;
        public int DaysLeft => Math.Max(0, SeasonDays - Day + 1);

        /// <summary>Gold earned from sales this season (spending doesn't reduce it).</summary>
        public int Earned { get; private set; }

        public int CapacityLevel { get; private set; }
        public Outcome Outcome { get; private set; }
        public bool IsGameOver => Outcome != Outcome.None;

        public event Action<Bot> BotAdded;
        public event Action DayStarted;
        public event Action<DayReport> DayEnded;
        public event Action Ticked;
        public event Action<Outcome> SeasonOver;
        public event Action<Cell> TileChanged;
        public event Action GoldChanged;

        public World(FarmMap map)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
        }

        // ------------------------------------------------------------------ setup & shop

        public Bot AddBot(string name = null)
        {
            int id = _nextBotId++;
            var b = new Bot(id, name ?? Balance.BotName(id), Map.Home, Facing.South, Balance.BotCapacity + CapacityLevel * Balance.CapacityUpgrade);
            _bots.Add(b);
            BotAdded?.Invoke(b);
            return b;
        }

        public void AddGold(int amount)
        {
            Gold += amount;
            GoldChanged?.Invoke();
        }

        public int NextBotCost => Balance.BotCost(_bots.Count);
        public int NextCapacityCost => Balance.CapacityCost(CapacityLevel);

        public bool TryBuySeeds(CropType crop, int count)
        {
            int cost = crop.SeedPrice * count;
            if (IsGameOver || count <= 0 || Gold < cost) return false;
            AddGold(-cost);
            SeedStock.Add(crop.Seed, count);
            return true;
        }

        public Bot TryBuyBot()
        {
            int cost = NextBotCost;
            if (IsGameOver || Gold < cost) return null;
            AddGold(-cost);
            return AddBot();
        }

        /// <summary>Every bot (current and future) can carry more weight.</summary>
        public bool TryUpgradeCapacity()
        {
            int cost = NextCapacityCost;
            if (IsGameOver || Gold < cost) return false;
            AddGold(-cost);
            CapacityLevel++;
            foreach (var b in _bots) b.Capacity += Balance.CapacityUpgrade;
            return true;
        }

        /// <summary>Deletes a custom command and removes every Run of it from every routine.</summary>
        public void DeleteMacro(Macro macro)
        {
            if (macro == null) return;
            foreach (var b in _bots) b.Routine.RemoveAll(cmd => cmd.IsRun && cmd.MacroId == macro.Id);
            Library.Remove(macro);
        }

        // ------------------------------------------------------------------ day loop

        public void StartDay()
        {
            if (Phase != DayPhase.Planning || IsGameOver) return;
            Phase = DayPhase.Running;
            Tick = 0;
            foreach (var b in _bots)
            {
                b.ReturnHome();
                b.ResetDailyStats();
            }
            DayStarted?.Invoke();
        }

        /// <summary>One tick: every bot works on its routine. After the last tick of the day comes dusk.</summary>
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

            foreach (var b in _bots) RunNextCommand(b);

            Tick++;
            Ticked?.Invoke();

            if (Tick >= TicksPerDay)
            {
                BeginDusk();
                if (AllHome()) EndDay();
            }
        }

        /// <summary>Runs the rest of the current day instantly. Handy for tests.</summary>
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

        /// <summary>Overnight: sell the bin, grow watered crops, dry the soil, then the next morning.</summary>
        void EndDay()
        {
            var report = new DayReport { Day = Day };
            foreach (var b in _bots) report.Problems += b.ProblemsToday;

            foreach (var a in Bin.ToList())
            {
                int gold = a.Amount * Crops.SellPrice(a.Item);
                report.GoldEarned += gold;
                report.Sold.Add(a.Item, a.Amount);
            }
            Bin.Clear();
            Earned += report.GoldEarned;
            if (report.GoldEarned > 0) AddGold(report.GoldEarned);

            foreach (var c in Map.AllCells())
            {
                var t = Map[c];
                if (t.HasCrop && !t.IsRipe)
                {
                    if (t.Watered)
                    {
                        t.Growth++;
                        report.CropsWatered++;
                    }
                    else report.CropsDry++;
                }
                if (t.IsRipe) report.CropsRipe++;
                if (t.Watered)
                {
                    t.Watered = false;
                    TileChanged?.Invoke(c);
                }
                else if (t.HasCrop) TileChanged?.Invoke(c);
            }

            Phase = DayPhase.Planning;
            foreach (var b in _bots) b.ReturnHome();
            Day++;

            if (Day > SeasonDays)
            {
                Day = SeasonDays;
                Outcome = Earned >= SeasonGoal ? Outcome.GoalMet : Outcome.GoalMissed;
            }

            DayEnded?.Invoke(report);
            if (IsGameOver) SeasonOver?.Invoke(Outcome);
        }

        void RaiseTileChanged(Cell c) => TileChanged?.Invoke(c);
    }
}
