using System;
using System.Collections.Generic;
using Farm.Sim;
using UnityEngine;

namespace Farm.Game
{
    /// <summary>
    /// Owns the <see cref="World"/>, drives it in real time, and holds player-facing state (selection,
    /// tile picking, speed). Views and UI read from here and call its methods; they never mutate the
    /// sim directly except for routine editing.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        static readonly float[] s_Speeds = { 1f, 2f, 4f, 8f };

        /// <summary>Dusk (bots walking home) plays faster than the working day.</summary>
        const float DuskSpeedup = 3f;

        public World World { get; private set; }
        public float SecondsPerTick { get; set; } = 0.2f;
        public int SpeedIndex { get; private set; }
        public float Speed => s_Speeds[SpeedIndex];
        public bool Paused { get; private set; }
        public bool AutoRunDays { get; private set; }

        /// <summary>0..1 progress through the current tick, for interpolating visuals.</summary>
        public float TickProgress { get; private set; } = 1f;

        public Bot SelectedBot { get; private set; }
        public bool HasSelectedTile { get; private set; }
        public Cell SelectedTile { get; private set; }

        public bool HasHover { get; private set; }
        public Cell HoveredCell { get; private set; }

        /// <summary>The "preview day" playback (set up by GameBootstrap).</summary>
        public PreviewController Preview { get; set; }

        /// <summary>Waiting for the player to click a tile for a "Go to" command.</summary>
        public bool IsPicking => _onPick != null;

        public bool IsPlanning => World.Phase == DayPhase.Planning;

        public event Action SelectionChanged;
        public event Action StateChanged;
        public event Action PickChanged;
        public event Action<string> Toast;
        public event Action RestartRequested;

        Action<Cell> _onPick;
        float _accumulator;
        bool _startNextDay;

        public void Init(World world, float secondsPerTick)
        {
            World = world;
            SecondsPerTick = Mathf.Max(0.02f, secondsPerTick);
            World.DayEnded += OnDayEnded;
            World.SeasonOver += OnSeasonOver;
        }

        void OnDestroy()
        {
            if (World == null) return;
            World.DayEnded -= OnDayEnded;
            World.SeasonOver -= OnSeasonOver;
        }

        void Update()
        {
            if (World == null) return;

            if (_startNextDay)
            {
                _startNextDay = false;
                StartDay();
            }

            if (World.Phase == DayPhase.Planning)
            {
                TickProgress = 1f;
                return;
            }
            if (Paused) return;

            _accumulator += Time.deltaTime * Speed * (World.IsDusk ? DuskSpeedup : 1f);
            int guard = 0;
            while (_accumulator >= SecondsPerTick && World.Phase != DayPhase.Planning && guard++ < 64)
            {
                _accumulator -= SecondsPerTick;
                World.Step();
            }
            TickProgress = World.Phase != DayPhase.Planning ? Mathf.Clamp01(_accumulator / SecondsPerTick) : 1f;
        }

        // ------------------------------------------------------------------ time

        public void StartDay()
        {
            if (World.Phase != DayPhase.Planning || World.IsGameOver) return;
            CancelPick();
            _accumulator = 0f;
            TickProgress = 0f;
            Paused = false;
            World.StartDay();
            StateChanged?.Invoke();
        }

        public void PrimaryAction()
        {
            if (World.Phase == DayPhase.Planning) StartDay();
            else TogglePause();
        }

        public void TogglePause()
        {
            if (World.Phase == DayPhase.Planning) return;
            Paused = !Paused;
            if (!Paused) CancelPick();
            StateChanged?.Invoke();
        }

        public void CycleSpeed()
        {
            SpeedIndex = (SpeedIndex + 1) % s_Speeds.Length;
            StateChanged?.Invoke();
        }

        public void ToggleAutoRun()
        {
            AutoRunDays = !AutoRunDays;
            StateChanged?.Invoke();
        }

        public void Restart() => RestartRequested?.Invoke();

        void OnDayEnded(DayReport r)
        {
            _accumulator = 0f;
            TickProgress = 1f;
            Paused = false;

            string sold = r.Sold.IsEmpty ? "nothing sold" : $"sold {r.Sold} for {r.GoldEarned}g";
            string dry = r.CropsDry > 0 ? $", {r.CropsDry} crop(s) went thirsty" : "";
            string ripe = r.CropsRipe > 0 ? $", {r.CropsRipe} ready to harvest" : "";
            ShowToast($"Day {r.Day}: {sold}{dry}{ripe}.");

            if (AutoRunDays && !World.IsGameOver) _startNextDay = true;
            StateChanged?.Invoke();
        }

        void OnSeasonOver(Outcome _)
        {
            AutoRunDays = false;
            CancelPick();
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ selection & picking

        public void SelectBot(Bot b)
        {
            SelectedBot = b;
            SelectionChanged?.Invoke();
        }

        public void SelectNextBot()
        {
            var all = World.Bots;
            if (all.Count == 0) return;
            int i = SelectedBot == null ? -1 : IndexOf(all, SelectedBot);
            SelectBot(all[(i + 1) % all.Count]);
        }

        public void SetHover(bool hasHover, Cell cell)
        {
            HasHover = hasHover;
            HoveredCell = cell;
        }

        public Bot BotAt(Cell cell)
        {
            Bot first = null;
            bool passedSelected = false;
            foreach (var b in World.Bots)
            {
                if (b.Position != cell) continue;
                if (first == null) first = b;
                if (passedSelected) return b;
                if (b == SelectedBot) passedSelected = true;
            }
            return first;
        }

        /// <summary>Ask the player to click a tile; <paramref name="onPicked"/> receives it.</summary>
        public void BeginPick(Action<Cell> onPicked)
        {
            _onPick = onPicked;
            if (Preview != null && Preview.IsOpen) Preview.Close(); // the ghost would sit on the tiles
            PickChanged?.Invoke();
        }

        public void CancelPick()
        {
            if (_onPick == null) return;
            _onPick = null;
            PickChanged?.Invoke();
        }

        /// <summary>Left click on the map.</summary>
        public void ClickCell(Cell cell)
        {
            if (IsPicking)
            {
                if (!World.Map.IsWalkable(cell))
                {
                    ShowToast("Bots can't stand there - pick an open tile");
                    return;
                }
                var callback = _onPick;
                _onPick = null;
                PickChanged?.Invoke();
                callback(cell);
                return;
            }

            var b = BotAt(cell);
            if (b != null)
            {
                SelectBot(b);
                return;
            }

            HasSelectedTile = !(HasSelectedTile && SelectedTile == cell);
            SelectedTile = cell;
            SelectionChanged?.Invoke();
        }

        /// <summary>Escape / right click.</summary>
        public void Cancel()
        {
            if (IsPicking) CancelPick();
            else if (Preview != null && Preview.IsOpen) Preview.Close();
            else if (HasSelectedTile)
            {
                HasSelectedTile = false;
                SelectionChanged?.Invoke();
            }
        }

        // ------------------------------------------------------------------ shop

        public void BuySeeds(CropType crop, int count)
        {
            if (!World.TryBuySeeds(crop, count))
            {
                ShowToast($"{count} {crop.Name.ToLowerInvariant()} seeds cost {crop.SeedPrice * count}g");
                return;
            }
            ShowToast($"{count} {crop.Name.ToLowerInvariant()} seeds are in the seed shed");
            StateChanged?.Invoke();
        }

        public void BuyBot()
        {
            int cost = World.NextBotCost;
            var b = World.TryBuyBot();
            if (b == null)
            {
                ShowToast($"A new bot costs {cost}g");
                return;
            }
            ShowToast($"{b.Name} whirrs to life by the door");
            SelectBot(b);
            StateChanged?.Invoke();
        }

        public void UpgradeCapacity()
        {
            int cost = World.NextCapacityCost;
            if (!World.TryUpgradeCapacity())
            {
                ShowToast($"Bigger baskets cost {cost}g");
                return;
            }
            ShowToast($"Every bot can now carry {Balance.BotCapacity + World.CapacityLevel * Balance.CapacityUpgrade} weight");
            StateChanged?.Invoke();
        }

        public void ShowToast(string message) => Toast?.Invoke(message);

        /// <summary>Something about a routine that isn't a Routine edit (e.g. Loop) changed.</summary>
        public void NotifyRoutineOptionsChanged() => StateChanged?.Invoke();

        static int IndexOf(IReadOnlyList<Bot> list, Bot b)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == b) return i;
            return -1;
        }
    }
}
