using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>Tiny helpers for building the uGUI interface in code.</summary>
    public static class UIFactory
    {
        public static readonly Color PanelColor = new Color(0.06f, 0.03f, 0.14f, 0.82f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.09f, 0.32f, 0.95f);
        public static readonly Color ButtonHot = new Color(0.0f, 0.75f, 0.9f, 1f);
        public static readonly Color TextColor = new Color(0.92f, 0.95f, 1f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.66f, 0.82f, 1f);
        public static readonly Color Cyan = new Color(0.2f, 0.95f, 1f, 1f);
        public static readonly Color Pink = new Color(1f, 0.35f, 0.85f, 1f);
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f, 1f);
        public static readonly Color Green = new Color(0.4f, 1f, 0.5f, 1f);
        public static readonly Color Red = new Color(1f, 0.3f, 0.35f, 1f);

        private static Font _font;
        private static Sprite _rounded;
        private static Sprite _circle;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>9-sliced rounded rectangle generated at runtime.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded == null) _rounded = MakeRounded(64, 18);
                return _rounded;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle == null) _circle = MakeRounded(128, 64, false);
                return _circle;
            }
        }

        private static Sprite MakeRounded(int size, int radius, bool sliced = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = sliced ? radius + 2 : 0;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return rt;
        }

        public static UnityEngine.UI.Image Panel(Transform parent, string name, Color color, bool rounded = true)
        {
            var rt = Stretch(parent, name);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            if (rounded)
            {
                img.sprite = Rounded;
                img.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return img;
        }

        public static UnityEngine.UI.Image Image(RectTransform rt, Color color, Sprite sprite = null)
        {
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite == Rounded ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            }
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            var rt = Stretch(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Text Label(RectTransform rt, string text, int size, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static void Glow(Graphic g, Color color, float distance = 2f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(distance, -distance);
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.6f);
            s.effectDistance = new Vector2(0f, -4f);
        }

        public static Button Button(Transform parent, string name, string label, int fontSize, UnityAction onClick, out Text text, Color? baseColor = null)
        {
            var rt = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 80));
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = Rounded;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = Color.white;
            var btn = rt.gameObject.AddComponent<Button>();
            var cb = btn.colors;
            Color c = baseColor ?? ButtonColor;
            cb.normalColor = c;
            cb.highlightedColor = Color.Lerp(c, ButtonHot, 0.45f);
            cb.pressedColor = Color.Lerp(c, ButtonHot, 0.8f);
            cb.selectedColor = cb.highlightedColor;
            cb.disabledColor = new Color(c.r, c.g, c.b, 0.35f);
            cb.fadeDuration = 0.06f;
            btn.colors = cb;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            text = Label(rt, "Label", label, fontSize, TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            return btn;
        }

        public static LayoutElement Layout(Component c, float preferredHeight, float preferredWidth = -1f)
        {
            if (!c.gameObject.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            le.minHeight = preferredHeight;
            if (preferredWidth > 0f)
            {
                le.preferredWidth = preferredWidth;
                le.minWidth = preferredWidth;
            }
            return le;
        }

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, int padding, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = align;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            return v;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing, int padding = 0)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = true;
            return h;
        }

        public static string Hex(Color c)
        {
            return ColorUtility.ToHtmlStringRGB(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b)));
        }
    }
}
