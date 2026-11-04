using System;
using System.Collections.Generic;
using Wizard.Sim;
using UnityEngine;

namespace Wizard.Game
{
    /// <summary>
    /// "Preview day": simulates the day on a copy of the world (see <see cref="DayPreview"/>) and plays
    /// it back for the selected construct as a ghost, with a scrubbable timeline. Works while planning
    /// (previews the whole next day) or while paused (previews the rest of today). Re-runs instantly
    /// whenever a routine, spell, building or the map changes, so you can edit and watch the result.
    /// </summary>
    public sealed class PreviewController : MonoBehaviour
    {
        /// <summary>Playback speed at 1x game speed (real time is 4 ticks/second).</summary>
        public const float TicksPerSecond = 12f;

        GameController _game;
        World _world;
        PreviewGhost _ghost;
        bool _dirty;
        readonly List<Construct> _watched = new List<Construct>();

        public bool IsOpen { get; private set; }
        public DayPreview Result { get; private set; }

        /// <summary>Playback position in ticks (fractional while playing).</summary>
        public float PlayTick { get; private set; }
        public bool Playing { get; private set; }

        /// <summary>Bumps every time the preview is (re)computed, so UI knows to rebuild its summary.</summary>
        public int Version { get; private set; }

        /// <summary>Opened, closed or recomputed.</summary>
        public event Action Changed;

        public bool CanOpen => _world != null && !_world.IsGameOver && (_world.Phase == DayPhase.Planning || _game.Paused);

        public void Init(GameController game)
        {
            _game = game;
            _world = game.World;
            game.Preview = this;

            _world.Spellbook.Changed += MarkDirty;
            _world.Spellbook.SpellEdited += OnSpellEdited;
            _world.ConstructAdded += OnConstructAdded;
            _world.BuildingAdded += OnBuildingAdded;
            _world.Grid.Expanded += MarkDirty;
            foreach (var c in _world.Constructs) Watch(c);

            _ghost = new GameObject("Preview Ghost").AddComponent<PreviewGhost>();
            _ghost.Init(game, this);
        }

        void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            if (_world == null) return;
            _world.Spellbook.Changed -= MarkDirty;
            _world.Spellbook.SpellEdited -= OnSpellEdited;
            _world.ConstructAdded -= OnConstructAdded;
            _world.BuildingAdded -= OnBuildingAdded;
            _world.Grid.Expanded -= MarkDirty;
            foreach (var c in _watched) c.Routine.Changed -= MarkDirty;
        }

        void Watch(Construct c)
        {
            c.Routine.Changed += MarkDirty;
            _watched.Add(c);
        }

        void OnConstructAdded(Construct c)
        {
            Watch(c);
            MarkDirty();
        }

        void OnBuildingAdded(Building _) => MarkDirty();
        void OnSpellEdited(Spell _) => MarkDirty();

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
                _game.ShowToast(_world.IsGameOver ? "The game is over" : "Pause first (Space) to preview the rest of the day");
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
        public bool TryGetFrame(Construct c, out PreviewFrame frame)
        {
            frame = default;
            if (!IsOpen || Result == null || c == null) return false;
            return Result.TryGetFrame(c.Id, Mathf.CeilToInt(PlayTick - 0.0001f), out frame);
        }

        void Recompute()
        {
            Result = DayPreview.Run(_world);
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
