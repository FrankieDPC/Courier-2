using System;
using System.Collections.Generic;
using Wizard.Sim;
using UnityEngine;

namespace Wizard.Game
{
    /// <summary>
    /// Owns the <see cref="World"/>, drives it in real time, and holds player-facing state
    /// (selection, placement mode, speed). Views and UI read from here and call its methods;
    /// they never mutate the sim directly except for routine/spell editing.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        static readonly float[] s_Speeds = { 1f, 2f, 4f, 8f };

        /// <summary>Dusk (constructs walking home) plays faster than the working day.</summary>
        const float DuskSpeedup = 3f;

        public World World { get; private set; }

        /// <summary>Real seconds per sim tick at 1x speed.</summary>
        public float SecondsPerTick { get; set; } = 0.25f;

        public int SpeedIndex { get; private set; }
        public float Speed => s_Speeds[SpeedIndex];
        public bool Paused { get; private set; }

        /// <summary>When on, the next day starts automatically after the previous one ends (never into the doom).</summary>
        public bool AutoRunDays { get; private set; }

        /// <summary>0..1 progress through the current tick, for interpolating visuals.</summary>
        public float TickProgress { get; private set; } = 1f;

        public Construct SelectedConstruct { get; private set; }
        public Building SelectedBuilding { get; private set; }
        public BuildingType PlacingType { get; private set; }

        /// <summary>The "preview day" playback (set up by GameBootstrap).</summary>
        public PreviewController Preview { get; set; }

        public bool HasHover { get; private set; }
        public Cell HoveredCell { get; private set; }

        public bool IsPlanning => World.Phase == DayPhase.Planning;

        public event Action SelectionChanged;
        public event Action PlacementChanged;
        public event Action StateChanged;
        public event Action<string> Toast;

        /// <summary>Raised when the player asks for a fresh game (GameBootstrap rebuilds everything).</summary>
        public event Action RestartRequested;

        float _accumulator;
        bool _startNextDay;

        public void Init(World world, float secondsPerTick)
        {
            World = world;
            SecondsPerTick = Mathf.Max(0.02f, secondsPerTick);
            World.DayEnded += OnDayEnded;
            World.GameOver += OnGameOver;
            World.JobFinished += OnJobFinished;
        }

        void OnDestroy()
        {
            if (World == null) return;
            World.DayEnded -= OnDayEnded;
            World.GameOver -= OnGameOver;
            World.JobFinished -= OnJobFinished;
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
            _accumulator = 0f;
            TickProgress = 0f;
            Paused = false;
            World.StartDay();
            if (World.IsSiege) ShowToast("The Doom is here! Hold the tower.");
            StateChanged?.Invoke();
        }

        /// <summary>Space bar / main button: start the day, or pause/resume it.</summary>
        public void PrimaryAction()
        {
            if (World.Phase == DayPhase.Planning) StartDay();
            else TogglePause();
        }

        public void TogglePause()
        {
            if (World.Phase == DayPhase.Planning) return;
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

        public void Restart() => RestartRequested?.Invoke();

        void OnDayEnded(DayReport report)
        {
            _accumulator = 0f;
            TickProgress = 1f;
            Paused = false;

            string earned = report.JobsDone.Count > 0 ? $"{report.JobsDone.Count} job(s) done" : "no jobs finished";
            string failed = report.JobsFailed.Count > 0 ? $", {report.JobsFailed.Count} missed" : "";
            string doom = World.IsDoomDay ? " The Doom arrives tomorrow!" : $" {World.DaysUntilDoom} days until the Doom.";
            ShowToast($"Day {report.Day} done: {earned}{failed}.{doom}");

            // Never auto-start the final day: the player should get one last look.
            if (AutoRunDays && !World.IsDoomDay) _startNextDay = true;
            StateChanged?.Invoke();
        }

        void OnGameOver(Outcome outcome)
        {
            TickProgress = 1f;
            AutoRunDays = false;
            StateChanged?.Invoke();
        }

        void OnJobFinished(Job job)
        {
            if (job.State == JobState.Done)
                ShowToast($"{job.Giver.Owner ?? job.Giver.Name} paid {ResourceAmount.Describe(job.Reward)}");
            else if (job.State == JobState.Failed)
                ShowToast($"Missed: {job.Title}");
        }

        // ------------------------------------------------------------------ selection

        public void SelectConstruct(Construct c)
        {
            SelectedConstruct = c;
            SelectionChanged?.Invoke();
        }

        public void SelectBuilding(Building b)
        {
            SelectedBuilding = b;
            SelectionChanged?.Invoke();
        }

        public void SelectNextConstruct()
        {
            var all = World.Constructs;
            if (all.Count == 0) return;
            int i = SelectedConstruct == null ? -1 : IndexOf(all, SelectedConstruct);
            SelectConstruct(all[(i + 1) % all.Count]);
        }

        public void SetHover(bool hasHover, Cell cell)
        {
            HasHover = hasHover;
            HoveredCell = cell;
        }

        public Building HoveredBuilding => HasHover ? World.BuildingAt(HoveredCell) : null;

        public Construct ConstructAt(Cell cell)
        {
            // Cycle through constructs sharing a cell on repeated clicks.
            Construct first = null;
            bool passedSelected = false;
            foreach (var c in World.Constructs)
            {
                if (c.Position != cell) continue;
                if (first == null) first = c;
                if (passedSelected) return c;
                if (c == SelectedConstruct) passedSelected = true;
            }
            return first;
        }

        public Monster MonsterAt(Cell cell)
        {
            foreach (var m in World.Monsters)
                if (m.Cell == cell) return m;
            return null;
        }

        /// <summary>Left click on the map.</summary>
        public void ClickCell(Cell cell)
        {
            if (PlacingType != null)
            {
                TryPlace(cell);
                return;
            }

            var c = ConstructAt(cell);
            if (c != null)
            {
                SelectConstruct(c);
                return;
            }

            SelectBuilding(World.BuildingAt(cell));
        }

        /// <summary>Escape / right click.</summary>
        public void Cancel()
        {
            if (PlacingType != null) CancelPlacement();
            else if (Preview != null && Preview.IsOpen) Preview.Close();
            else if (SelectedBuilding != null) SelectBuilding(null);
        }

        // ------------------------------------------------------------------ spending

        public void BeginPlacement(BuildingType type)
        {
            if (!World.CanAfford(type.BuildCost))
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
            var b = World.TryBuild(type, cell, out var result);
            if (b == null)
            {
                ShowToast(Explain(result));
                return;
            }
            ShowToast($"Raised a {type.DisplayName}");
            CancelPlacement();
            SelectBuilding(b);
        }

        public void SummonConstruct()
        {
            var cost = World.NextConstructCost;
            var c = World.TrySummonConstruct();
            if (c == null)
            {
                ShowToast($"Summoning a construct needs {ResourceAmount.Describe(cost)}");
                return;
            }
            ShowToast($"{c.Name} creaks to life at the tower");
            SelectConstruct(c);
        }

        public void Expand()
        {
            if (!World.Grid.CanExpand)
            {
                ShowToast("The village can't grow any further");
                return;
            }
            var cost = World.NextExpansionCost;
            if (!World.TryExpand())
            {
                ShowToast($"Expanding needs {ResourceAmount.Describe(cost)}");
                return;
            }
            ShowToast("New blocks claimed from the wilds");
            StateChanged?.Invoke();
        }

        public void Fortify()
        {
            var cost = World.NextFortifyCost;
            if (!World.TryFortify())
            {
                ShowToast($"Fortifying needs {ResourceAmount.Describe(cost)}");
                return;
            }
            ShowToast($"Tower fortified: {World.TowerMaxHp} HP");
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
                case PlacementCheck.Locked: return "That block is still wild - expand first";
                case PlacementCheck.Occupied: return "Something's already there";
                case PlacementCheck.NoRoadAccess: return "Buildings need a road next to them";
                case PlacementCheck.CannotAfford: return "Not enough in the tower's stores";
                case PlacementCheck.NotBuildable: return "That can't be built";
                default: return check.ToString();
            }
        }

        static int IndexOf(IReadOnlyList<Construct> list, Construct c)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == c) return i;
            return -1;
        }
    }
}
