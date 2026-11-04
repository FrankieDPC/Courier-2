using System;
using Bunker.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Bunker.Game
{
    /// <summary>
    /// Steers your robot. Key presses are recorded every frame; the GameController asks for a command
    /// (<see cref="TryGetCommand"/>) right before each sim tick, so you move one tile per tick like
    /// everyone else and nothing is lost or doubled at high speed.
    ///   WASD / arrows   tap to turn to face that way; tap again or hold to walk
    ///   E               do the obvious job on the tile in front of you (ignored while you're busy)
    ///   Q               choose which seed to plant / take from the hatch
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        static readonly Item[] s_SeedChoices = { Item.None, Item.RadishSeeds, Item.PotatoSeeds, Item.SquashSeeds };

        GameController _game;

        bool _useTap;          // E pressed, not yet used
        bool _hasTap;          // a direction pressed since the last tick
        Facing _tapDir;
        bool _hasHold;         // the direction key being held (most recent wins)
        Facing _holdDir;
        bool _holdReady;       // held through at least one tick since it was pressed: walking repeats

        /// <summary>Which seed E plants or takes. None = any / some of each.</summary>
        public Item SeedChoice { get; private set; } = Item.None;

        public event Action SeedChoiceChanged;

        public void Init(GameController game) => _game = game;

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
            if (_game == null || _game.World == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (!UIFactory.IsTyping() && kb.qKey.wasPressedThisFrame) CycleSeed();

            if (!CanAct)
            {
                _useTap = _hasTap = _hasHold = _holdReady = false;
                return;
            }
            if (!_hasHold)
            {
                // A key held through a pause, the night or the interface: carry on walking that way.
                for (int f = 0; f < 4; f++)
                {
                    if (!Held(kb, (Facing)f) || PressedThisFrame(kb, (Facing)f)) continue;
                    _holdDir = (Facing)f;
                    _hasHold = true;
                    _holdReady = true;
                    break;
                }
            }

            // E: one job per press; presses while you're busy (or already waiting) are dropped so a
            // double-tap can't chain into a second, different job.
            if (kb.eKey.wasPressedThisFrame && !_game.World.PlayerBusy) _useTap = true;

            // Directions: remember fresh presses (the newest wins) and which key is held.
            for (int f = 0; f < 4; f++)
            {
                if (!PressedThisFrame(kb, (Facing)f)) continue;
                _tapDir = (Facing)f;
                _hasTap = true;
                _holdDir = (Facing)f;
                _hasHold = true;
                _holdReady = false;
            }
            if (_hasHold && !Held(kb, _holdDir))
            {
                _hasHold = false;
                _holdReady = false;
                for (int f = 0; f < 4; f++)
                {
                    if (!Held(kb, (Facing)f)) continue;
                    _holdDir = (Facing)f; // another key is still down: carry on that way
                    _hasHold = true;
                    _holdReady = true;
                    break;
                }
            }
        }

        /// <summary>The command for this tick, if any. Called by the GameController just before each step.</summary>
        public bool TryGetCommand(out Command cmd)
        {
            cmd = default;
            var w = _game.World;
            if (!CanAct) return false;
            var p = w.Player;
            if (w.PlayerBusy)
            {
                _hasTap = false; // taps during a job are dropped, not replayed after it
                return false;
            }

            if (_useTap)
            {
                _useTap = false;
                _hasTap = false;
                cmd = w.SuggestUse(p, SeedChoice);
                return true;
            }

            if (_hasTap)
            {
                _hasTap = false;
                var dir = _tapDir;
                if (p.Facing != dir)
                {
                    cmd = Command.Face(dir); // a tap turns you; keep holding to walk
                    return true;
                }
                return StepOrFace(w, p, dir, out cmd);
            }

            if (_hasHold)
            {
                if (!_holdReady)
                {
                    _holdReady = true; // the press was handled last tick; from now on, holding walks
                    if (p.Facing != _holdDir) return false;
                }
                return StepOrFace(w, p, _holdDir, out cmd);
            }
            return false;
        }

        static bool StepOrFace(World w, Bot p, Facing dir, out Command cmd)
        {
            if (w.Map.IsWalkable(p.Position.Step(dir)))
            {
                cmd = Command.Step(dir);
                return true;
            }
            cmd = Command.Face(dir);
            return p.Facing != dir; // already facing what's in the way: nothing to do
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
