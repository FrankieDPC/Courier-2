using System.Collections.Generic;
using Courier.Sim;
using UnityEngine;

namespace Courier.Game
{
    /// <summary>
    /// Placeholder building: a coloured block with a status light, a cycle progress bar, and little
    /// crates on the roof showing stock (front row = finished output, back row = waiting input).
    /// </summary>
    public sealed class BuildingView : MonoBehaviour
    {
        const int MaxPips = 6;

        Building _b;
        GameObject _statusLight;
        Transform _progress;
        float _height;
        readonly List<GameObject> _outputPips = new List<GameObject>();
        readonly List<GameObject> _inputPips = new List<GameObject>();

        public void Init(Building b)
        {
            _b = b;
            var type = b.Type;
            var color = Palette.For(type);
            _height = type.IsTownHall ? 1.0f : 0.55f;
            float footprint = type.IsTownHall ? 0.86f : 0.78f;

            Shapes.Box("Body", transform, new Vector3(0f, _height * 0.5f, 0f), new Vector3(footprint, _height, footprint), color);
            Shapes.Box("Roof", transform, new Vector3(0f, _height + 0.03f, 0f), new Vector3(footprint * 0.9f, 0.06f, footprint * 0.9f), Palette.Shade(color, 0.8f));

            float roof = _height + 0.06f;

            if (type.IsProducer)
            {
                _statusLight = Shapes.Box("Status", transform, new Vector3(footprint * 0.5f - 0.08f, roof + 0.06f, footprint * 0.5f - 0.08f), Vector3.one * 0.12f, Palette.Idle, false);

                // Progress bar along the front (camera-facing) edge.
                var barRoot = new GameObject("Progress").transform;
                barRoot.SetParent(transform, false);
                barRoot.localPosition = new Vector3(-footprint * 0.5f, 0.08f, -footprint * 0.5f - 0.02f);
                _progress = Shapes.Box("Fill", barRoot, new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.06f, 0.04f), Palette.Working, false).transform;
                barRoot.localScale = new Vector3(footprint, 1f, 1f);

                if (type.Outputs.Count > 0) MakePips(_outputPips, Palette.For(type.Outputs[0].Resource), roof, -0.2f);
                if (type.Inputs.Count > 0) MakePips(_inputPips, Palette.For(type.Inputs[0].Resource), roof, 0.2f);
            }

            var label = Shapes.Label("Label", transform, new Vector3(0f, roof + 0.25f, 0f), type.DisplayName, Palette.Ink, 0.04f);
            label.gameObject.AddComponent<Billboard>();

            Refresh();
        }

        void MakePips(List<GameObject> list, Color color, float roof, float z)
        {
            for (int i = 0; i < MaxPips; i++)
            {
                float x = -0.25f + i * 0.1f;
                var pip = Shapes.Box("Pip", transform, new Vector3(x, roof + 0.045f, z), Vector3.one * 0.08f, color, false);
                list.Add(pip);
            }
        }

        public void Refresh()
        {
            if (_b == null || !_b.Type.IsProducer) return;

            Shapes.Recolor(_statusLight, Palette.For(_b.Status));
            _progress.localScale = new Vector3(Mathf.Max(0.001f, _b.CycleProgress01), 1f, 1f);
            _progress.localPosition = new Vector3(_progress.localScale.x * 0.5f, 0f, 0f);

            if (_b.Type.Outputs.Count > 0) ShowCount(_outputPips, _b.Output.Get(_b.Type.Outputs[0].Resource));
            if (_b.Type.Inputs.Count > 0) ShowCount(_inputPips, _b.Input.Get(_b.Type.Inputs[0].Resource));
        }

        static void ShowCount(List<GameObject> pips, int count)
        {
            for (int i = 0; i < pips.Count; i++) pips[i].SetActive(i < count);
        }
    }
}
