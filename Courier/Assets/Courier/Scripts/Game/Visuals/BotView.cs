using Courier.Sim;
using UnityEngine;

namespace Courier.Game
{
    /// <summary>
    /// Placeholder courier: a little cart with a dark "nose" showing which way it faces, cargo
    /// crates on its back, and a "!" when its last command had a problem. Interpolates between
    /// ticks using <see cref="GameController.TickProgress"/>.
    /// </summary>
    public sealed class BotView : MonoBehaviour
    {
        CourierBot _bot;
        GameController _game;
        GameObject[] _cargo;
        GameObject _selection;
        TextMesh _alert;

        Vector3 _from, _to;
        Quaternion _fromRot, _toRot;
        bool _bump;

        /// <summary>Visual nudge used when several bots share a cell.</summary>
        public Vector3 Offset { get; set; }

        public void Init(CourierBot bot, GameController game)
        {
            _bot = bot;
            _game = game;
            var color = Palette.Bot(bot.Id);

            _selection = Shapes.Box("Selection", transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.78f, 0.02f, 0.78f), Palette.Selection, false);
            Shapes.Box("Body", transform, new Vector3(0f, 0.18f, 0f), new Vector3(0.44f, 0.22f, 0.56f), color);
            Shapes.Box("Nose", transform, new Vector3(0f, 0.24f, 0.25f), new Vector3(0.36f, 0.12f, 0.1f), Palette.Ink);

            _cargo = new GameObject[Mathf.Max(1, bot.Capacity)];
            for (int i = 0; i < _cargo.Length; i++)
            {
                var pos = new Vector3(0f, 0.36f + i * 0.13f, -0.08f);
                _cargo[i] = Shapes.Box("Cargo", transform, pos, Vector3.one * 0.18f, Color.white);
            }

            _alert = Shapes.Label("Alert", transform, new Vector3(0f, 0.8f, 0f), "!", Palette.Error, 0.07f);
            _alert.gameObject.AddComponent<Billboard>();

            Snap();
        }

        Vector3 WorldPos(Cell c) => TownView.CellToWorld(c) + Offset;
        static Quaternion Rot(Facing f) => Quaternion.Euler(0f, f.ToYawDegrees(), 0f);

        /// <summary>Jump straight to the bot's current state (day start/end).</summary>
        public void Snap()
        {
            _from = _to = WorldPos(_bot.Position);
            _fromRot = _toRot = Rot(_bot.Facing);
            _bump = false;
            RefreshDecor();
            Apply(1f);
        }

        /// <summary>Called after every sim tick: animate from the previous state to the new one.</summary>
        public void OnTick()
        {
            _from = WorldPos(_bot.PreviousPosition);
            _to = WorldPos(_bot.Position);
            _fromRot = Rot(_bot.PreviousFacing);
            _toRot = Rot(_bot.Facing);
            _bump = _bot.LastResult == CommandResult.Blocked;
            RefreshDecor();
        }

        void RefreshDecor()
        {
            var cargoColor = Palette.For(_bot.Cargo);
            for (int i = 0; i < _cargo.Length; i++)
            {
                bool on = i < _bot.CargoCount;
                _cargo[i].SetActive(on);
                if (on) Shapes.Recolor(_cargo[i], cargoColor);
            }

            bool problem = _game.Town.Phase == DayPhase.Running && CommandInfo.IsProblem(_bot.LastResult);
            _alert.gameObject.SetActive(problem);
            if (problem) _alert.color = CommandInfo.IsHardProblem(_bot.LastResult) ? Palette.Error : Palette.Warning;
        }

        void Update()
        {
            _selection.SetActive(_game.SelectedBot == _bot);
            Apply(_game.TickProgress);
        }

        void Apply(float t)
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            var pos = Vector3.Lerp(_from, _to, s);
            var rot = Quaternion.Slerp(_fromRot, _toRot, s);
            if (_bump)
            {
                // Nudge into the obstacle and back so a blocked move is visible.
                pos += rot * Vector3.forward * (0.25f * Mathf.Sin(t * Mathf.PI));
            }
            transform.localPosition = pos;
            transform.localRotation = rot;
        }
    }
}
