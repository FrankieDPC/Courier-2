using Wizard.Sim;
using UnityEngine;

namespace Wizard.Game
{
    /// <summary>
    /// A pale, floating copy of the selected construct that acts out the previewed day. The real
    /// construct stays put; nothing the ghost does is real.
    /// </summary>
    public sealed class PreviewGhost : MonoBehaviour
    {
        const float Lift = 0.14f;

        GameController _game;
        PreviewController _preview;
        Transform _model;
        GameObject[] _cargo;
        TextMesh _alert;
        int _builtFor = -1;

        public void Init(GameController game, PreviewController preview)
        {
            _game = game;
            _preview = preview;
        }

        void Build(Construct c)
        {
            if (_model != null) Destroy(_model.gameObject);
            _builtFor = c.Id;

            var color = Color.Lerp(Palette.Construct(c.Id), Color.white, 0.55f);
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);

            var body = new GameObject("Body").transform;
            body.SetParent(_model, false);
            Shapes.Box("Ring", _model, new Vector3(0f, -Lift + 0.015f, 0f), new Vector3(0.7f, 0.02f, 0.7f), Palette.Accent, false);
            Shapes.Box("Legs", body, new Vector3(0f, 0.08f, 0f), new Vector3(0.36f, 0.16f, 0.3f), Palette.Shade(color, 0.85f), false);
            Shapes.Box("Torso", body, new Vector3(0f, 0.3f, 0f), new Vector3(0.46f, 0.3f, 0.42f), color, false);
            Shapes.Box("Eye", body, new Vector3(0f, 0.34f, 0.22f), new Vector3(0.2f, 0.08f, 0.04f), Palette.Accent, false);

            _cargo = new GameObject[Mathf.Max(1, c.Capacity)];
            for (int i = 0; i < _cargo.Length; i++)
                _cargo[i] = Shapes.Box("Cargo", body, new Vector3(0f, 0.53f + i * 0.15f, -0.06f), Vector3.one * 0.2f, Color.white, false);

            var label = Shapes.Label("Label", _model, new Vector3(0f, 1.05f, 0f), "preview", Palette.Accent, 0.035f);
            label.gameObject.AddComponent<Billboard>();
            _alert = Shapes.Label("Alert", _model, new Vector3(0f, 1.3f, 0f), "!", Palette.Error, 0.07f);
            _alert.gameObject.AddComponent<Billboard>();
        }

        void LateUpdate()
        {
            var c = _game.SelectedConstruct;
            var result = _preview.Result;
            if (!_preview.IsOpen || result == null || c == null)
            {
                if (_model != null) _model.gameObject.SetActive(false);
                return;
            }

            if (_builtFor != c.Id || _model == null) Build(c);
            _model.gameObject.SetActive(true);

            float t = _preview.PlayTick;
            int k = Mathf.FloorToInt(t);
            float frac = t - k;
            if (!result.TryGetFrame(c.Id, k, out var from)) return;
            result.TryGetFrame(c.Id, k + 1, out var to);
            if (k >= result.EndTick) to = from;

            float s = Mathf.SmoothStep(0f, 1f, frac);
            var rotFrom = Quaternion.Euler(0f, from.Facing.ToYawDegrees(), 0f);
            var rotTo = Quaternion.Euler(0f, to.Facing.ToYawDegrees(), 0f);
            var rot = Quaternion.Slerp(rotFrom, rotTo, s);
            var pos = Vector3.Lerp(WorldView.CellToWorld(from.Position), WorldView.CellToWorld(to.Position), s);
            if (frac > 0f && to.Result == CommandResult.Blocked) pos += rot * Vector3.forward * (0.25f * Mathf.Sin(frac * Mathf.PI));

            _model.localPosition = pos + Vector3.up * (Lift + 0.03f * Mathf.Sin(Time.time * 3f));
            _model.localRotation = rot;

            var cargoFrame = frac > 0.5f ? to : from;
            var cargoColor = Palette.For(cargoFrame.Cargo);
            for (int i = 0; i < _cargo.Length; i++)
            {
                bool on = i < cargoFrame.CargoCount;
                _cargo[i].SetActive(on);
                if (on) Shapes.Recolor(_cargo[i], cargoColor);
            }

            _preview.TryGetFrame(c, out var shown);
            bool problem = CommandInfo.IsProblem(shown.Result);
            _alert.gameObject.SetActive(problem);
            if (problem) _alert.color = CommandInfo.IsHardProblem(shown.Result) ? Palette.Error : Palette.Warning;
        }
    }
}
