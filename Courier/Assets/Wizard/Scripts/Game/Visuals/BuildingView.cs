using System.Collections.Generic;
using Wizard.Sim;
using UnityEngine;

namespace Wizard.Game
{
    /// <summary>
    /// Placeholder buildings built from boxes:
    ///   Tower     tall stacked block with a glowing orb, flashes red when hit
    ///   Cottage   little house with a "!" while its villager has an open job
    ///   Ward      pylon with an orb and an ammo gauge
    ///   Producer  block with a status light, cycle bar and stock crates (front = output, back = input)
    /// </summary>
    public sealed class BuildingView : MonoBehaviour
    {
        const int MaxPips = 6;

        Building _b;
        GameController _game;
        GameObject _statusLight;
        Transform _progress;
        GameObject _jobMarker;
        GameObject _orb;
        Transform _ammoFill;
        Renderer[] _bodyRenderers;
        float _flashUntil;
        readonly List<GameObject> _outputPips = new List<GameObject>();
        readonly List<GameObject> _inputPips = new List<GameObject>();

        public void Init(Building b, GameController game)
        {
            _b = b;
            _game = game;
            var type = b.Type;
            var color = Palette.For(type);
            float labelHeight;

            switch (type.Role)
            {
                case BuildingRole.Tower:
                    Shapes.Box("Base", transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.9f), color);
                    Shapes.Box("Spire", transform, new Vector3(0f, 1.2f, 0f), new Vector3(0.6f, 0.7f, 0.6f), Palette.Shade(color, 1.15f));
                    Shapes.Box("Cap", transform, new Vector3(0f, 1.62f, 0f), new Vector3(0.7f, 0.14f, 0.7f), Palette.Shade(color, 0.75f));
                    _orb = Shapes.Box("Orb", transform, new Vector3(0f, 1.86f, 0f), Vector3.one * 0.26f, Palette.DoomGlow);
                    _orb.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
                    labelHeight = 2.15f;
                    break;

                case BuildingRole.Cottage:
                    Shapes.Box("Body", transform, new Vector3(0f, 0.22f, 0f), new Vector3(0.66f, 0.44f, 0.6f), color);
                    var roof = Shapes.Box("Roof", transform, new Vector3(0f, 0.52f, 0f), new Vector3(0.5f, 0.5f, 0.66f), Palette.Hex("#9C4A3A"));
                    roof.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    var marker = Shapes.Label("Job", transform, new Vector3(0f, 1.0f, 0f), "!", Palette.Job, 0.09f);
                    marker.gameObject.AddComponent<Billboard>();
                    _jobMarker = marker.gameObject;
                    labelHeight = 0.85f;
                    break;

                case BuildingRole.Ward:
                    Shapes.Box("Plinth", transform, new Vector3(0f, 0.08f, 0f), new Vector3(0.6f, 0.16f, 0.6f), Palette.Shade(color, 0.8f));
                    Shapes.Box("Pylon", transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.22f, 0.8f, 0.22f), color);
                    _orb = Shapes.Box("Orb", transform, new Vector3(0f, 1.1f, 0f), Vector3.one * 0.24f, Palette.Idle);
                    _orb.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
                    var gauge = new GameObject("Ammo").transform;
                    gauge.SetParent(transform, false);
                    gauge.localPosition = new Vector3(0.22f, 0.16f, -0.22f);
                    Shapes.Box("Track", gauge, new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.6f, 0.08f), Palette.Shade(color, 0.6f), false);
                    _ammoFill = Shapes.Box("Fill", gauge, Vector3.zero, new Vector3(0.1f, 0.6f, 0.1f), Palette.For(Resource.Charge), false).transform;
                    labelHeight = 1.35f;
                    break;

                default:
                    labelHeight = BuildProducer(type, color);
                    break;
            }

            var label = Shapes.Label("Label", transform, new Vector3(0f, labelHeight, 0f), b.Name, Palette.Ink, 0.04f);
            label.gameObject.AddComponent<Billboard>();
            _bodyRenderers = GetComponentsInChildren<Renderer>();
            Refresh();
        }

        float BuildProducer(BuildingType type, Color color)
        {
            const float height = 0.55f;
            const float footprint = 0.78f;
            Shapes.Box("Body", transform, new Vector3(0f, height * 0.5f, 0f), new Vector3(footprint, height, footprint), color);
            Shapes.Box("Roof", transform, new Vector3(0f, height + 0.03f, 0f), new Vector3(footprint * 0.9f, 0.06f, footprint * 0.9f), Palette.Shade(color, 0.8f));
            float roof = height + 0.06f;

            _statusLight = Shapes.Box("Status", transform, new Vector3(footprint * 0.5f - 0.08f, roof + 0.06f, footprint * 0.5f - 0.08f), Vector3.one * 0.12f, Palette.Idle, false);

            var barRoot = new GameObject("Progress").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = new Vector3(-footprint * 0.5f, 0.08f, -footprint * 0.5f - 0.02f);
            _progress = Shapes.Box("Fill", barRoot, new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.06f, 0.04f), Palette.Working, false).transform;
            barRoot.localScale = new Vector3(footprint, 1f, 1f);

            if (type.Outputs.Count > 0) MakePips(_outputPips, Palette.For(type.Outputs[0].Resource), roof, -0.2f);
            if (type.Inputs.Count > 0) MakePips(_inputPips, Palette.For(type.Inputs[0].Resource), roof, 0.2f);
            return roof + 0.25f;
        }

        void MakePips(List<GameObject> list, Color color, float roof, float z)
        {
            for (int i = 0; i < MaxPips; i++)
            {
                float x = -0.25f + i * 0.1f;
                list.Add(Shapes.Box("Pip", transform, new Vector3(x, roof + 0.045f, z), Vector3.one * 0.08f, color, false));
            }
        }

        public void Refresh()
        {
            if (_b == null) return;
            var type = _b.Type;

            if (type.IsCottage)
            {
                bool hasJob = false;
                foreach (var j in _game.World.Jobs)
                    if (j.IsOpen && j.Giver == _b) hasJob = true;
                _jobMarker.SetActive(hasJob);
                return;
            }

            if (type.IsWard)
            {
                int ammo = _game.World.AmmoOf(_b);
                float k = type.InputCapacity > 0 ? Mathf.Clamp01(ammo / (float)type.InputCapacity) : 0f;
                _ammoFill.localScale = new Vector3(0.1f, Mathf.Max(0.001f, 0.6f * k), 0.1f);
                _ammoFill.localPosition = new Vector3(0f, 0.3f * k, 0f);
                Shapes.Recolor(_orb, ammo > 0 ? Palette.Beam : Palette.Idle);
                return;
            }

            if (!type.IsProducer) return;

            Shapes.Recolor(_statusLight, Palette.For(_b.Status));
            _progress.localScale = new Vector3(Mathf.Max(0.001f, _b.CycleProgress01), 1f, 1f);
            _progress.localPosition = new Vector3(_progress.localScale.x * 0.5f, 0f, 0f);

            if (type.Outputs.Count > 0) ShowCount(_outputPips, _b.Output.Get(type.Outputs[0].Resource));
            if (type.Inputs.Count > 0) ShowCount(_inputPips, _b.Input.Get(type.Inputs[0].Resource));
        }

        /// <summary>Brief red flash (the tower taking a hit).</summary>
        public void Flash() => _flashUntil = Time.time + 0.2f;

        void Update()
        {
            if (_orb != null && _b.Type.IsTower)
                _orb.transform.localRotation = Quaternion.Euler(45f, Time.time * 60f, 0f);

            if (_flashUntil <= 0f) return;
            bool on = Time.time < _flashUntil;
            foreach (var r in _bodyRenderers)
                if (r != null && r.gameObject != _orb) r.enabled = !on || Mathf.Repeat(Time.time * 20f, 1f) > 0.5f;
            if (!on) _flashUntil = 0f;
        }

        static void ShowCount(List<GameObject> pips, int count)
        {
            for (int i = 0; i < pips.Count; i++) pips[i].SetActive(i < count);
        }
    }
}
