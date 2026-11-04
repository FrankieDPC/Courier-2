using UnityEngine;
using UnityEngine.Rendering;

namespace Wizard.Game
{
    /// <summary>A short-lived zap from a ward or the tower to a monster.</summary>
    public sealed class Beam : MonoBehaviour
    {
        const float Duration = 0.18f;
        LineRenderer _line;
        float _until;

        public bool Busy => _line != null && _line.enabled;

        void Awake()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
            _line.sharedMaterial = Shapes.Unlit(Palette.Beam);
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.enabled = false;
        }

        public void Fire(Vector3 from, Vector3 to)
        {
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            _line.widthMultiplier = 0.12f;
            _line.enabled = true;
            _until = Time.time + Duration;
        }

        void Update()
        {
            if (!_line.enabled) return;
            float t = Mathf.Clamp01((_until - Time.time) / Duration);
            _line.widthMultiplier = 0.12f * t;
            if (t <= 0f) _line.enabled = false;
        }
    }
}
