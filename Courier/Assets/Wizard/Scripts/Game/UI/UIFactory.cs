using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wizard.Game
{
    /// <summary>Tiny helpers for building uGUI from code (no prefabs, no TMP import needed).</summary>
    public static class UIFactory
    {
        static Sprite s_Rounded;

        /// <summary>A procedurally generated 9-sliced rounded rectangle.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (s_Rounded == null) s_Rounded = MakeRoundedSprite(32, 10);
                return s_Rounded;
            }
        }

        static Sprite MakeRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Rounded",
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 }; // UI layer
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color, bool rounded = true, float cornerScale = 1f)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (rounded)
            {
                img.sprite = Rounded;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.1f, cornerScale);
            }
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Shapes.Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Color bg, Color fg, int fontSize,
            UnityAction onClick, out Text text)
        {
            var img = Panel(name, parent, bg, true, 0.8f);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.93f, 0.93f, 0.93f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            btn.colors = colors;
            btn.onClick.AddListener(() =>
            {
                // Drop focus so Space (start/pause) doesn't re-click the last button.
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                onClick();
            });

            text = Label("Text", img.transform, label, fontSize, fg, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 8, 8, 2, 2);
            return btn;
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static LayoutElement Size(Component c, float height = -1, float width = -1, float flexWidth = -1, float flexHeight = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.preferredWidth = width;
            if (width >= 0) le.minWidth = width;
            le.flexibleWidth = flexWidth;
            le.flexibleHeight = flexHeight;
            return le;
        }

        public static VerticalLayoutGroup VStack(Component c, float spacing, int padding = 0)
        {
            var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HStack(Component c, float spacing, int padding = 0)
        {
            var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = true;
            g.childAlignment = TextAnchor.MiddleLeft;
            return g;
        }

        public static void FitHeight(Component c)
        {
            var f = c.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        public static void DestroyChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                child.SetActive(false); // drop out of layout this frame
                Object.Destroy(child);
            }
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static string Colour(string s, Color c) => $"<color={Hex(c)}>{s}</color>";

        /// <summary>A single-line text field (legacy uGUI InputField, works with the Input System UI module).</summary>
        public static InputField TextField(string name, Transform parent, string text, int fontSize, int charLimit,
            string placeholder, UnityAction<string> onEndEdit)
        {
            var bg = Panel(name, parent, Color.white, true, 0.6f);
            var field = bg.gameObject.AddComponent<InputField>();

            var t = Label("Text", bg.transform, "", fontSize, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(t.rectTransform, 8, 8, 2, 2);

            var ph = Label("Placeholder", bg.transform, placeholder, fontSize, Palette.InkSoft, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 8, 8, 2, 2);

            field.targetGraphic = bg;
            field.textComponent = t;
            field.placeholder = ph;
            field.characterLimit = charLimit;
            field.lineType = InputField.LineType.SingleLine;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.text = text;
            field.onEndEdit.AddListener(onEndEdit);
            return field;
        }

        /// <summary>True while a text field has keyboard focus, so world hotkeys should stand down.</summary>
        public static bool IsTyping()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var go = es.currentSelectedGameObject;
            if (go == null) return false;
            var field = go.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        public static GridLayoutGroup Grid(Component c, Vector2 cellSize, Vector2 spacing, int columns)
        {
            var g = c.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cellSize;
            g.spacing = spacing;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            return g;
        }
    }
}
