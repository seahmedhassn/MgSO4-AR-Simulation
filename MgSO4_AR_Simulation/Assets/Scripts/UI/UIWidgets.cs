#if MGSO4_READY
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MgSO4AR
{
    /// <summary>Higher-level widgets of the clinical design system (radio options, keypad, donut, segmented control).</summary>
    public static class Widgets
    {
        static Sprite outline, ring;

        public static Sprite Outline
        {
            get
            {
                if (outline != null) return outline;
                int size = 96, r = 30; float w = 3.2f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                        float outer = Mathf.Clamp01(r - d + 0.5f), inner = Mathf.Clamp01(r - w - d + 0.5f);
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(outer - inner) * 255));
                    }
                tex.SetPixels32(px); tex.Apply(false, true);
                outline = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return outline;
            }
        }

        public static Sprite Ring
        {
            get
            {
                if (ring != null) return ring;
                int size = 256; float ro = 127f, ri = 103f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(128, 128));
                        float a = Mathf.Clamp01(ro - d + 0.5f) * Mathf.Clamp01(d - ri + 0.5f);
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                tex.SetPixels32(px); tex.Apply(false, true);
                ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return ring;
            }
        }

        /// <summary>Adds a thin rounded border on top of an element.</summary>
        public static Image Border(Transform parent, Color color)
        {
            var img = UIKit.NewRect("Border", parent).gameObject.AddComponent<Image>();
            img.sprite = Outline; img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
            UIKit.Stretch(img.rectTransform);
            img.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return img;
        }

        // ------------------------------------------------------------------ segmented control
        public class Segmented : MonoBehaviour
        {
            public List<Button> Buttons = new List<Button>();
            public int Index;
            public void Select(int i)
            {
                Index = i;
                for (int k = 0; k < Buttons.Count; k++)
                {
                    ((Image)Buttons[k].targetGraphic).color = k == i ? Theme.Navy : new Color(1, 1, 1, 0);
                    Buttons[k].Label().color = k == i ? Color.white : Theme.SubText;
                }
            }
        }

        public static Segmented MakeSegmented(Transform parent, string[] labels, Action<int> onSelect, float height = 84)
        {
            var bg = UIKit.MakeImage(parent, Theme.CardLight, true, "Segmented");
            UIKit.LE(bg, height, height);
            var h = UIKit.HStack(bg.rectTransform, 6, 6);
            var seg = bg.gameObject.AddComponent<Segmented>();
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = UIKit.MakeButton(bg.transform, labels[i], new Color(1, 1, 1, 0), () => { seg.Select(idx); onSelect?.Invoke(idx); }, 28, height - 12);
                seg.Buttons.Add(b);
            }
            seg.Select(0);
            return seg;
        }

        // ------------------------------------------------------------------ radio / checkbox options
        public class Option : MonoBehaviour
        {
            public Image Bg, BorderImg, Mark, MarkFill;
            public Text Label;
            public bool Multi, Selected;
            public Button Button;

            public void SetSelected(bool on)
            {
                Selected = on;
                Bg.color = on ? Theme.Tint(Theme.Primary, 0.12f) : Theme.Surface;
                BorderImg.color = on ? Theme.Primary : Theme.Border;
                Mark.color = on ? Theme.Primary : Theme.Tint(Theme.SubText, 0.5f);
                MarkFill.enabled = on;
                MarkFill.color = Multi ? Color.white : Theme.Primary;
                if (Multi) Mark.sprite = on ? UIKit.Rounded : Widgets.Outline;
            }

            /// <summary>After submitting: 1 = correct, -1 = wrong, 0 = neutral.</summary>
            public void SetResult(int r, bool wasSelected)
            {
                Button.interactable = false;
                if (r > 0)
                {
                    Bg.color = wasSelected ? Theme.SuccessBg : Theme.Tint(Theme.Success, 0.06f);
                    BorderImg.color = Theme.Success; Mark.color = Theme.Success;
                    MarkFill.enabled = true; MarkFill.sprite = UIKit.Icon("check"); MarkFill.color = Multi && wasSelected ? Color.white : Theme.Success;
                    if (Multi && wasSelected) Mark.sprite = UIKit.Rounded;
                }
                else if (r < 0)
                {
                    Bg.color = Theme.DangerBg; BorderImg.color = Theme.Danger; Mark.color = Theme.Danger;
                    MarkFill.enabled = true; MarkFill.sprite = UIKit.Icon("cross"); MarkFill.color = Multi ? Color.white : Theme.Danger;
                    if (Multi) Mark.sprite = UIKit.Rounded;
                }
                else { BorderImg.color = Theme.Border; }
            }
        }

        public static Option MakeOption(Transform parent, string text, bool multi, Action onClick)
        {
            var bg = UIKit.MakeImage(parent, Theme.Surface, true, "Option");
            var b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            var cb = b.colors; cb.pressedColor = new Color(0.92f, 0.95f, 0.97f); cb.disabledColor = Color.white; b.colors = cb;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            bg.gameObject.AddComponent<PressFx>();
            var h = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(26, 26, 22, 22); h.spacing = 22; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            UIKit.LE(bg, 96);
            var border = Border(bg.transform, Theme.Border);

            var mark = UIKit.NewRect("Mark", bg.transform).gameObject.AddComponent<Image>();
            mark.sprite = multi ? Outline : Ring; mark.type = multi ? Image.Type.Sliced : Image.Type.Simple;
            mark.raycastTarget = false;
            UIKit.LE(mark, 44, 44, -1, 44, -1, 44);
            var fill = UIKit.NewRect("Fill", mark.transform).gameObject.AddComponent<Image>();
            fill.sprite = multi ? UIKit.Icon("check") : UIKit.Circle; fill.raycastTarget = false; fill.preserveAspect = true;
            fill.rectTransform.anchorMin = new Vector2(0.24f, 0.24f); fill.rectTransform.anchorMax = new Vector2(0.76f, 0.76f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

            var label = UIKit.MakeText(bg.transform, text, 30, Theme.Text, FontStyle.Normal, TextAnchor.MiddleLeft);
            UIKit.LE(label, -1, -1, -1, -1, 1);
            var o = bg.gameObject.AddComponent<Option>();
            o.Bg = bg; o.BorderImg = border; o.Mark = mark; o.MarkFill = fill; o.Label = label; o.Multi = multi; o.Button = b;
            o.SetSelected(false);
            b.onClick.AddListener(() => { Haptics.Click(); onClick(); });
            return o;
        }

        // ------------------------------------------------------------------ numeric keypad
        public class Keypad : MonoBehaviour
        {
            public Text Display;
            public string Value = "";
            public string Unit = "";
            public void Press(string k)
            {
                if (k == "<") { if (Value.Length > 0) Value = Value.Substring(0, Value.Length - 1); }
                else if (k == ".") { if (!Value.Contains(".") && Value.Length < 7) Value += Value.Length == 0 ? "0." : "."; }
                else if (Value.Length < 7) Value = Value == "0" ? k : Value + k;
                Refresh();
            }
            public void Clear() { Value = ""; Refresh(); }
            public void Refresh()
            {
                Display.text = (Value.Length == 0 ? $"<color={Theme.Html(Theme.Tint(Theme.SubText, 0.6f))}>0</color>" : Value)
                               + $"  <size=30><color={Theme.Html(Theme.SubText)}>{Unit}</color></size>";
            }
        }

        public static Keypad MakeKeypad(Transform parent, string unit, Action<string> onSubmit, string submitLabel = "Check")
        {
            var root = UIKit.NewRect("Keypad", parent);
            UIKit.VStack(root, 12, 0);
            var dispBg = UIKit.MakeImage(root, Theme.CardLight, true, "Display");
            UIKit.LE(dispBg, 110, 110);
            var disp = UIKit.MakeText(dispBg.transform, "", 56, Theme.Text, FontStyle.Bold, TextAnchor.MiddleRight);
            UIKit.Stretch(disp.rectTransform, 30, 4, 30, 4);
            var kp = root.gameObject.AddComponent<Keypad>();
            kp.Display = disp; kp.Unit = unit;
            string[][] rows = { new[] { "7", "8", "9", "<" }, new[] { "4", "5", "6", "." }, new[] { "1", "2", "3", "0" } };
            foreach (var row in rows)
            {
                var r = UIKit.Row(root, 92, 12);
                foreach (var k in row)
                {
                    string key = k;
                    var b = UIKit.MakeButton(r, k == "<" ? "" : k, k == "<" ? Theme.Tint(Theme.Accent, 0.25f) : Theme.Surface, () => kp.Press(key), 40, 92);
                    if (k == "<")
                    {
                        var ic = UIKit.MakeIcon(b.transform, "back", Theme.AccentText, 40);
                        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    }
                    else Border(b.transform, Theme.Border);
                }
            }
            UIKit.MakeButton(root, submitLabel, Theme.Navy, () => onSubmit(kp.Value), 34, 104);
            kp.Refresh();
            return kp;
        }

        // ------------------------------------------------------------------ donut score
        public static Image Donut(Transform parent, float fraction, Color color, float size, string caption)
        {
            var holder = UIKit.NewRect("Donut", parent);
            UIKit.LE(holder, size, size, -1, size, -1, size);
            var track = UIKit.NewRect("Track", holder).gameObject.AddComponent<Image>();
            track.sprite = Ring; track.color = Theme.Border; track.raycastTarget = false;
            UIKit.Stretch(track.rectTransform);
            var fill = UIKit.NewRect("Fill", holder).gameObject.AddComponent<Image>();
            fill.sprite = Ring; fill.color = color; fill.raycastTarget = false;
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Radial360; fill.fillOrigin = (int)Image.Origin360.Top; fill.fillClockwise = true;
            fill.fillAmount = 0f;
            UIKit.Stretch(fill.rectTransform);
            var pct = UIKit.MakeText(holder, "", Mathf.RoundToInt(size * 0.24f), Theme.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Anchor(pct.rectTransform, new Vector2(0, 0.42f), new Vector2(1, 0.72f), Vector2.zero, Vector2.zero);
            var cap = UIKit.MakeText(holder, caption, Mathf.RoundToInt(size * 0.1f), Theme.SubText, FontStyle.Normal, TextAnchor.MiddleCenter);
            UIKit.Anchor(cap.rectTransform, new Vector2(0, 0.26f), new Vector2(1, 0.42f), Vector2.zero, Vector2.zero);
            holder.gameObject.AddComponent<DonutAnim>().Init(fill, pct, Mathf.Clamp01(fraction));
            return fill;
        }

        class DonutAnim : MonoBehaviour
        {
            Image fill; Text pct; float target;
            public void Init(Image f, Text p, float t) { fill = f; pct = p; target = t; StartCoroutine(Run()); }
            IEnumerator Run()
            {
                float t = 0;
                while (t < 1.2f)
                {
                    t += Time.deltaTime;
                    float k = Ease.OutCubic(t / 1.2f) * target;
                    fill.fillAmount = k; pct.text = $"{k * 100f:0}%";
                    yield return null;
                }
                fill.fillAmount = target; pct.text = $"{target * 100f:0}%";
            }
        }
    }
}
#endif
