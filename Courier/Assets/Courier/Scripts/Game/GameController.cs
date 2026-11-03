using System;
using System.Collections.Generic;
using Courier.Sim;
using UnityEngine;

namespace Courier.Game
{
    /// <summary>
    /// Owns the <see cref="Town"/>, drives it in real time, and holds player-facing state
    /// (selection, placement mode, speed). Views and UI read from here and call its methods;
    /// they never mutate the sim directly except for schedule editing.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        static readonly float[] s_Speeds = { 1f, 2f, 4f, 8f };

        public Town Town { get; private set; }

        /// <summary>Real seconds per sim tick at 1x speed.</summary>
        public float SecondsPerTick { get; set; } = 0.25f;

        public int SpeedIndex { get; private set; }
        public float Speed => s_Speeds[SpeedIndex];
        public bool Paused { get; private set; }

        /// <summary>When on, the next day starts automatically after the previous one ends.</summary>
        public bool AutoRunDays { get; private set; }

        /// <summary>0..1 progress through the current tick, for interpolating visuals.</summary>
        public float TickProgress { get; private set; } = 1f;

        public CourierBot SelectedBot { get; private set; }
        public Building SelectedBuilding { get; private set; }
        public BuildingType PlacingType { get; private set; }

        public bool HasHover { get; private set; }
        public Cell HoveredCell { get; private set; }

        public bool IsPlanning => Town.Phase == DayPhase.Planning;

        public event Action SelectionChanged;
        public event Action PlacementChanged;
        public event Action StateChanged;
        public event Action<string> Toast;

        float _accumulator;
        bool _startNextDay;

        public void Init(Town town, float secondsPerTick)
        {
            Town = town;
            SecondsPerTick = Mathf.Max(0.02f, secondsPerTick);
            Town.DayEnded += OnDayEnded;
        }

        void OnDestroy()
        {
            if (Town != null) Town.DayEnded -= OnDayEnded;
        }

        void Update()
        {
            if (Town == null) return;

            if (_startNextDay)
            {
                _startNextDay = false;
                StartDay();
            }

            if (Town.Phase != DayPhase.Running)
            {
                TickProgress = 1f;
                return;
            }
            if (Paused) return;

            _accumulator += Time.deltaTime * Speed;
            int guard = 0;
            while (_accumulator >= SecondsPerTick && Town.Phase == DayPhase.Running && guard++ < 64)
            {
                _accumulator -= SecondsPerTick;
                Town.Step();
            }
            TickProgress = Town.Phase == DayPhase.Running ? Mathf.Clamp01(_accumulator / SecondsPerTick) : 1f;
        }

        // ------------------------------------------------------------------ time

        public void StartDay()
        {
            if (Town.Phase == DayPhase.Running) return;
            _accumulator = 0f;
            TickProgress = 0f;
            Paused = false;
            Town.StartDay();
            StateChanged?.Invoke();
        }

        /// <summary>Space bar / main button: start the day, or pause/resume it.</summary>
        public void PrimaryAction()
        {
            if (Town.Phase == DayPhase.Planning) StartDay();
            else TogglePause();
        }

        public void TogglePause()
        {
            if (Town.Phase != DayPhase.Running) return;
            Paused = !Paused;
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

        void OnDayEnded(DayReport report)
        {
            _accumulator = 0f;
            TickProgress = 1f;
            Paused = false;

            string delivered = report.Delivered.IsEmpty ? "nothing delivered" : "delivered " + report.Delivered;
            string problems = report.Problems > 0 ? $" ({report.Problems} commands had nothing to do)" : "";
            ShowToast($"Day {report.Day} done: {delivered}{problems}");

            if (AutoRunDays) _startNextDay = true;
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ selection

        public void SelectBot(CourierBot bot)
        {
            SelectedBot = bot;
            SelectionChanged?.Invoke();
        }

        public void SelectBuilding(Building b)
        {
            SelectedBuilding = b;
            SelectionChanged?.Invoke();
        }

        public void SelectNextBot()
        {
            var bots = Town.Bots;
            if (bots.Count == 0) return;
            int i = SelectedBot == null ? -1 : IndexOf(bots, SelectedBot);
            SelectBot(bots[(i + 1) % bots.Count]);
        }

        public void SetHover(bool hasHover, Cell cell)
        {
            HasHover = hasHover;
            HoveredCell = cell;
        }

        public Building HoveredBuilding => HasHover ? Town.BuildingAt(HoveredCell) : null;

        public CourierBot BotAt(Cell cell)
        {
            // Cycle through bots sharing a cell on repeated clicks.
            CourierBot first = null;
            bool passedSelected = false;
            foreach (var b in Town.Bots)
            {
                if (b.Position != cell) continue;
                if (first == null) first = b;
                if (passedSelected) return b;
                if (b == SelectedBot) passedSelected = true;
            }
            return first;
        }

        /// <summary>Left click on the map.</summary>
        public void ClickCell(Cell cell)
        {
            if (PlacingType != null)
            {
                TryPlace(cell);
                return;
            }

            var bot = BotAt(cell);
            if (bot != null)
            {
                SelectBot(bot);
                return;
            }

            SelectBuilding(Town.BuildingAt(cell));
        }

        /// <summary>Escape / right click.</summary>
        public void Cancel()
        {
            if (PlacingType != null) CancelPlacement();
            else if (SelectedBuilding != null) SelectBuilding(null);
        }

        // ------------------------------------------------------------------ building & spending

        public void BeginPlacement(BuildingType type)
        {
            if (!Town.CanAfford(type.BuildCost))
            {
                ShowToast($"{type.DisplayName} needs {ResourceAmount.Describe(type.BuildCost)}");
                return;
            }
            PlacingType = type;
            PlacementChanged?.Invoke();
        }

        public void CancelPlacement()
        {
            if (PlacingType == null) return;
            PlacingType = null;
            PlacementChanged?.Invoke();
        }

        void TryPlace(Cell cell)
        {
            var type = PlacingType;
            var b = Town.TryBuild(type, cell, out var result);
            if (b == null)
            {
                ShowToast(Explain(result));
                return;
            }
            ShowToast($"Built a {type.DisplayName}");
            CancelPlacement();
            SelectBuilding(b);
        }

        public void BuyBot()
        {
            var cost = Town.NextBotCost;
            var bot = Town.TryBuyBot();
            if (bot == null)
            {
                ShowToast($"A new courier needs {ResourceAmount.Describe(cost)}");
                return;
            }
            ShowToast($"{bot.Name} is ready at the depot");
            SelectBot(bot);
        }

        public void Expand()
        {
            if (!Town.Grid.CanExpand)
            {
                ShowToast("The town can't grow any further");
                return;
            }
            var cost = Town.NextExpansionCost;
            if (!Town.TryExpand())
            {
                ShowToast($"Expanding needs {ResourceAmount.Describe(cost)}");
                return;
            }
            ShowToast("New blocks unlocked!");
            StateChanged?.Invoke();
        }

        public void ShowToast(string message) => Toast?.Invoke(message);

        public static string Explain(PlacementCheck check)
        {
            switch (check)
            {
                case PlacementCheck.Ok: return "OK";
                case PlacementCheck.OutOfBounds: return "That's off the map";
                case PlacementCheck.NotALot: return "Build on a lot, not a road";
                case PlacementCheck.Locked: return "That block isn't unlocked yet";
                case PlacementCheck.Occupied: return "Something's already there";
                case PlacementCheck.NoRoadAccess: return "Buildings need a road next to them";
                case PlacementCheck.CannotAfford: return "Not enough goods in the town hall";
                case PlacementCheck.NotBuildable: return "That can't be built";
                default: return check.ToString();
            }
        }

        static int IndexOf(IReadOnlyList<CourierBot> list, CourierBot bot)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == bot) return i;
            return -1;
        }
    }
}
