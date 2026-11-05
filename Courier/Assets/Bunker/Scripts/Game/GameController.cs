using System;
using System.Collections.Generic;
using Bunker.Sim;
using UnityEngine;

namespace Bunker.Game
{
    /// <summary>
    /// Owns the <see cref="World"/> and keeps time running: there's no "start day", the clock just goes,
    /// and at 2 am night falls with a short fade while everyone is reset. Holds player-facing state:
    /// pause, speed, the bot whose programming interface is open, tile picking. Views and UI read from
    /// here and call its methods. Your robot's input comes through <see cref="PlayerController"/>.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        static readonly float[] s_Speeds = { 1f, 2f, 4f, 8f };

        /// <summary>The fade to black before night falls, and how long the night lasts after it (real seconds).</summary>
        public const float FadeInSeconds = 0.7f;
        public const float NightSeconds = 2.4f;
        const float FadeOutSeconds = 0.8f;

        public World World { get; private set; }
        public float SecondsPerTick { get; set; } = 0.25f;
        public int SpeedIndex { get; private set; }
        public float Speed => s_Speeds[SpeedIndex];

        /// <summary>Time is stopped: you paused (Space), or a bot's programming interface is open.</summary>
        public bool Paused => _userPaused || IsEditorOpen;

        /// <summary>You paused with Space (as opposed to the interface pausing for you).</summary>
        public bool UserPaused => _userPaused;

        /// <summary>The night fade is playing (fading out the last tick, or the night itself); nothing moves.</summary>
        public bool InNight => _fadeInTimer > 0f || _nightTimer > 0f;

        /// <summary>0..1 darkness: fades in before the reset, holds while everything is reset, fades out.</summary>
        public float NightFade01
        {
            get
            {
                if (_fadeInTimer > 0f) return Mathf.Clamp01(1f - _fadeInTimer / FadeInSeconds);
                if (_nightTimer > 0f) return Mathf.Clamp01(_nightTimer / FadeOutSeconds);
                return 0f;
            }
        }

        /// <summary>The last night's results, for the night card.</summary>
        public DayReport LastNight { get; private set; }

        /// <summary>0..1 progress through the current tick, for interpolating visuals.</summary>
        public float TickProgress { get; private set; } = 1f;

        /// <summary>The bot whose programming interface is open (never you: you're steered directly).</summary>
        public Bot SelectedBot { get; private set; }
        public bool IsEditorOpen => SelectedBot != null;
        public bool HasSelectedTile { get; private set; }
        public Cell SelectedTile { get; private set; }

        public bool HasHover { get; private set; }
        public Cell HoveredCell { get; private set; }

        /// <summary>The "preview day" playback (set up by GameBootstrap).</summary>
        public PreviewController Preview { get; set; }

        /// <summary>Your robot's controls (set up by GameBootstrap).</summary>
        public PlayerController Player { get; set; }

        /// <summary>Bot and crop stats in use (the BunkerData asset).</summary>
        public BunkerData Data { get; set; }

        /// <summary>The data asset was edited while playing (stats already applied to the sim).</summary>
        public event Action DataChanged;

        /// <summary>Waiting for the player to click a tile for a "Go to" command.</summary>
        public bool IsPicking => _onPick != null;

        public event Action SelectionChanged;
        public event Action StateChanged;
        public event Action PickChanged;
        public event Action<string> Toast;
        public event Action RestartRequested;

        Action<Cell> _onPick;
        float _accumulator;
        float _nightTimer;
        float _fadeInTimer;
        bool _dataDirty;
        bool _userPaused;
        bool _warnedLate;
        CommandResult _lastToastResult;
        float _lastToastTime;

        // Routines edited while time was stopped: those bots restart from line 1 when time runs again.
        readonly HashSet<Bot> _editedBots = new HashSet<Bot>();
        readonly HashSet<int> _editedMacros = new HashSet<int>();
        readonly List<Bot> _watched = new List<Bot>();

        public void Init(World world, float secondsPerTick)
        {
            World = world;
            SecondsPerTick = Mathf.Max(0.02f, secondsPerTick);
            World.DayEnded += OnDayEnded;
            World.DayStarted += OnDayStarted;
            World.GameOver += OnGameOver;
            World.PlayerActed += OnPlayerActed;
            World.BotAdded += Watch;
            World.Library.MacroEdited += OnMacroEdited;
            foreach (var b in World.Bots) Watch(b);
            BunkerData.Edited += OnDataEdited;
        }

        /// <summary>An asset was edited while playing: if it's ours, re-apply it next frame.</summary>
        void OnDataEdited(BunkerData edited)
        {
            if (edited != null && edited == Data) _dataDirty = true;
        }

        /// <summary>Live tuning: stats, carry capacities, routes and the preview follow the edited asset.</summary>
        void ApplyDataEdits()
        {
            _dataDirty = false;
            if (World == null || Data == null) return;
            Data.Apply();
            World.RefreshStats();
            if (Preview != null) Preview.MarkDirty();
            DataChanged?.Invoke();
            StateChanged?.Invoke();
        }

        void OnDestroy()
        {
            if (World == null) return;
            World.DayEnded -= OnDayEnded;
            World.DayStarted -= OnDayStarted;
            World.GameOver -= OnGameOver;
            World.PlayerActed -= OnPlayerActed;
            World.BotAdded -= Watch;
            World.Library.MacroEdited -= OnMacroEdited;
            foreach (var b in _watched) b.Routine.Changed -= MarkEditedFor(b);
            BunkerData.Edited -= OnDataEdited;
        }

        void Update()
        {
            if (World == null) return;
            if (_dataDirty) ApplyDataEdits();

            if (_fadeInTimer > 0f)
            {
                // Fading to black over the day's last tick: when it's dark, run it and let night fall unseen.
                _fadeInTimer -= Time.unscaledDeltaTime;
                if (_fadeInTimer <= 0f)
                {
                    _fadeInTimer = 0f;
                    World.ClearPlayerCommand();
                    World.Step(); // the last tick: Nightfall -> OnDayEnded starts the night timer
                }
                return;
            }
            if (_nightTimer > 0f)
            {
                _nightTimer -= Time.unscaledDeltaTime;
                TickProgress = 1f;
                if (_nightTimer <= 0f) StateChanged?.Invoke();
                return;
            }
            if (Paused || World.IsGameOver) return;

            _accumulator += Time.deltaTime * Speed;
            int guard = 0;
            while (_accumulator >= SecondsPerTick && guard++ < 64)
            {
                if (World.Tick >= World.TicksPerDay - 1)
                {
                    // The next tick ends the day: fade to black first (everything holds still), then run it.
                    _fadeInTimer = FadeInSeconds;
                    CancelPick();
                    StateChanged?.Invoke();
                    return;
                }
                _accumulator -= SecondsPerTick;
                // Your input is read fresh for every tick, so fast speeds don't drop or repeat moves.
                if (Player != null && Player.TryGetCommand(out var cmd)) World.SetPlayerCommand(cmd);
                else World.ClearPlayerCommand();
                World.Step();
                if (_nightTimer > 0f || World.IsGameOver) break; // night fell: stop for the fade
            }
            TickProgress = InNight ? 1f : Mathf.Clamp01(_accumulator / SecondsPerTick);

            if (!_warnedLate && World.ClockMinutes >= 24 * 60)
            {
                _warnedLate = true;
                ShowToast("Midnight. At 2 am everyone is called back to the hatch and the day resets.");
            }
        }

        // ------------------------------------------------------------------ time

        /// <summary>Space: pause/resume, or close the programming interface if it's open.</summary>
        public void PrimaryAction()
        {
            if (IsEditorOpen) CloseEditor();
            else TogglePause();
        }

        public void TogglePause()
        {
            if (World.IsGameOver || InNight) return;
            _userPaused = !_userPaused;
            if (!Paused) OnResumed();
            StateChanged?.Invoke();
        }

        public void CycleSpeed()
        {
            SpeedIndex = (SpeedIndex + 1) % s_Speeds.Length;
            StateChanged?.Invoke();
        }

        public void Restart() => RestartRequested?.Invoke();

        void OnResumed()
        {
            CancelPick();
            ApplyEdits();
        }

        void OnDayEnded(DayReport r)
        {
            LastNight = r;
            _accumulator = 0f;
            TickProgress = 1f;
            CancelPick();
            if (!World.IsGameOver) _nightTimer = NightSeconds;

            string delivered = r.Delivered.IsEmpty ? "nothing delivered" : $"delivered {r.Delivered}";
            string ripened = r.CropsRipened > 0 ? $", {r.CropsRipened} crop(s) ripened" : "";
            string dry = r.CropsDry > 0 ? $", {r.CropsDry} crop(s) left in dry soil" : "";
            string ration = "";
            if (r.RationNight)
            {
                ration = r.RationShort
                    ? $" RATION SHORT: the bunker needed {r.RationNeeded} food and got {r.RationEaten}. Strike {r.Strikes} of {World.MaxStrikes}."
                    : $" The bunker ate its ration ({r.RationNeeded} food).";
            }
            ShowToast($"Day {r.Day}: {delivered}{ripened}{dry}.{ration}");
            StateChanged?.Invoke();
        }

        void OnDayStarted()
        {
            _warnedLate = false;
            _editedBots.Clear(); // everyone starts their routine from line 1 anyway
            _editedMacros.Clear();
        }

        void OnGameOver(Outcome _)
        {
            _nightTimer = 0f;
            _fadeInTimer = 0f;
            CancelPick();
            StateChanged?.Invoke();
        }

        /// <summary>When your own job fails, say why and what to do about it.</summary>
        void OnPlayerActed(Command c, CommandResult r)
        {
            if (c.Type == CommandType.Dump && r == CommandResult.Ok)
            {
                ShowToast("You pour your water out on the ground");
                return;
            }
            if (!CommandInfo.IsProblem(r)) return;
            if (r == _lastToastResult && Time.unscaledTime - _lastToastTime < 2f) return; // don't nag on repeat presses
            _lastToastResult = r;
            _lastToastTime = Time.unscaledTime;
            ShowToast(Hint(c, r, World.Player != null && World.Player.Load.Get(Item.Water) > 0));
        }

        static string Hint(Command c, CommandResult r, bool carryingWater)
        {
            if (r == CommandResult.LoadFull && carryingWater) return "Too heavy: press X to dump your water, or face the hatch and press E to deliver";
            if (r == CommandResult.AlreadyDone && c.Type == CommandType.Water) return "Still wet: water again once the soil dries to damp";
            if (r == CommandResult.AlreadyDone && c.Type == CommandType.Grab) return "Your tank's already full";
            switch (r)
            {
                case CommandResult.NoWater: return "Out of water: face the stream and press E to fill up";
                case CommandResult.NoSeeds: return "No seeds on you: face the hatch and press E to take some (Q picks which)";
                case CommandResult.LoadFull: return "Too heavy: face the hatch and press E to deliver what you're carrying";
                case CommandResult.OutOfStock: return "The bunker has none of those seeds. Clear weeds and thickets for wild ones, or save seeds in the workshop";
                case CommandResult.NotRipe: return "Not ripe yet";
                case CommandResult.AlreadyDone: return "Already done";
                case CommandResult.NothingToClear: return "Nothing to do there";
                case CommandResult.NotSoil: return "Nothing grows on concrete";
                case CommandResult.NothingToDump: return "You're not carrying any water";
                default: return CommandInfo.Describe(r);
            }
        }

        // ------------------------------------------------------------------ edits made while time is stopped

        void Watch(Bot b)
        {
            if (b.IsPlayer) return;
            b.Routine.Changed += MarkEditedFor(b);
            _watched.Add(b);
        }

        readonly Dictionary<Bot, Action> _editHandlers = new Dictionary<Bot, Action>();

        Action MarkEditedFor(Bot b)
        {
            if (!_editHandlers.TryGetValue(b, out var handler))
            {
                handler = () => _editedBots.Add(b);
                _editHandlers[b] = handler;
            }
            return handler;
        }

        void OnMacroEdited(Macro m) => _editedMacros.Add(m.Id);

        /// <summary>True if this bot will restart from line 1 when time runs again (its routine, or a command it runs, was edited).</summary>
        public bool WillRestart(Bot b) =>
            b != null && !b.IsPlayer && (_editedBots.Contains(b) || RunsAny(b.Routine, _editedMacros, new HashSet<int>()));

        /// <summary>Bots whose routine (or a custom command they run) changed start again from line 1.</summary>
        void ApplyEdits()
        {
            if (_editedBots.Count == 0 && _editedMacros.Count == 0) return;
            var restarted = new List<string>();
            foreach (var b in World.Bots)
            {
                if (!WillRestart(b)) continue;
                World.RestartRoutine(b);
                restarted.Add(b.Name);
            }
            _editedBots.Clear();
            _editedMacros.Clear();
            if (restarted.Count > 0)
                ShowToast($"{string.Join(", ", restarted)} {(restarted.Count == 1 ? "starts its" : "start their")} new routine from line 1");
        }

        bool RunsAny(Routine routine, HashSet<int> ids, HashSet<int> seen)
        {
            if (ids.Count == 0) return false;
            foreach (var c in routine)
            {
                if (!c.IsRun) continue;
                if (ids.Contains(c.MacroId)) return true;
                if (!seen.Add(c.MacroId)) continue;
                var m = World.Library.Get(c.MacroId);
                if (m != null && RunsAny(m.Routine, ids, seen)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the programming interface

        /// <summary>Opens a bot's programming interface. Time stops while it's open.</summary>
        public void OpenEditor(Bot b)
        {
            if (b == null || b.IsPlayer || World.IsGameOver || InNight) return;
            if (SelectedBot == b) return;
            CancelPick();
            SelectedBot = b;
            SelectionChanged?.Invoke();
            StateChanged?.Invoke();
        }

        /// <summary>Closes the interface; time runs again (unless you'd paused with Space).</summary>
        public void CloseEditor()
        {
            if (!IsEditorOpen) return;
            CancelPick();
            if (Preview != null && Preview.IsOpen) Preview.Close();
            SelectedBot = null;
            if (!Paused) OnResumed();
            SelectionChanged?.Invoke();
            StateChanged?.Invoke();
        }

        /// <summary>Opens the interface for <paramref name="b"/>, or closes it for null.</summary>
        public void SelectBot(Bot b)
        {
            if (b == null) CloseEditor();
            else OpenEditor(b);
        }

        public void SelectNextBot()
        {
            var bots = new List<Bot>();
            foreach (var b in World.Bots)
                if (!b.IsPlayer) bots.Add(b);
            if (bots.Count == 0)
            {
                ShowToast("No bots yet: build one in the workshop");
                return;
            }
            int i = SelectedBot == null ? -1 : bots.IndexOf(SelectedBot);
            OpenEditor(bots[(i + 1) % bots.Count]);
        }

        public void SetHover(bool hasHover, Cell cell)
        {
            HasHover = hasHover;
            HoveredCell = cell;
        }

        /// <summary>Where a bot is drawn (set by the SurfaceView). Bots walking slowly are a tile ahead in the sim.</summary>
        public Func<Bot, Cell> DrawnCell { get; set; }

        /// <summary>A bot drawn on <paramref name="cell"/> (cycling through several), never you.</summary>
        public Bot BotAt(Cell cell)
        {
            Bot first = null;
            bool passedSelected = false;
            foreach (var b in World.Bots)
            {
                if (b.IsPlayer || (DrawnCell != null ? DrawnCell(b) : b.Position) != cell) continue;
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
                OpenEditor(b);
                return;
            }

            HasSelectedTile = !(HasSelectedTile && SelectedTile == cell);
            SelectedTile = cell;
            SelectionChanged?.Invoke();
        }

        /// <summary>Escape / right click: back out of whatever's open.</summary>
        public void Cancel()
        {
            if (IsPicking) CancelPick();
            else if (Preview != null && Preview.IsOpen) Preview.Close();
            else if (IsEditorOpen) CloseEditor();
            else if (HasSelectedTile)
            {
                HasSelectedTile = false;
                SelectionChanged?.Invoke();
            }
        }

        // ------------------------------------------------------------------ workshop

        public void Build(Recipe r)
        {
            if (InNight) return;
            if (!World.TryBuild(r, out var bot))
            {
                string locked = World.LockedReason(r);
                ShowToast(World.IsGameOver ? "It's all over - press Play again"
                    : locked != null ? locked
                    : $"Not enough in the stores: needs {CostText(World, r)}");
                return;
            }
            if (bot != null)
            {
                ShowToast($"{bot.Name} the {BotKinds.Name(bot.Kind).ToLowerInvariant()} whirrs to life by the hatch. Give it commands to loop, then press Done to let it work.");
                OpenEditor(bot);
            }
            else if (r == Recipe.BiggerTanks)
                ShowToast($"Everyone can now carry {World.Player.Capacity} weight");
            else
            {
                var crop = World.CropFor(r);
                ShowToast($"Saved {crop.SeedsSaved} {crop.Name.ToLowerInvariant()} seeds from one {crop.Name.ToLowerInvariant()}");
            }
            StateChanged?.Invoke();
        }

        public static string CostText(World w, Recipe r)
        {
            var parts = new List<string>();
            foreach (var a in w.CostOf(r)) parts.Add(a.ToString());
            return string.Join(", ", parts);
        }

        public void ShowToast(string message) => Toast?.Invoke(message);

        /// <summary>"8:40 am" for minutes after midnight (past 24:00 wraps into the small hours).</summary>
        public static string FormatClock(int minutes)
        {
            minutes = ((minutes % (24 * 60)) + 24 * 60) % (24 * 60);
            int h = minutes / 60;
            int m = minutes % 60 / 10 * 10; // ten-minute steps, like Stardew
            string ampm = h < 12 ? "am" : "pm";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{h12}:{m:00} {ampm}";
        }
    }
}
