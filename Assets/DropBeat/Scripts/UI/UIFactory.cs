using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>
    /// "Neon glass" UI kit built in code: generated sprites (glass card with a lit edge, pills, circles,
    /// soft glow), gradient text/fills, pill buttons with press animation, and layout helpers.
    /// </summary>
    public static class UIFactory
    {
        public static readonly Color TextColor = new Color(0.94f, 0.96f, 1f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.64f, 0.8f, 1f);
        public static readonly Color Faint = new Color(0.62f, 0.64f, 0.8f, 0.55f);
        public static readonly Color Cyan = new Color(0.2f, 0.95f, 1f, 1f);
        public static readonly Color Pink = new Color(1f, 0.35f, 0.85f, 1f);
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f, 1f);
        public static readonly Color Green = new Color(0.4f, 1f, 0.55f, 1f);
        public static readonly Color Red = new Color(1f, 0.3f, 0.38f, 1f);
        public static readonly Color Violet = new Color(0.55f, 0.4f, 1f, 1f);

        /// <summary>Tint for glass cards: dark violet fill, softly lit lavender edge.</summary>
        public static readonly Color GlassTint = new Color(0.45f, 0.38f, 0.95f, 0.92f);
        public static readonly Color GlassTintSelected = new Color(0.3f, 0.85f, 1f, 1f);
        public static readonly Color ButtonTint = new Color(0.5f, 0.42f, 1f, 0.95f);

        // Legacy names kept for older call sites.
        public static readonly Color PanelColor = GlassTint;
        public static readonly Color ButtonColor = ButtonTint;
        public static readonly Color ButtonHot = Cyan;

        private static Font _font;
        private static Sprite _glass, _pill, _pillOutline, _circle, _ringOutline, _glow, _rounded;

        /// <summary>Uses Resources/Fonts/DropBeatFont if present (drop any TTF/OTF there), else Unity's built-in font.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.Load<Font>("Fonts/DropBeatFont");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Sprite Glass => _glass != null ? _glass : (_glass = MakeGlass());
        public static Sprite Pill => _pill != null ? _pill : (_pill = MakeRounded(128, 64, 0f, 1f, 1f));
        public static Sprite PillOutline => _pillOutline != null ? _pillOutline : (_pillOutline = MakeRounded(128, 64, 3.5f, 0.16f, 1f));
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeRounded(128, 64, 0f, 1f, 1f, false));
        public static Sprite RingOutline => _ringOutline != null ? _ringOutline : (_ringOutline = MakeRounded(256, 128, 7f, 0f, 1f, false));
        public static Sprite GlowSprite => _glow != null ? _glow : (_glow = MakeGlow(128));
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(64, 14, 0f, 1f, 1f));

        // ------------------------------------------------------------------ sprites

        /// <summary>Rounded rect: fill alpha/luminance inside, a brighter border band of <paramref name="border"/> px.</summary>
        private static Sprite MakeRounded(int size, int radius, float border, float fillAlpha, float borderAlpha, bool sliced = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x, y, size, radius);
                    float shape = Mathf.Clamp01(0.5f - d);
                    float a;
                    if (border > 0f)
                    {
                        float edge = Mathf.Clamp01(border + 0.5f + d);
                        a = shape * Mathf.Lerp(fillAlpha, borderAlpha, edge);
                    }
                    else
                    {
                        a = shape * fillAlpha;
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = sliced ? radius : 0;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        /// <summary>Glass card: dark translucent body with a faint top sheen and a 2 px lit edge (tint with Image.color).</summary>
        private static Sprite MakeGlass()
        {
            const int size = 96, radius = 26;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x, y, size, radius);
                    float shape = Mathf.Clamp01(0.5f - d);
                    float edge = Mathf.Clamp01(2.5f + d);           // 1 on the outer 2 px
                    float sheen = Mathf.Lerp(0.2f, 0.32f, y / (float)size);
                    float lum = Mathf.Lerp(sheen, 1f, edge * 0.85f);
                    float a = shape * Mathf.Lerp(0.8f, 1f, edge);
                    byte l = (byte)(lum * 255f);
                    px[y * size + x] = new Color32(l, l, l, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private static Sprite MakeGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Signed distance (pixels, negative inside) to a rounded square.</summary>
        private static float RoundedDistance(int x, int y, int size, int radius)
        {
            float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
            float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            float dx = x + 0.5f - cx;
            float dy = y + 0.5f - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - radius;
        }

        // ------------------------------------------------------------------ elements

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

        /// <summary>Rect anchored at a single point (anchor = pivot).</summary>
        public static RectTransform At(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            return Rect(parent, name, anchor, anchor, anchor, pos, size);
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            return Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        public static Image Image(RectTransform rt, Color color, Sprite sprite = null, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            }
            img.raycastTarget = raycast;
            if (sprite == Pill || sprite == PillOutline) rt.gameObject.AddComponent<PillShape>();
            return img;
        }

        /// <summary>Glass card filling <paramref name="rt"/>.</summary>
        public static Image Card(RectTransform rt, Color? tint = null)
        {
            return Image(rt, tint ?? GlassTint, Glass);
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            return Label(Stretch(parent, name), text, size, color, align, style);
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
            t.supportRichText = true;
            return t;
        }

        /// <summary>Small caps-style label: upper case with letter spacing.</summary>
        public static string Spaced(string s, int spaces = 1)
        {
            var sb = new System.Text.StringBuilder(s.Length * (spaces + 1));
            string pad = new string(' ', spaces);
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(char.ToUpperInvariant(s[i]));
                if (i < s.Length - 1) sb.Append(s[i] == ' ' ? pad + pad : pad);
            }
            return sb.ToString();
        }

        public static UIGradient Gradient(Graphic g, Color a, Color b, bool horizontal)
        {
            var grad = g.gameObject.AddComponent<UIGradient>();
            grad.Set(a, b, horizontal);
            return grad;
        }

        public static void Glow(Graphic g, Color color, float distance = 2f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(distance, -distance);
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.55f);
            s.effectDistance = new Vector2(0f, -4f);
        }

        /// <summary>Soft radial glow behind an element (child stretched beyond its parent).</summary>
        public static Image Halo(RectTransform parent, Color color, float grow = 1.6f)
        {
            var rt = Rect(parent, "Halo", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.SetAsFirstSibling();
            var fit = rt.gameObject.AddComponent<HaloFit>();
            fit.grow = grow;
            return Image(rt, color, GlowSprite);
        }

        public enum ButtonStyle { Primary, Glass, Outline }

        /// <summary>Pill button with a press animation. Primary = cyan-to-magenta gradient, Glass = dark glass, Outline = lit outline.</summary>
        public static Button Button(Transform parent, string name, string label, int fontSize, UnityAction onClick, out Text text, ButtonStyle style = ButtonStyle.Glass)
        {
            var rt = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 80));
            Sprite sprite = style == ButtonStyle.Outline ? PillOutline : Pill;
            Color tint = style == ButtonStyle.Primary ? Color.white : style == ButtonStyle.Outline ? ButtonTint : new Color(0.16f, 0.11f, 0.32f, 0.92f);
            var img = Image(rt, tint, sprite, true);
            if (style == ButtonStyle.Primary) Gradient(img, new Color(0.05f, 0.85f, 1f), new Color(0.95f, 0.25f, 0.85f), true);

            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.2f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.95f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            cb.colorMultiplier = 1.2f;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            rt.gameObject.AddComponent<PressFX>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            text = Label(rt, "Label", label, fontSize, TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (style == ButtonStyle.Primary)
            {
                var sh = text.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0.1f, 0f, 0.25f, 0.45f);
                sh.effectDistance = new Vector2(0f, -3f);
            }
            return btn;
        }

        /// <summary>Glass pill button tinted with <paramref name="baseColor"/>.</summary>
        public static Button Button(Transform parent, string name, string label, int fontSize, UnityAction onClick, out Text text, Color baseColor)
        {
            var b = Button(parent, name, label, fontSize, onClick, out text, ButtonStyle.Glass);
            TintButton(b, baseColor);
            return b;
        }

        /// <summary>Recolours a pill button's background (for selected states).</summary>
        public static void TintButton(Button b, Color c)
        {
            var g = b.targetGraphic;
            if (g != null) g.color = c;
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

    /// <summary>Two-colour gradient across a Graphic's vertices (works on Text, Image, anything).</summary>
    [DisallowMultipleComponent]
    public sealed class UIGradient : BaseMeshEffect
    {
        [SerializeField] private Color a = Color.white;
        [SerializeField] private Color b = Color.white;
        [SerializeField] private bool horizontal = true;

        public void Set(Color from, Color to, bool isHorizontal)
        {
            a = from;
            b = to;
            horizontal = isHorizontal;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float c = horizontal ? v.position.x : v.position.y;
                min = Mathf.Min(min, c);
                max = Mathf.Max(max, c);
            }
            float range = Mathf.Max(1e-3f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = ((horizontal ? v.position.x : v.position.y) - min) / range;
                if (!horizontal) t = 1f - t; // a = top, b = bottom
                Color g = Color.Lerp(a, b, t);
                Color baseColor = v.color;
                v.color = baseColor * g;
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>Keeps pill sprites perfectly round at any height (corner radius = height / 2).</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public sealed class PillShape : UIBehaviour
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            Apply();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        private void Apply()
        {
            var img = GetComponent<Image>();
            var rt = (RectTransform)transform;
            if (img == null || img.sprite == null) return;
            float h = Mathf.Max(8f, Mathf.Min(rt.rect.height, rt.rect.width));
            img.pixelsPerUnitMultiplier = img.sprite.border.y * 2f / h;
        }
    }

    /// <summary>Scales a button down while pressed and slightly up on hover.</summary>
    public sealed class PressFX : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private float _target = 1f;
        private bool _down;
        private bool _over;

        public void OnPointerDown(PointerEventData e) { _down = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); }
        public void OnPointerEnter(PointerEventData e) { _over = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; Refresh(); }

        private void Refresh() => _target = _down ? 0.94f : _over ? 1.03f : 1f;

        private void OnDisable()
        {
            _down = _over = false;
            _target = 1f;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            float s = Mathf.Lerp(transform.localScale.x, _target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 22f));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Reports a horizontal swipe across this element: -1 = swiped right (previous), +1 = swiped left (next).</summary>
    public sealed class SwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public System.Action<int> onSwipe;
        private Vector2 _start;

        public void OnBeginDrag(PointerEventData e) => _start = e.position;
        public void OnDrag(PointerEventData e) { }

        public void OnEndDrag(PointerEventData e)
        {
            Vector2 d = e.position - _start;
            if (Mathf.Abs(d.x) > Screen.width * 0.08f && Mathf.Abs(d.x) > Mathf.Abs(d.y)) onSwipe?.Invoke(d.x < 0f ? 1 : -1);
        }
    }

    /// <summary>Sizes a halo image to its parent times <see cref="grow"/>.</summary>
    [ExecuteAlways]
    public sealed class HaloFit : MonoBehaviour
    {
        public float grow = 1.6f;

        private void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            var rt = (RectTransform)transform;
            if (parent == null) return;
            Vector2 size = parent.rect.size * grow;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
        }
    }
}
