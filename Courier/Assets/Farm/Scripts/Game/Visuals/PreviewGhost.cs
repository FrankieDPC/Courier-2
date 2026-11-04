using Farm.Sim;
using UnityEngine;

namespace Farm.Game
{
    /// <summary>
    /// A pale, floating copy of the selected bot that acts out the previewed day. The real bot stays
    /// put; nothing the ghost does is real.
    /// </summary>
    public sealed class PreviewGhost : MonoBehaviour
    {
        const float Lift = 0.14f;

        GameController _game;
        PreviewController _preview;
        Transform _model;
        GameObject[] _load;
        TextMesh _alert;
        int _builtFor = -1;

        public void Init(GameController game, PreviewController preview)
        {
            _game = game;
            _preview = preview;
        }

        void Build(Bot b)
        {
            if (_model != null) Destroy(_model.gameObject);
            _builtFor = b.Id;

            var color = Color.Lerp(Palette.Bot(b.Id), Color.white, 0.55f);
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            Shapes.Box("Ring", _model, new Vector3(0f, -Lift + 0.09f, 0f), new Vector3(0.7f, 0.02f, 0.7f), Palette.Pick, false);
            var body = BotView.BuildModel(_model, color, b.Kind, Color.white, false);

            _load = new GameObject[5];
            for (int i = 0; i < _load.Length; i++)
                _load[i] = Shapes.Box("Load", body, new Vector3(0f, 0.2f + i * 0.1f, -0.2f), new Vector3(0.24f, 0.09f, 0.14f), Color.white, false);

            var label = Shapes.Label("Label", _model, new Vector3(0f, 1.05f, 0f), "preview", Palette.Pick, 0.035f);
            label.gameObject.AddComponent<Billboard>();
            _alert = Shapes.Label("Alert", _model, new Vector3(0f, 1.3f, 0f), "!", Palette.Error, 0.07f);
            _alert.gameObject.AddComponent<Billboard>();
        }

        void LateUpdate()
        {
            var c = _game.SelectedBot;
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
            var pos = Vector3.Lerp(FarmView.CellToWorld(from.Position), FarmView.CellToWorld(to.Position), s);
            if (frac > 0f && to.Result == CommandResult.Blocked && to.Path.Length > 0) pos += rot * Vector3.forward * (0.25f * Mathf.Sin(frac * Mathf.PI));

            _model.localPosition = pos + Vector3.up * (Lift + 0.03f * Mathf.Sin(Time.time * 3f));
            _model.localRotation = rot;

            var loadFrame = frac > 0.5f ? to : from;
            BotView.ShowLoad(_load, loadFrame.LoadWeight, loadFrame.Capacity, loadFrame.LoadMain);

            _preview.TryGetFrame(c, out var shown);
            bool problem = shown.Path.Length > 0 && CommandInfo.IsProblem(shown.Result);
            _alert.gameObject.SetActive(problem);
            if (problem) _alert.color = CommandInfo.IsHardProblem(shown.Result) ? Palette.Error : Palette.Warning;
        }
    }
}
