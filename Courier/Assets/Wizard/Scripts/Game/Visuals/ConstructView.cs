using Wizard.Sim;
using UnityEngine;

namespace Wizard.Game
{
    /// <summary>
    /// Placeholder construct: a stout little golem with a glowing eye on the side it faces, cargo
    /// crates on its back, and a "!" when its last command had a problem. Interpolates between
    /// ticks using <see cref="GameController.TickProgress"/>.
    /// </summary>
    public sealed class ConstructView : MonoBehaviour
    {
        Construct _c;
        GameController _game;
        GameObject[] _cargo;
        GameObject _selection;
        TextMesh _alert;
        Transform _body;

        Vector3 _from, _to;
        Quaternion _fromRot, _toRot;
        bool _bump;

        /// <summary>Visual nudge used when several constructs share a cell.</summary>
        public Vector3 Offset { get; set; }

        public void Init(Construct c, GameController game)
        {
            _c = c;
            _game = game;
            var color = Palette.Construct(c.Id);

            _selection = Shapes.Box("Selection", transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.78f, 0.02f, 0.78f), Palette.Selection, false);
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            Shapes.Box("Legs", _body, new Vector3(0f, 0.08f, 0f), new Vector3(0.36f, 0.16f, 0.3f), Palette.Shade(color, 0.6f));
            Shapes.Box("Torso", _body, new Vector3(0f, 0.3f, 0f), new Vector3(0.46f, 0.3f, 0.42f), color);
            Shapes.Box("Eye", _body, new Vector3(0f, 0.34f, 0.22f), new Vector3(0.2f, 0.08f, 0.04f), Palette.Beam);

            _cargo = new GameObject[Mathf.Max(1, c.Capacity)];
            for (int i = 0; i < _cargo.Length; i++)
                _cargo[i] = Shapes.Box("Cargo", _body, new Vector3(0f, 0.53f + i * 0.15f, -0.06f), Vector3.one * 0.2f, Color.white);

            _alert = Shapes.Label("Alert", transform, new Vector3(0f, 1.0f, 0f), "!", Palette.Error, 0.07f);
            _alert.gameObject.AddComponent<Billboard>();

            Snap();
        }

        Vector3 WorldPos(Cell c) => WorldView.CellToWorld(c) + Offset;
        static Quaternion Rot(Facing f) => Quaternion.Euler(0f, f.ToYawDegrees(), 0f);

        public void Snap()
        {
            _from = _to = WorldPos(_c.Position);
            _fromRot = _toRot = Rot(_c.Facing);
            _bump = false;
            RefreshDecor();
            Apply(1f);
        }

        public void OnTick()
        {
            _from = WorldPos(_c.PreviousPosition);
            _to = WorldPos(_c.Position);
            _fromRot = Rot(_c.PreviousFacing);
            _toRot = Rot(_c.Facing);
            _bump = _c.LastResult == CommandResult.Blocked;
            RefreshDecor();
        }

        void RefreshDecor()
        {
            var cargoColor = Palette.For(_c.Cargo);
            for (int i = 0; i < _cargo.Length; i++)
            {
                bool on = i < _c.CargoCount;
                _cargo[i].SetActive(on);
                if (on) Shapes.Recolor(_cargo[i], cargoColor);
            }

            bool problem = _game.World.Phase == DayPhase.Running && CommandInfo.IsProblem(_c.LastResult);
            _alert.gameObject.SetActive(problem);
            if (problem) _alert.color = CommandInfo.IsHardProblem(_c.LastResult) ? Palette.Error : Palette.Warning;
        }

        void Update()
        {
            _selection.SetActive(_game.SelectedConstruct == _c);
            Apply(_game.TickProgress);
            // A little hover-bob so they read as magical.
            _body.localPosition = new Vector3(0f, 0.04f + 0.03f * Mathf.Sin(Time.time * 4f + _c.Id), 0f);
        }

        void Apply(float t)
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            var pos = Vector3.Lerp(_from, _to, s);
            var rot = Quaternion.Slerp(_fromRot, _toRot, s);
            if (_bump) pos += rot * Vector3.forward * (0.25f * Mathf.Sin(t * Mathf.PI));
            transform.localPosition = pos;
            transform.localRotation = rot;
        }
    }
}
