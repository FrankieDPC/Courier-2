using System;
using System.Collections.Generic;
using Bunker.Sim;
using UnityEngine;

namespace Bunker.Game
{
    /// <summary>
    /// "Preview day": simulates the day on a copy of the surface (see <see cref="DayPreview"/>) and plays it
    /// back for the selected bot as a ghost, with a scrubbable timeline. Works while time is stopped (a
    /// bot's interface is open, or you paused) and previews the rest of today. Re-runs instantly whenever a
    /// routine, custom command, the stores or a bot changes, so you can edit and watch the result.
    /// </summary>
    public sealed class PreviewController : MonoBehaviour
    {
        /// <summary>Playback speed at 1x game speed (real time is 4 ticks/second).</summary>
        public const float TicksPerSecond = 90f;

        GameController _game;
        World _world;
        PreviewGhost _ghost;
        bool _dirty;
        readonly List<Bot> _watched = new List<Bot>();

        public bool IsOpen { get; private set; }
        public DayPreview Result { get; private set; }

        /// <summary>Playback position in ticks (fractional while playing).</summary>
        public float PlayTick { get; private set; }
        public bool Playing { get; private set; }

        /// <summary>Bumps every time the preview is (re)computed, so UI knows to rebuild its summary.</summary>
        public int Version { get; private set; }

        /// <summary>Opened, closed or recomputed.</summary>
        public event Action Changed;

        public bool CanOpen => _world != null && !_world.IsGameOver && _game.Paused && !_game.InNight;

        public void Init(GameController game)
        {
            _game = game;
            _world = game.World;
            game.Preview = this;

            _world.Library.Changed += MarkDirty;
            _world.Library.MacroEdited += OnMacroEdited;
            _world.BotAdded += OnBotAdded;
            _world.Stores.Changed += MarkDirty;
            foreach (var b in _world.Bots) Watch(b);

            _ghost = new GameObject("Preview Ghost").AddComponent<PreviewGhost>();
            _ghost.Init(game, this);
        }

        void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            if (_world == null) return;
            _world.Library.Changed -= MarkDirty;
            _world.Library.MacroEdited -= OnMacroEdited;
            _world.BotAdded -= OnBotAdded;
            _world.Stores.Changed -= MarkDirty;
            foreach (var c in _watched) c.Routine.Changed -= MarkDirty;
        }

        void Watch(Bot b)
        {
            b.Routine.Changed += MarkDirty;
            _watched.Add(b);
        }

        void OnBotAdded(Bot b)
        {
            Watch(b);
            MarkDirty();
        }

        void OnMacroEdited(Macro _) => MarkDirty();

        /// <summary>Something that affects the day changed: recompute on the next frame.</summary>
        public void MarkDirty()
        {
            if (IsOpen) _dirty = true;
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (!CanOpen)
            {
                _game.ShowToast(_world.IsGameOver ? "It's all over - press Play again" : "Open a bot (click it) or pause (Space) to preview the rest of the day");
                return;
            }
            IsOpen = true;
            Recompute();
            PlayTick = Result.StartTick;
            Playing = true;
            Changed?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Playing = false;
            Result = null;
            _dirty = false;
            Changed?.Invoke();
        }

        public void TogglePlay()
        {
            if (Result == null) return;
            if (!Playing && PlayTick >= Result.EndTick) PlayTick = Result.StartTick;
            Playing = !Playing;
        }

        public void SetTick(float tick, bool pause = true)
        {
            if (Result == null) return;
            PlayTick = Mathf.Clamp(tick, Result.StartTick, Result.EndTick);
            if (pause) Playing = false;
        }

        /// <summary>
        /// The frame for the command being shown: while animating from tick k to k+1 that's frame k+1
        /// (the command in progress); when stopped exactly on tick k it's frame k (what just happened).
        /// </summary>
        public bool TryGetFrame(Bot c, out PreviewFrame frame)
        {
            frame = default;
            if (!IsOpen || Result == null || c == null) return false;
            return Result.TryGetFrame(c.Id, Mathf.CeilToInt(PlayTick - 0.0001f), out frame);
        }

        void Recompute()
        {
            Result = DayPreview.Run(_world, _game.WillRestart);
            PlayTick = Mathf.Clamp(PlayTick, Result.StartTick, Result.EndTick);
            Version++;
            Changed?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            if (!CanOpen)
            {
                Close(); // the day started or resumed: the real thing takes over
                return;
            }

            if (_dirty)
            {
                _dirty = false;
                Recompute();
            }

            if (Playing)
            {
                PlayTick += Time.unscaledDeltaTime * TicksPerSecond * _game.Speed;
                if (PlayTick >= Result.EndTick)
                {
                    PlayTick = Result.EndTick;
                    Playing = false;
                }
            }
        }
    }
}
