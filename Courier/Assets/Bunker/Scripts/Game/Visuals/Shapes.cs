using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bunker.Game
{
    /// <summary>
    /// Placeholder geometry: coloured primitive boxes and world-space labels. Everything here is
    /// meant to be swapped for real art later; the rest of the code only asks for "a box of colour X".
    /// </summary>
    public static class Shapes
    {
        static Material s_Lit;
        static Shader s_Unlit;
        static readonly Dictionary<Color, Material> s_LitCache = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Material> s_UnlitCache = new Dictionary<Color, Material>();
        static Font s_Font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_LitCache.Clear();
            s_UnlitCache.Clear();
            s_Lit = null;
            s_Unlit = null;
            s_Cube = null;
        }

        static Mesh s_Cube;

        /// <summary>The render pipeline's default material and the cube mesh, borrowed from a throwaway primitive.</summary>
        static Material BaseLit
        {
            get
            {
                if (s_Lit == null || s_Cube == null) Probe();
                return s_Lit;
            }
        }

        static Mesh Cube
        {
            get
            {
                if (s_Lit == null || s_Cube == null) Probe();
                return s_Cube;
            }
        }

        static void Probe()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s_Lit = probe.GetComponent<Renderer>().sharedMaterial;
            s_Cube = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(probe);
        }

        public static Font Font
        {
            get
            {
                if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_Font;
            }
        }

        public static Material Lit(Color c)
        {
            if (s_LitCache.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(BaseLit) { name = "Lit " + ColorUtility.ToHtmlStringRGB(c), color = c };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            s_LitCache[c] = m;
            return m;
        }

        public static Material Unlit(Color c)
        {
            if (s_UnlitCache.TryGetValue(c, out var m) && m != null) return m;
            if (s_Unlit == null) s_Unlit = Shader.Find("Universal Render Pipeline/Unlit");
            m = s_Unlit != null ? new Material(s_Unlit) : new Material(BaseLit);
            m.name = "Unlit " + ColorUtility.ToHtmlStringRGB(c);
            m.color = c;
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // double-sided, for flat line ribbons
            s_UnlitCache[c] = m;
            return m;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Color color, bool castShadows = true)
        {
            // A bare mesh + renderer: no collider (picking is done with grid maths, not physics).
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = Cube;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Lit(color);
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        public static void Recolor(GameObject go, Color color) => go.GetComponent<Renderer>().sharedMaterial = Lit(color);

        public static TextMesh Label(string name, Transform parent, Vector3 localPos, string text, Color color, float size = 0.045f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = 48;
            tm.characterSize = size;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Font.material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return tm;
        }
    }
}
