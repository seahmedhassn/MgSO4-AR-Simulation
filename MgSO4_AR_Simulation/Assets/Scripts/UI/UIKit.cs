#if MGSO4_READY
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MgSO4AR
{
    /// <summary>Clinical light design system (teal/blue medical palette).</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("F2F6FA");          // app background
        public static readonly Color Panel = Hex("FFFFFF");       // sheets / nav bar
        public static readonly Color Card = Hex("F6F9FC");        // list rows / options
        public static readonly Color CardLight = Hex("EAF2F7");
        public static readonly Color Surface = Hex("FFFFFF");     // elevated cards
        public static readonly Color Border = Hex("DCE7EF");
        public static readonly Color Primary = Hex("0EA5A0");     // medical teal
        public static readonly Color PrimaryDark = Hex("0B7F7B");
        public static readonly Color Blue = Hex("2F80ED");
        public static readonly Color Accent = Hex("F5A524");
        public static readonly Color AccentText = Hex("B86E00");
        public static readonly Color Danger = Hex("E5484D");
        public static readonly Color Success = Hex("1FAA6B");
        public static readonly Color Info = Hex("2F80ED");
        public static readonly Color Purple = Hex("7C5CE0");
        public static readonly Color Orange = Hex("F07C2E");
        public static readonly Color Text = Hex("0E2233");
        public static readonly Color SubText = Hex("5E7184");
        public static readonly Color Neutral = Hex("E6EEF4");     // secondary buttons (dark text)
        public static readonly Color Navy = Hex("12304D");        // primary dark actions (Submit)
        public static readonly Color VitalGreen = Hex("3BE37A"), VitalRed = Hex("FF6B5B"), VitalCyan = Hex("4AD8FF"), VitalYellow = Hex("FFD24A");
        public static readonly Color Highlight = Hex("FFE14D");
        public static readonly Color Hud = new Color(0.04f, 0.10f, 0.16f, 0.80f);   // dark glass over camera
        public static readonly Color HudButton = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color SuccessBg = Hex("E3F6EC"), DangerBg = Hex("FDE9EA"), WarnBg = Hex("FFF4DE"), InfoBg = Hex("E7F0FD");

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }
        public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static float Luma(Color c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * c.a + (1f - c.a) * 0.9f;
        /// <summary>Readable text colour for a given background.</summary>
        public static Color On(Color bg) => Luma(bg) > 0.62f ? Text : Color.white;
        public static Color Tint(Color c, float k) => Color.Lerp(Color.white, c, k);
    }

    /// <summary>Builds uGUI elements from code so the whole app is data-driven and needs no prefabs.</summary>
    public static class UIKit
    {
        static Font font, fontBold, fontSemi;
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>("Fonts/Inter-Regular");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }
        public static Font FontBold
        {
            get
            {
                if (fontBold == null) fontBold = Resources.Load<Font>("Fonts/Inter-Bold");
                return fontBold != null ? fontBold : Font;
            }
        }
        public static Font FontSemi
        {
            get
            {
                if (fontSemi == null) fontSemi = Resources.Load<Font>("Fonts/Inter-SemiBold");
                return fontSemi != null ? fontSemi : Font;
            }
        }

        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        public static Sprite Icon(string name)
        {
            if (icons.TryGetValue(name, out var sp) && sp != null) return sp;
            var tex = Resources.Load<Texture2D>("Icons/" + name);
            sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            icons[name] = sp;
            return sp;
        }

        public static Image MakeIcon(Transform parent, string name, Color color, float size)
        {
            var rt = NewRect("Icon_" + name, parent);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Icon(name);
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            LE(img, size, size, -1, size, -1, size);
            return img;
        }

        /// <summary>Round tinted badge with an icon (used on tiles and headers).</summary>
        public static Image IconBadge(Transform parent, string icon, Color color, float size = 104)
        {
            var bg = MakeImage(parent, Theme.Tint(color, 0.16f), true, "Badge");
            bg.sprite = Circle; bg.type = Image.Type.Simple;
            bg.raycastTarget = false;
            LE(bg, size, size, -1, size, -1, size);
            var ic = MakeIcon(bg.transform, icon, color, size * 0.52f);
            var r = ic.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            return bg;
        }

        static Sprite circle, shadow;
        public static Sprite Circle { get { if (circle == null) circle = MakeRounded(128, 64); return circle; } }
        public static Sprite ShadowSprite
        {
            get
            {
                if (shadow != null) return shadow;
                int size = 128, r = 30, blur = 22;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float m = blur + r;
                        float cx = Mathf.Clamp(x + 0.5f, m, size - m), cy = Mathf.Clamp(y + 0.5f, m, size - m);
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - r;
                        float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / blur));
                        px[y * size + x] = new Color32(16, 42, 66, (byte)(a * 255));
                    }
                tex.SetPixels32(px); tex.Apply(false, true);
                int bdr = blur + r + 2;
                shadow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(bdr, bdr, bdr, bdr));
                return shadow;
            }
        }

        /// <summary>Adds a soft drop shadow behind an element that lives in a layout group.</summary>
        public static RectTransform WithShadow(RectTransform inner, float alpha = 0.10f, float spread = 22, float drop = 8)
        {
            var parent = inner.parent;
            int idx = inner.GetSiblingIndex();
            var wrap = NewRect("Shadowed", parent);
            wrap.SetSiblingIndex(idx);
            var v = wrap.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var sh = NewRect("Shadow", wrap).gameObject.AddComponent<Image>();
            sh.sprite = ShadowSprite; sh.type = Image.Type.Sliced; sh.color = new Color(1, 1, 1, alpha * 2.2f); sh.raycastTarget = false;
            var le = sh.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            sh.rectTransform.anchorMin = Vector2.zero; sh.rectTransform.anchorMax = Vector2.one;
            sh.rectTransform.offsetMin = new Vector2(-spread, -spread - drop); sh.rectTransform.offsetMax = new Vector2(spread, spread - drop);
            inner.SetParent(wrap, false);
            var innerLe = inner.GetComponent<LayoutElement>();
            if (innerLe != null)
            {
                var wl = wrap.gameObject.AddComponent<LayoutElement>();
                wl.minHeight = innerLe.minHeight; wl.preferredHeight = innerLe.preferredHeight; wl.flexibleWidth = innerLe.flexibleWidth;
            }
            return inner;
        }

        static Sprite rounded;
        public static Sprite Rounded { get { if (rounded == null) rounded = MakeRounded(96, 30); return rounded; } }

        static Sprite MakeRounded(int size, int r)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                    float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        // ---------- structure ----------
        public static Canvas CreateCanvas(string name, int sortOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortOrder;
            var s = go.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1080, 1920);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = Screen.width > Screen.height ? 1f : 0f;
            return c;
        }

        public static RectTransform SafeRoot(Canvas canvas)
        {
            var rt = NewRect("SafeArea", canvas.transform);
            Stretch(rt);
            rt.gameObject.AddComponent<SafeArea>();
            return rt;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax;
            return rt;
        }

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, int pad, TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = rt.gameObject.GetComponent<VerticalLayoutGroup>();
            if (v == null) v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing, int pad = 0)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return h;
        }

        public static LayoutElement LE(Component c, float minH = -1, float prefH = -1, float flexH = -1, float prefW = -1, float flexW = -1, float minW = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.minHeight = minH; le.preferredHeight = prefH; le.flexibleHeight = flexH;
            le.preferredWidth = prefW; le.flexibleWidth = flexW; le.minWidth = minW;
            return le;
        }

        public static RectTransform Row(Transform parent, float height, float spacing = 16)
        {
            var rt = NewRect("Row", parent);
            HStack(rt, spacing);
            LE(rt, height, height);
            return rt;
        }

        public static RectTransform Spacer(Transform parent, float h)
        {
            var rt = NewRect("Spacer", parent);
            LE(rt, h, h);
            return rt;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                c.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(c);
            }
        }

        // ---------- widgets ----------
        public static Image MakeImage(Transform parent, Color color, bool rounded = true, string name = "Image")
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (rounded)
            {
                img.sprite = Rounded;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }
            return img;
        }

        public static Text MakeText(Transform parent, string text, int size = 34, Color? color = null,
            FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = NewRect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = style == FontStyle.Bold || style == FontStyle.BoldAndItalic ? FontBold : Font;
            t.fontSize = size;
            t.color = color ?? Theme.Text;
            t.fontStyle = style == FontStyle.Bold ? FontStyle.Normal : style == FontStyle.BoldAndItalic ? FontStyle.Italic : style;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.lineSpacing = 1.08f;
            t.text = text;
            return t;
        }

        /// <summary>Card: rounded panel with vertical layout that grows with its content.</summary>
        public static RectTransform Card(Transform parent, Color? color = null, int pad = 36, float spacing = 18)
        {
            var c = color ?? Theme.Surface;
            var img = MakeImage(parent, c, true, "Card");
            img.raycastTarget = false;
            VStack(img.rectTransform, spacing, pad);
            if (c.a > 0.99f && Theme.Luma(c) > 0.9f) WithShadow(img.rectTransform);
            return img.rectTransform;
        }

        /// <summary>Fixed-height button (height &gt; 0) or auto-height button sized to its text (height &lt;= 0).</summary>
        public static Button MakeButton(Transform parent, string label, Color bg, Action onClick, int fontSize = 36,
            float height = 120, Color? textColor = null, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var img = MakeImage(parent, bg, true, "Button");
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = Color.white; cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            cb.fadeDuration = 0.06f;
            b.colors = cb;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            img.gameObject.AddComponent<PressFx>();

            var t = MakeText(img.transform, label, fontSize, textColor ?? Theme.On(bg), FontStyle.Bold, align);
            if (height > 0)
            {
                Stretch(t.rectTransform, 24, 6, 24, 6);
                LE(img, height, height);
            }
            else
            {
                var v = VStack(img.rectTransform, 0, 0);
                v.padding = new RectOffset(34, 34, 26, 26);
                LE(img, 104);
                t.font = Font;
                t.fontStyle = FontStyle.Normal;
            }
            return b;
        }

        public static Text Label(this Button b) => b.GetComponentInChildren<Text>(true);
        public static void SetColor(this Button b, Color c)
        {
            ((Image)b.targetGraphic).color = c;
            var t = b.Label();
            if (t != null) t.color = Theme.On(c);
        }

        /// <summary>Menu tile: white card, tinted icon badge, title, description, chevron.</summary>
        public static Button MakeTile(Transform parent, string title, string desc, Color accent, Action onClick, string icon = "info")
        {
            var img = MakeImage(parent, Theme.Surface, true, "Tile");
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            var cb = b.colors; cb.pressedColor = new Color(0.93f, 0.96f, 0.98f); b.colors = cb;
            b.onClick.AddListener(() => onClick());
            img.gameObject.AddComponent<PressFx>();
            var h = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(30, 30, 26, 26); h.spacing = 26;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            IconBadge(img.transform, icon, accent, 104);
            var col = NewRect("Text", img.transform);
            VStack(col, 4, 0);
            LE(col, -1, -1, -1, -1, 1);
            MakeText(col, title, 38, Theme.Text, FontStyle.Bold);
            MakeText(col, desc, 27, Theme.SubText);
            MakeIcon(img.transform, "chevron", Theme.Tint(Theme.SubText, 0.6f), 40);
            LE(img, 150);
            WithShadow(img.rectTransform, 0.08f, 20, 6);
            return b;
        }

        public static RectTransform Chip(Transform parent, string text, Color bg, int size = 26)
        {
            var img = MakeImage(parent, bg, true, "Chip");
            img.raycastTarget = false;
            var h = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(22, 22, 8, 8);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var t = MakeText(img.transform, text, size, Theme.On(bg), FontStyle.Bold, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return img.rectTransform;
        }

        public static RectTransform MakeScroll(Transform parent, out ScrollRect sr, float spacing = 24, int pad = 32)
        {
            var root = NewRect("Scroll", parent);
            sr = root.gameObject.AddComponent<ScrollRect>();
            var viewport = NewRect("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0);
            var content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VStack(content, spacing, pad);
            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40;
            sr.inertia = true;
            return content;
        }

        public static InputField MakeInput(Transform parent, string placeholder, InputField.ContentType type,
            float height = 110, int fontSize = 38)
        {
            var img = MakeImage(parent, Theme.CardLight, true, "Input");
            LE(img, height, height);
            var text = MakeText(img.transform, "", fontSize, Theme.Bg, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 30, 6, 30, 6);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = MakeText(img.transform, placeholder, fontSize - 4, new Color(0.45f, 0.5f, 0.56f), FontStyle.Italic, TextAnchor.MiddleLeft);
            Stretch(ph.rectTransform, 30, 6, 30, 6);
            var input = img.gameObject.AddComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = text;
            input.placeholder = ph;
            input.contentType = type;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        public static Slider MakeSlider(Transform parent, float min, float max, float value, Action<float> onChange, float height = 96)
        {
            var root = NewRect("Slider", parent);
            LE(root, height, height);
            var bg = MakeImage(root, Theme.Neutral, true, "Background");
            Anchor(bg.rectTransform, new Vector2(0, 0.38f), new Vector2(1, 0.62f), Vector2.zero, Vector2.zero);
            var fillArea = NewRect("Fill Area", root);
            Anchor(fillArea, new Vector2(0, 0.38f), new Vector2(1, 0.62f), new Vector2(10, 0), new Vector2(-10, 0));
            var fill = MakeImage(fillArea, Theme.Primary, true, "Fill");
            fill.rectTransform.anchorMin = new Vector2(0, 0);
            fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.sizeDelta = new Vector2(20, 0);
            var handleArea = NewRect("Handle Slide Area", root);
            Stretch(handleArea, 34, 0, 34, 0);
            var handle = MakeImage(handleArea, Color.white, true, "Handle");
            handle.rectTransform.anchorMin = new Vector2(0, 0.1f);
            handle.rectTransform.anchorMax = new Vector2(0, 0.9f);
            handle.rectTransform.sizeDelta = new Vector2(68, 0);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max; s.value = value;
            var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        // ---------- world-space text ----------
        const float WorldPPU = 2000f;

        public static Text WorldText(Transform parent, string text, Vector3 localPos, Vector2 worldSize, float letterHeight,
            Color color, bool billboard = false, Color? bg = null, Quaternion? rot = null, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("WorldText", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.localPosition = localPos;
            rt.localRotation = rot ?? Quaternion.identity;
            rt.sizeDelta = worldSize * WorldPPU;
            rt.localScale = Vector3.one / WorldPPU;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (bg.HasValue)
            {
                var b = MakeImage(rt, bg.Value, true, "Bg");
                Stretch(b.rectTransform);
                b.raycastTarget = false;
            }
            var t = MakeText(rt, text, Mathf.Max(8, Mathf.RoundToInt(letterHeight * WorldPPU)), color, FontStyle.Bold, align);
            Stretch(t.rectTransform, 16, 6, 16, 6);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (billboard) go.AddComponent<Billboard>();
            return t;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ---------- pointer helpers ----------
        static readonly List<RaycastResult> hits = new List<RaycastResult>();
        public static bool OverUI(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var ped = new PointerEventData(es) { position = screenPos };
            hits.Clear();
            es.RaycastAll(ped, hits);
            return hits.Count > 0;
        }
    }

    public class SafeArea : MonoBehaviour
    {
        RectTransform rt;
        Rect last;
        Vector2Int lastSize;

        void Awake() { rt = GetComponent<RectTransform>(); Apply(); }
        void Update()
        {
            if (Screen.safeArea != last || lastSize.x != Screen.width || lastSize.y != Screen.height) Apply();
        }
        void Apply()
        {
            last = Screen.safeArea;
            lastSize = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var min = last.position;
            var max = last.position + last.size;
            rt.anchorMin = new Vector2(min.x / Screen.width, min.y / Screen.height);
            rt.anchorMax = new Vector2(max.x / Screen.width, max.y / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    public static class CamRef
    {
        public static Camera Active;
        public static Camera Get() => Active != null && Active.isActiveAndEnabled ? Active : Camera.main;
    }

    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = CamRef.Get();
            if (cam == null) return;
            var dir = transform.position - cam.transform.position;
            if (dir.sqrMagnitude < 1e-8f) return;
            transform.rotation = Quaternion.LookRotation(dir, cam.transform.up);
        }
    }
}
#endif
