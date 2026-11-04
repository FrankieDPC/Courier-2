using System;
using System.Collections.Generic;

namespace Bunker.Sim
{
    public enum Outcome
    {
        None,

        /// <summary>The bunker was fed to the end: the doors open.</summary>
        DoorsOpen,

        /// <summary>Too many missed rations: the bunker seals its doors.</summary>
        Sealed,
    }

    public sealed class DayReport
    {
        public int Day;

        /// <summary>Everything handed in at the hatch today (Deliver, plus unloading at night).</summary>
        public Inventory Delivered = new Inventory();
        public int TilesCleared;
        public int CropsWatered;
        public int CropsDry;
        public int CropsRipe;
        public int Problems;

        // Ration night (every few days).
        public bool RationNight;
        public int RationNeeded;
        public int RationEaten;
        public bool RationShort;
        public int Strikes;
    }

    /// <summary>
    /// The whole surface simulation. Deterministic and engine-free. Time never stops on its own: call
    /// <see cref="Step"/> once per tick. You (the player bot) act on whatever <see cref="SetPlayerCommand"/>
    /// last asked for; other bots loop their routines. When the day's last tick passes, night falls
    /// instantly: everyone is reset at the hatch, watered crops grow a day, every few nights the bunker
    /// eats a ration, and the next morning begins.
    /// </summary>
    public sealed partial class World
    {
        readonly List<Bot> _bots = new List<Bot>();
        readonly Inventory _deliveredToday = new Inventory();
        int _nextBotId = 1;
        int _tilesClearedToday;

        bool _hasPlayerNext;
        Command _playerNext;

        public SurfaceMap Map { get; }
        public CommandLibrary Library { get; } = new CommandLibrary();

        /// <summary>Every bot, you included (you're <see cref="Player"/>).</summary>
        public IReadOnlyList<Bot> Bots => _bots;
        public Bot Player { get; private set; }

        /// <summary>The bunker's stores: food (the pantry), seeds and building materials.</summary>
        public Inventory Stores { get; } = new Inventory();

        public int Day { get; private set; } = 1;
        public int Tick { get; private set; }
        public int TicksPerDay { get; set; } = Balance.TicksPerDay;
        public float DayProgress01 => TicksPerDay > 0 ? Math.Min(1f, Tick / (float)TicksPerDay) : 0f;

        /// <summary>Time of day in minutes after midnight: 6:00 am (360) at the first tick, 2:00 am (1560) at nightfall.</summary>
        public int ClockMinutes => Balance.DayStartMinutes + (int)((long)Tick * (Balance.DayEndMinutes - Balance.DayStartMinutes) / Math.Max(1, TicksPerDay));

        // Rations
        public int RationEvery { get; set; } = Balance.RationEvery;
        public int FinalDay { get; set; } = Balance.FinalDay;
        public int MaxStrikes { get; set; } = Balance.MaxStrikes;

        /// <summary>Rations served so far (missed ones included).</summary>
        public int RationsServed { get; private set; }
        public int Strikes { get; private set; }

        /// <summary>The next night the bunker eats.</summary>
        public int NextRationDay => ((Day + RationEvery - 1) / RationEvery) * RationEvery;
        public int NextRationNeeded => Balance.Ration(RationsServed);

        public int CapacityLevel { get; private set; }
        public int BotsBuilt { get; private set; }
        public Outcome Outcome { get; private set; }
        public bool IsGameOver => Outcome != Outcome.None;

        public event Action<Bot> BotAdded;

        /// <summary>A new morning (after the night reset).</summary>
        public event Action DayStarted;

        /// <summary>Night fell: everyone has been reset at the hatch and the night's results are in.</summary>
        public event Action<DayReport> DayEnded;
        public event Action Ticked;
        public event Action<Outcome> GameOver;
        public event Action<Cell> TileChanged;

        /// <summary>You did something (or tried to): the command and how it went. For toasts and sounds.</summary>
        public event Action<Command, CommandResult> PlayerActed;

        public World(SurfaceMap map)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
        }

        // ------------------------------------------------------------------ setup

        /// <summary>Adds your robot (once). It starts at home like the bots.</summary>
        public Bot AddPlayer(string name)
        {
            if (Player != null) return Player;
            Player = AddBot(name, BotKind.Player);
            return Player;
        }

        public Bot AddBot(string name, BotKind kind)
        {
            int id = _nextBotId++;
            var b = new Bot(id, name, Map.Home, Map.HomeFacing, Balance.BotCapacity + CapacityLevel * Balance.CapacityUpgrade, kind);
            _bots.Add(b);
            BotAdded?.Invoke(b);
            return b;
        }

        /// <summary>Deletes a custom command and removes every Run of it from every routine.</summary>
        public void DeleteMacro(Macro macro)
        {
            if (macro == null) return;
            foreach (var b in _bots) b.Routine.RemoveAll(cmd => cmd.IsRun && cmd.MacroId == macro.Id);
            Library.Remove(macro);
        }

        // ------------------------------------------------------------------ your robot

        /// <summary>
        /// What you do on the next tick (a Step, Face, or a job on the tile ahead). Consumed when it runs;
        /// set it again every tick a key is held. Ignored while a multi-tick job is underway.
        /// </summary>
        public void SetPlayerCommand(Command c)
        {
            _playerNext = c;
            _hasPlayerNext = true;
        }

        public void ClearPlayerCommand() => _hasPlayerNext = false;

        /// <summary>An input is waiting for the next tick.</summary>
        public bool PlayerCommandPending => _hasPlayerNext;

        /// <summary>True while your robot is partway through a job (clearing, tilling...).</summary>
        public bool PlayerBusy => Player != null && Player.HasJob;

        void RunPlayer(Bot b)
        {
            b.PreviousPosition = b.Position;
            b.PreviousFacing = b.Facing;

            Command cmd;
            if (b.HasJob) cmd = b.Job;
            else if (_hasPlayerNext)
            {
                cmd = _playerNext;
                _hasPlayerNext = false;
            }
            else
            {
                b.LastResult = CommandResult.Idle;
                return;
            }

            b.LastResult = Execute(b, cmd, out bool finished);
            b.HasJob = !finished;
            b.Job = cmd;
            if (finished) PlayerActed?.Invoke(cmd, b.LastResult);
        }

        // ------------------------------------------------------------------ day loop

        /// <summary>One tick: you act, every bot works on its routine. After the day's last tick, night falls.</summary>
        public void Step()
        {
            if (IsGameOver) return;

            foreach (var b in _bots)
            {
                if (b.IsPlayer) RunPlayer(b);
                else RunNextCommand(b);
            }

            Tick++;
            Ticked?.Invoke();

            if (Tick >= TicksPerDay) Nightfall();
        }

        /// <summary>Runs the rest of the current day instantly (up to and including the night). Handy for tests.</summary>
        public DayReport RunFullDay()
        {
            DayReport report = null;
            void Capture(DayReport r) => report = r;
            DayEnded += Capture;
            int guard = 0;
            while (report == null && !IsGameOver && guard++ < 1000000) Step();
            DayEnded -= Capture;
            return report;
        }

        /// <summary>
        /// Night: everyone is reset at the hatch (loads handed in, routines back to line 1), watered crops
        /// grow, the soil dries, maybe the bunker eats a ration, then it's the next morning.
        /// </summary>
        void Nightfall()
        {
            var report = new DayReport { Day = Day, TilesCleared = _tilesClearedToday };
            foreach (var b in _bots) report.Problems += b.ProblemsToday;

            _hasPlayerNext = false;
            foreach (var b in _bots) ResetAtHome(b);
            foreach (var a in _deliveredToday.ToList()) report.Delivered.Add(a.Item, a.Amount);

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

            if (Day % RationEvery == 0) ServeRation(report);
            report.Strikes = Strikes;

            if (Strikes >= MaxStrikes) Outcome = Outcome.Sealed;
            else if (Day >= FinalDay) Outcome = Outcome.DoorsOpen;
            else
            {
                Day++;
                Tick = 0;
            }

            _deliveredToday.Clear();
            _tilesClearedToday = 0;
            foreach (var b in _bots) b.ResetDailyStats();

            DayEnded?.Invoke(report);
            if (IsGameOver) GameOver?.Invoke(Outcome);
            else DayStarted?.Invoke();
        }

        /// <summary>
        /// Restarts a bot's routine from line 1 where it stands (keeps its position and load). Use after
        /// editing a routine mid-day so the bot doesn't carry on from a line that has moved.
        /// </summary>
        public void RestartRoutine(Bot b)
        {
            if (b == null || b.IsPlayer) return;
            b.ResetProgram();
            b.LastResult = CommandResult.None;
        }

        /// <summary>The bunker eats from the stores, smallest food first. Not enough food is a strike.</summary>
        void ServeRation(DayReport report)
        {
            int need = NextRationNeeded;
            report.RationNight = true;
            report.RationNeeded = need;

            int eaten = 0;
            foreach (var crop in Crops.All) // ordered smallest food value first
            {
                while (eaten < need && Stores.Get(crop.Produce) > 0)
                {
                    Stores.Remove(crop.Produce, 1);
                    eaten += crop.Food;
                }
            }
            report.RationEaten = eaten;
            if (eaten < need)
            {
                report.RationShort = true;
                Strikes++;
            }
            RationsServed++;
        }

        /// <summary>Hands items in at the hatch: into the stores, counted towards today's report.</summary>
        void HandIn(Bot b, Item item, int amount)
        {
            if (amount <= 0) return;
            Stores.Add(item, amount);
            if (ItemInfo.IsSeed(item)) return; // seeds going back on the shelf aren't news
            _deliveredToday.Add(item, amount);
            b.DeliveredToday += amount;
        }

        void RaiseTileChanged(Cell c) => TileChanged?.Invoke(c);
    }
}
