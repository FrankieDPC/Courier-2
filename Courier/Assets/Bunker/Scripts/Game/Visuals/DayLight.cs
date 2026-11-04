using UnityEngine;

namespace Bunker.Game
{
    /// <summary>
    /// Tints the sun, the ambient light and the background through the day: a warm 6 am, white midday,
    /// an orange evening and a dim blue night before the 2 am reset. The sun also swings across the sky
    /// so shadows move.
    /// </summary>
    public sealed class DayLight : MonoBehaviour
    {
        struct Key
        {
            public float T;
            public Color Sun;
            public float Intensity;
            public Color Ambient;
            public Color Sky;

            public Key(float t, string sun, float intensity, string ambient, string sky)
            {
                T = t;
                Sun = Palette.Hex(sun);
                Intensity = intensity;
                Ambient = Palette.Hex(ambient);
                Sky = Palette.Hex(sky);
            }
        }

        // Day progress 0 = 6 am, 1 = 2 am.
        static readonly Key[] s_Keys =
        {
            new Key(0.00f, "#FFC98F", 0.75f, "#7F7A78", "#2B2A26"),
            new Key(0.15f, "#FFF1DC", 1.10f, "#999F94", "#1E2A21"),
            new Key(0.60f, "#FFF1DC", 1.10f, "#999F94", "#1E2A21"),
            new Key(0.72f, "#FFAE6B", 0.85f, "#8A7A70", "#2E2420"),
            new Key(0.82f, "#8FA2D9", 0.45f, "#4A5268", "#121726"),
            new Key(1.00f, "#7F92CC", 0.35f, "#3C4458", "#0C101C"),
        };

        GameController _game;
        Light _sun;
        Camera _cam;

        public void Init(GameController game, Light sun, Camera cam)
        {
            _game = game;
            _sun = sun;
            _cam = cam;
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (_game == null || _game.World == null || _sun == null) return;
            float t = _game.World.DayProgress01;

            int i = 0;
            while (i < s_Keys.Length - 2 && t > s_Keys[i + 1].T) i++;
            var a = s_Keys[i];
            var b = s_Keys[i + 1];
            float k = Mathf.InverseLerp(a.T, b.T, t);

            _sun.color = Color.Lerp(a.Sun, b.Sun, k);
            _sun.intensity = Mathf.Lerp(a.Intensity, b.Intensity, k);
            RenderSettings.ambientLight = Color.Lerp(a.Ambient, b.Ambient, k);
            if (_cam != null) _cam.backgroundColor = Color.Lerp(a.Sky, b.Sky, k);

            // East in the morning, west in the evening.
            float yaw = Mathf.Lerp(-70f, 70f, Mathf.Clamp01(t / 0.8f));
            float pitch = Mathf.Lerp(35f, 60f, Mathf.Sin(Mathf.Clamp01(t / 0.8f) * Mathf.PI));
            _sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
