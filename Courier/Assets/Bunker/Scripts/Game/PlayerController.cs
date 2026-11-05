using System;
using Bunker.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Bunker.Game
{
    /// <summary>
    /// Steers your robot. You move freely and smoothly (not tile by tile), faster than any bot, and turn
    /// instantly without stopping. The sim is told which tile you're on and which way you face; jobs
    /// (E) run in the sim on the next tick and work on the tile in front of you.
    ///   WASD / arrows   move (diagonals too); you face the way you're heading
    ///   E               do the obvious job on the tile in front of you
    ///   Q               choose which seed to plant / take from the hatch
    /// While a job is underway you stand still; pressing a direction walks away from it (and cancels it).
    /// Runs before the GameController steps the world, so the sim always has your latest tile.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerController : MonoBehaviour
    {
        static readonly Item[] s_SeedChoices = { Item.None, Item.RadishSeeds, Item.PotatoSeeds, Item.SquashSeeds };

        /// <summary>Half the width of your collision box, in tiles. Under 0.5 so you fit through one-tile gaps.</summary>
        const float Radius = 0.3f;

        GameController _game;
        Vector2 _pos;            // in tiles: (x, y) with tile centres on whole numbers
        Facing _facing;
        Facing _lastPressed;     // for diagonals: the most recently pressed direction wins the facing
        bool _useTap;            // E pressed, waiting for the next tick
        bool _dumpTap;           // X pressed: pour your water out on the next tick
        float _diagonalUntil;    // just after walking diagonally: keep that facing while the keys come up

        /// <summary>Walking speed in tiles per second of real time (from the BunkerData asset).</summary>
        public float TilesPerSecond => _game != null && _game.Data != null ? Mathf.Max(0.5f, _game.Data.playerTilesPerSecond) : 5.5f;

        public Vector3 WorldPosition => new Vector3(_pos.x, 0f, _pos.y);
        public Facing Facing => _facing;
        public bool IsWalking { get; private set; }

        /// <summary>Which seed E plants or takes. None = any / some of each.</summary>
        public Item SeedChoice { get; private set; } = Item.None;

        public event Action SeedChoiceChanged;

        public void Init(GameController game)
        {
            _game = game;
            SyncFromSim();
            game.World.DayStarted += SyncFromSim;
            game.World.DayEnded += SyncAfterNight;
        }

        void OnDestroy()
        {
            if (_game == null || _game.World == null) return;
            _game.World.DayStarted -= SyncFromSim;
            _game.World.DayEnded -= SyncAfterNight;
        }

        void SyncAfterNight(DayReport _) => SyncFromSim(); // also covers the last night, when no morning follows

        /// <summary>Back to wherever the sim has you (the start, and after the night reset).</summary>
        void SyncFromSim()
        {
            var p = _game.World.Player;
            if (p == null) return;
            _pos = new Vector2(p.Position.X, p.Position.Y);
            _facing = _lastPressed = p.Facing;
            _useTap = _dumpTap = false;
            IsWalking = false;
        }

        bool CanAct
        {
            get
            {
                var w = _game.World;
                return w.Player != null && !w.IsGameOver && !_game.Paused && !_game.InNight && !_game.IsPicking && !UIFactory.IsTyping();
            }
        }

        void Update()
        {
            IsWalking = false;
            if (_game == null || _game.World == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (!UIFactory.IsTyping() && kb.qKey.wasPressedThisFrame) CycleSeed();
            if (!CanAct)
            {
                _useTap = _dumpTap = false;
                return;
            }

            var w = _game.World;
            var p = w.Player;
            bool freshPress = false;
            var pressed = Facing.North;
            for (int f = 0; f < 4; f++)
            {
                if (!PressedThisFrame(kb, (Facing)f)) continue;
                _lastPressed = pressed = (Facing)f;
                freshPress = true;
            }

            if (kb.eKey.wasPressedThisFrame && !w.PlayerBusy && !freshPress) _useTap = true;
            if (kb.xKey.wasPressedThisFrame && !w.PlayerBusy && !freshPress) _dumpTap = true;

            // A fresh press means you want to walk: it wins over an E that hasn't started yet, and over a
            // job underway, unless it's just the way you're already facing into something solid.
            if (freshPress)
            {
                _useTap = _dumpTap = false;
                bool wantsAway = pressed != _facing || w.Map.IsWalkable(p.Position.Step(pressed));
                if (w.PlayerBusy && wantsAway) w.CancelPlayerJob();
            }
            if (w.PlayerBusy) return; // mid-job you stand still
            if (_useTap || _dumpTap) return; // hold still until the job starts on the next tick

            var input = ReadInput(kb);
            if (input != Vector2.zero)
            {
                bool diagonal = input.x != 0f && input.y != 0f;
                if (diagonal) _diagonalUntil = Time.unscaledTime + 0.12f;
                if (diagonal || Time.unscaledTime >= _diagonalUntil) _facing = FacingFor(input); // keys rarely come up together
                var before = _pos;
                float dt = Mathf.Min(Time.deltaTime, 0.05f); // a long frame can't carry you through a wall
                _pos = Move(w.Map, _pos, input.normalized * (TilesPerSecond * dt));
                IsWalking = (_pos - before).sqrMagnitude > 1e-8f;
            }

            // Tell the sim which tile you're on (the tile under your centre) and which way you face.
            var cell = new Cell(Mathf.RoundToInt(_pos.x), Mathf.RoundToInt(_pos.y));
            if (cell != p.Position || _facing != p.Facing) w.SetPlayerPose(cell, _facing);
        }

        /// <summary>The E (or X: dump water) command for this tick, if any. Called by the GameController just before each step.</summary>
        public bool TryGetCommand(out Command cmd)
        {
            cmd = default;
            if (!(_useTap || _dumpTap) || !CanAct) return false;
            bool dump = _dumpTap;
            _useTap = _dumpTap = false;
            var w = _game.World;
            if (w.PlayerBusy) return false;
            cmd = dump ? Command.Dump : w.SuggestUse(w.Player, SeedChoice);
            return true;
        }

        // ------------------------------------------------------------------ movement

        static Vector2 ReadInput(Keyboard kb)
        {
            var v = Vector2.zero;
            if (Held(kb, Facing.North)) v.y += 1f;
            if (Held(kb, Facing.South)) v.y -= 1f;
            if (Held(kb, Facing.East)) v.x += 1f;
            if (Held(kb, Facing.West)) v.x -= 1f;
            return v;
        }

        /// <summary>Straight lines face that way; on a diagonal, the most recently pressed key decides.</summary>
        Facing FacingFor(Vector2 input)
        {
            bool h = input.x != 0f, v = input.y != 0f;
            if (h && !v) return input.x > 0f ? Facing.East : Facing.West;
            if (v && !h) return input.y > 0f ? Facing.North : Facing.South;
            var horizontal = input.x > 0f ? Facing.East : Facing.West;
            var vertical = input.y > 0f ? Facing.North : Facing.South;
            if (_lastPressed == horizontal || _lastPressed == vertical) return _lastPressed;
            return _facing == horizontal || _facing == vertical ? _facing : vertical;
        }

        /// <summary>
        /// Moves in small steps, one axis at a time, so you slide along walls instead of sticking to them
        /// and can't skip through a wall even at high speed.
        /// </summary>
        static Vector2 Move(SurfaceMap map, Vector2 pos, Vector2 delta)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.25f));
            var step = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                pos = MoveAxis(map, pos, step.x, true, Mathf.Abs(step.y) < 1e-6f);
                pos = MoveAxis(map, pos, step.y, false, Mathf.Abs(step.x) < 1e-6f);
            }
            return pos;
        }

        static Vector2 MoveAxis(SurfaceMap map, Vector2 pos, float d, bool horizontal, bool straight)
        {
            if (d == 0f) return pos;
            var next = horizontal ? new Vector2(pos.x + d, pos.y) : new Vector2(pos.x, pos.y + d);
            if (!Blocked(map, next)) return next;

            // Corner help: walking straight at an opening that you're slightly off-centre from, ease
            // sideways into line with it instead of stopping dead on its edge.
            if (straight)
            {
                var here = new Cell(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y));
                var ahead = horizontal ? new Cell(here.X + (d > 0f ? 1 : -1), here.Y) : new Cell(here.X, here.Y + (d > 0f ? 1 : -1));
                if (map.IsWalkable(ahead))
                {
                    float off = horizontal ? pos.y - here.Y : pos.x - here.X;
                    float nudge = Mathf.Clamp(-off, -Mathf.Abs(d), Mathf.Abs(d));
                    var eased = horizontal ? new Vector2(pos.x, pos.y + nudge) : new Vector2(pos.x + nudge, pos.y);
                    if (!Blocked(map, eased)) return eased;
                }
            }

            return horizontal ? new Vector2(Snap(pos.x, d), pos.y) : new Vector2(pos.x, Snap(pos.y, d));
        }

        /// <summary>Slide right up against the edge of the tile you're bumping into.</summary>
        static float Snap(float c, float dir)
        {
            float tile = Mathf.Round(c);
            const float gap = 0.001f;
            return dir > 0f ? Mathf.Max(c, tile + 0.5f - Radius - gap) : Mathf.Min(c, tile - 0.5f + Radius + gap);
        }

        /// <summary>Does a box of half-width <see cref="Radius"/> at <paramref name="p"/> overlap any tile you can't stand on?</summary>
        static bool Blocked(SurfaceMap map, Vector2 p)
        {
            int x0 = Mathf.FloorToInt(p.x - Radius + 0.5f), x1 = Mathf.FloorToInt(p.x + Radius + 0.5f);
            int y0 = Mathf.FloorToInt(p.y - Radius + 0.5f), y1 = Mathf.FloorToInt(p.y + Radius + 0.5f);
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (!map.IsWalkable(new Cell(x, y))) return true;
            return false;
        }

        static bool PressedThisFrame(Keyboard kb, Facing f)
        {
            switch (f)
            {
                case Facing.North: return Pressed(kb.wKey, kb.upArrowKey);
                case Facing.East: return Pressed(kb.dKey, kb.rightArrowKey);
                case Facing.South: return Pressed(kb.sKey, kb.downArrowKey);
                default: return Pressed(kb.aKey, kb.leftArrowKey);
            }
        }

        static bool Pressed(KeyControl a, KeyControl b) => a.wasPressedThisFrame || b.wasPressedThisFrame;

        static bool Held(Keyboard kb, Facing f)
        {
            switch (f)
            {
                case Facing.North: return kb.wKey.isPressed || kb.upArrowKey.isPressed;
                case Facing.East: return kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                case Facing.South: return kb.sKey.isPressed || kb.downArrowKey.isPressed;
                default: return kb.aKey.isPressed || kb.leftArrowKey.isPressed;
            }
        }

        public void CycleSeed()
        {
            int i = Array.IndexOf(s_SeedChoices, SeedChoice);
            SeedChoice = s_SeedChoices[(i + 1) % s_SeedChoices.Length];
            SeedChoiceChanged?.Invoke();
            _game.ShowToast(SeedChoice == Item.None
                ? "Planting any seed you carry (Q to choose)"
                : $"Planting {Crops.ForSeed(SeedChoice).Name.ToLowerInvariant()} (Q to change)");
        }

        public static string SeedLabel(Item seed) => seed == Item.None ? "any seed" : Crops.ForSeed(seed).Name.ToLowerInvariant();
    }
}
