#if MGSO4_READY
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MgSO4AR
{
    public static class Ease
    {
        public static float OutCubic(float t) { t = Mathf.Clamp01(t) - 1f; return t * t * t + 1f; }
        public static float OutBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2); }
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * Mathf.Clamp01(t)) - 1f) / 2f;
    }

    /// <summary>Fade + scale "pop" entrance used for cards, list items and page transitions.</summary>
    public class AppearAnim : MonoBehaviour
    {
        public float Delay;
        public float Duration = 0.35f;
        public float FromScale = 0.94f;
        CanvasGroup cg;

        public static void Apply(Component c, float delay, float fromScale = 0.94f)
        {
            if (c == null) return;
            var a = c.GetComponent<AppearAnim>();
            if (a == null) a = c.gameObject.AddComponent<AppearAnim>();
            a.Delay = delay;
            a.FromScale = fromScale;
            if (a.isActiveAndEnabled) a.Restart();
        }

        /// <summary>Staggered entrance of all direct children.</summary>
        public static void Stagger(Transform parent, float step = 0.045f, float start = 0f)
        {
            int k = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var ch = parent.GetChild(i);
                if (!ch.gameObject.activeSelf) continue;
                Apply(ch, start + Mathf.Min(k++, 12) * step);
            }
        }

        void OnEnable() => Restart();

        public void Restart()
        {
            if (cg == null) cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            StopAllCoroutines();
            if (isActiveAndEnabled) StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            cg.alpha = 0f;
            transform.localScale = Vector3.one * FromScale;
            float t = -Delay;
            while (t < Duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Duration);
                cg.alpha = Ease.OutCubic(k);
                transform.localScale = Vector3.one * Mathf.LerpUnclamped(FromScale, 1f, Ease.OutBack(k));
                yield return null;
            }
            cg.alpha = 1f;
            transform.localScale = Vector3.one;
        }
    }

    /// <summary>Tactile press feedback: shrinks slightly while pressed.</summary>
    public class PressFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1f, current = 1f;
        public void OnPointerDown(PointerEventData e) { target = 0.955f; Haptics.Tap(); Ripple(e); }

        // Material-style touch ripple: a soft circle grows from the exact touch point, clipped to the button.
        RectTransform clip;
        void Ripple(PointerEventData e)
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            if (clip == null)
            {
                clip = new GameObject("RippleClip", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D)).GetComponent<RectTransform>();
                clip.SetParent(rt, false);
                clip.anchorMin = Vector2.zero; clip.anchorMax = Vector2.one;
                clip.offsetMin = clip.offsetMax = Vector2.zero;
                clip.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            }
            clip.SetAsLastSibling();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(clip, e.position, e.pressEventCamera, out var local)) return;
            var img = new GameObject("Ripple", typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
            img.sprite = UIKit.Circle;
            img.raycastTarget = false;
            img.rectTransform.SetParent(clip, false);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            img.rectTransform.anchoredPosition = local;
            var size = rt.rect.size;
            StartCoroutine(RippleAnim(img, Mathf.Sqrt(size.x * size.x + size.y * size.y) * 2.1f));
        }

        System.Collections.IEnumerator RippleAnim(UnityEngine.UI.Image img, float maxD)
        {
            var c = Theme.Primary;
            float t = 0f;
            while (t < 0.55f && img != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 0.55f);
                img.rectTransform.sizeDelta = Vector2.one * maxD * Ease.OutCubic(k);
                img.color = new Color(c.r, c.g, c.b, 0.22f * (1f - k * k));
                yield return null;
            }
            if (img != null) Destroy(img.gameObject);
        }
        public void OnPointerUp(PointerEventData e) => target = 1f;
        public void OnPointerExit(PointerEventData e) => target = 1f;
        void Update()
        {
            if (Mathf.Approximately(current, target)) return;
            current = Mathf.MoveTowards(current, target, Time.deltaTime * 1.2f);
            transform.localScale = Vector3.one * current;
        }
    }

    /// <summary>
    /// Heartbeat-style pulse. With MotionReactive (default) it beats faster and larger when the
    /// phone is shaken or held by an unsteady hand (MotionSense), and calms down when still.
    /// </summary>
    public class Pulse : MonoBehaviour
    {
        public float Amount = 0.03f, Speed = 2.2f;
        public bool MotionReactive = true;
        public bool Heartbeat;           // double "lub-dub" beat instead of a sine
        public bool HapticBeats;         // phone vibrates along with its own movement while this is visible
        bool counted;
        void OnEnable() { if (HapticBeats && !counted) { counted = true; MotionSense.HapticSources++; } }
        void OnDisable() { if (counted) { counted = false; MotionSense.HapticSources--; } }
        void Start() { OnEnable(); }
        public bool MotionOnly;          // no movement at all when the phone is still
        float phase;
        public static Pulse On(Component c, float amount = 0.03f, float speed = 2.2f, bool heartbeat = false)
        {
            var p = c.gameObject.AddComponent<Pulse>();
            p.Amount = amount; p.Speed = speed; p.Heartbeat = heartbeat;
            return p;
        }
        void Update()
        {
            float m = MotionReactive ? MotionSense.Intensity : 0f;
            phase += Time.deltaTime * Speed * (1f + 1.8f * m);
            float amp = MotionOnly ? Amount * m : Amount * (1f + 5f * m);
            float w;
            if (Heartbeat)
            {
                float x = Mathf.Repeat(phase * 0.5f, 1f);
                w = Mathf.Exp(-Mathf.Pow((x - 0.1f) / 0.05f, 2)) + 0.6f * Mathf.Exp(-Mathf.Pow((x - 0.28f) / 0.05f, 2));
            }
            else w = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI);
            transform.localScale = Vector3.one * (1f + amp * w);
        }
    }

    /// <summary>Vertical or horizontal two-colour gradient applied to any UI graphic.</summary>
    public class UIGradient : BaseMeshEffect
    {
        public Color A = Color.white, B = Color.black;
        public bool Horizontal;

        public static UIGradient On(Graphic g, Color a, Color b, bool horizontal = false)
        {
            var e = g.gameObject.AddComponent<UIGradient>();
            e.A = a; e.B = b; e.Horizontal = horizontal;
            g.color = Color.white;
            g.SetVerticesDirty();
            return e;
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var rect = ((RectTransform)transform).rect;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = Horizontal ? Mathf.InverseLerp(rect.xMin, rect.xMax, v.position.x)
                                     : Mathf.InverseLerp(rect.yMax, rect.yMin, v.position.y);
                var orig = (Color)v.color;
                var c = Color.Lerp(A, B, t);
                c.a *= orig.a;
                v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>Animated ECG trace (scrolling heartbeat line) drawn as a UI mesh.</summary>
    public class EcgGraphic : MaskableGraphic
    {
        public float Bpm = 72f;
        public float Thickness = 4f;
        public float Speed = 0.35f;          // screen widths per second
        public bool Flat;                    // asystole
        public bool MotionReactive;          // decorative traces react to phone movement
        public bool MotionOnly;              // straight line when the phone is still
        const int N = 160;
        float phase, motionGain = 1f;

        static float Beat(float x)
        {
            // one cardiac cycle on 0..1: P, QRS, T
            float y = 0f;
            y += 0.12f * Mathf.Exp(-Mathf.Pow((x - 0.18f) / 0.035f, 2));
            y -= 0.12f * Mathf.Exp(-Mathf.Pow((x - 0.30f) / 0.012f, 2));
            y += 1.00f * Mathf.Exp(-Mathf.Pow((x - 0.33f) / 0.012f, 2));
            y -= 0.25f * Mathf.Exp(-Mathf.Pow((x - 0.36f) / 0.012f, 2));
            y += 0.22f * Mathf.Exp(-Mathf.Pow((x - 0.58f) / 0.05f, 2));
            return y;
        }

        void Update()
        {
            float m = MotionReactive ? MotionSense.Intensity : 0f;
            motionGain = MotionOnly ? Mathf.Clamp01(m * 1.4f) : 1f + 0.9f * m;
            phase += Time.deltaTime * Speed * (1f + 1.2f * m);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float beatsAcross = Mathf.Max(1.5f, Bpm / 60f * (1f / Mathf.Max(0.05f, Speed)));
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= N; i++)
            {
                float u = (float)i / N;
                float bx = Mathf.Repeat(u * beatsAcross - phase * beatsAcross, 1f);
                float y = Flat ? 0f : Beat(bx) * motionGain;
                var p = new Vector2(r.xMin + u * r.width, r.yMin + r.height * (0.32f + 0.55f * y));
                if (i > 0)
                {
                    // fade the trailing part (sweep look)
                    float a = Mathf.Lerp(0.15f, 1f, u);
                    AddSegment(vh, prev, p, a);
                }
                prev = p;
            }
        }

        void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, float alpha)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * Thickness * 0.5f;
            var c = color; c.a *= alpha;
            int i = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero);
            vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero);
            vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    /// <summary>Counts a number up for result screens.</summary>
    public class CountUp : MonoBehaviour
    {
        public static void On(Text t, float to, string format, float duration = 1.1f)
        {
            var c = t.gameObject.AddComponent<CountUp>();
            c.StartCoroutine(c.Run(t, to, format, duration));
        }
        IEnumerator Run(Text t, float to, string format, float duration)
        {
            float tt = 0;
            while (tt < duration)
            {
                tt += Time.deltaTime;
                t.text = string.Format(format, Mathf.Lerp(0, to, Ease.OutCubic(tt / duration)));
                yield return null;
            }
            t.text = string.Format(format, to);
        }
    }

    /// <summary>Animated progress bar (fill grows to the target).</summary>
    public class ProgressBar : MonoBehaviour
    {
        RectTransform fill;
        float target, current;

        public static ProgressBar Make(Transform parent, float value, Color fillColor, float height = 12)
        {
            var bg = UIKit.MakeImage(parent, Theme.Border, true, "Progress");
            UIKit.LE(bg, height, height);
            var f = UIKit.MakeImage(bg.transform, fillColor, true, "Fill");
            f.rectTransform.anchorMin = Vector2.zero;
            f.rectTransform.anchorMax = new Vector2(0, 1);
            f.rectTransform.offsetMin = f.rectTransform.offsetMax = Vector2.zero;
            var pb = bg.gameObject.AddComponent<ProgressBar>();
            pb.fill = f.rectTransform;
            pb.target = Mathf.Clamp01(value);
            return pb;
        }

        public void Set(float v) => target = Mathf.Clamp01(v);

        void Update()
        {
            current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            fill.anchorMax = new Vector2(current, 1);
        }
    }

    /// <summary>Animates a RectTransform's anchors (used for the AR bottom sheet).</summary>
    public class AnchorTween : MonoBehaviour
    {
        Vector2 fromMin, fromMax, toMin, toMax;
        float t = 1f, dur = 0.3f;
        RectTransform rt;

        public static void To(RectTransform r, Vector2 min, Vector2 max, float duration = 0.3f)
        {
            var a = r.GetComponent<AnchorTween>();
            if (a == null) a = r.gameObject.AddComponent<AnchorTween>();
            a.rt = r;
            a.fromMin = r.anchorMin; a.fromMax = r.anchorMax;
            a.toMin = min; a.toMax = max;
            a.dur = duration; a.t = 0f;
        }

        void Update()
        {
            if (t >= 1f || rt == null) return;
            t = Mathf.Min(1f, t + Time.deltaTime / dur);
            float k = Ease.OutCubic(t);
            rt.anchorMin = Vector2.LerpUnclamped(fromMin, toMin, k);
            rt.anchorMax = Vector2.LerpUnclamped(fromMax, toMax, k);
        }
    }

    /// <summary>Animated splash: gradient, beating heart, ECG sweep and title, then fades out.</summary>
    public class Splash : MonoBehaviour
    {
        static bool shown;

        public static void ShowOnce(Canvas canvas)
        {
            if (shown) return;
            shown = true;
            var root = UIKit.MakeImage(canvas.transform, Color.white, false, "Splash");
            UIKit.Stretch(root.rectTransform);
            UIGradient.On(root, Theme.Primary, Theme.Blue);
            root.gameObject.AddComponent<Splash>().Build(root.rectTransform);
        }

        void Build(RectTransform root)
        {
            var cg = gameObject.AddComponent<CanvasGroup>();
            var heartBg = UIKit.MakeImage(root, new Color(1, 1, 1, 0.16f), true, "Halo");
            var hr = heartBg.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.62f);
            hr.sizeDelta = new Vector2(300, 300);
            var heart = UIKit.MakeIcon(hr, "heart", Color.white, 190);
            var hp = Pulse.On(heart, 0.16f, 2.0f, true);
            hp.HapticBeats = true; hp.MotionOnly = true;
            var hb = Pulse.On(heartBg, 0.10f, 2.0f, true);
            hb.MotionOnly = true;

            var ecgGo = UIKit.NewRect("ECG", root);
            ecgGo.anchorMin = new Vector2(0, 0.42f); ecgGo.anchorMax = new Vector2(1, 0.50f);
            ecgGo.offsetMin = ecgGo.offsetMax = Vector2.zero;
            var ecg = ecgGo.gameObject.AddComponent<EcgGraphic>();
            ecg.color = new Color(1, 1, 1, 0.9f); ecg.Thickness = 6; ecg.Bpm = 90; ecg.raycastTarget = false; ecg.MotionReactive = true; ecg.MotionOnly = true;

            var title = UIKit.MakeText(root, "MgSO4 AR Sim", 78, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Anchor(title.rectTransform, new Vector2(0, 0.3f), new Vector2(1, 0.38f), Vector2.zero, Vector2.zero);
            var sub = UIKit.MakeText(root, "NURSING EDUCATION AR SIMULATOR", 30, new Color(1, 1, 1, 0.88f), FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Anchor(sub.rectTransform, new Vector2(0, 0.25f), new Vector2(1, 0.3f), Vector2.zero, Vector2.zero);
            var foot = UIKit.NewRect("Footer", root);
            UIKit.Anchor(foot, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.13f), Vector2.zero, Vector2.zero);
            var fh = foot.gameObject.AddComponent<HorizontalLayoutGroup>();
            fh.spacing = 22; fh.childAlignment = TextAnchor.MiddleCenter; fh.childControlWidth = fh.childControlHeight = true; fh.childForceExpandWidth = fh.childForceExpandHeight = false;
            var seal = UIKit.MakeImage(foot, new Color(1, 1, 1, 0.18f), true, "Seal");
            seal.sprite = UIKit.Circle; seal.type = Image.Type.Simple;
            UIKit.LE(seal, 96, 96, -1, 96, -1, 96);
            var sic = UIKit.MakeIcon(seal.transform, "book", Color.white, 52);
            sic.rectTransform.anchorMin = sic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var fac = UIKit.MakeText(foot, "<b>Mansoura University</b>\nFaculty of Nursing", 27, new Color(1, 1, 1, 0.9f), FontStyle.Normal, TextAnchor.MiddleLeft);
            AppearAnim.Apply(foot, 0.6f, 0.9f);
            AppearAnim.Apply(title, 0.25f, 0.85f);
            AppearAnim.Apply(sub, 0.45f, 0.9f);
            AppearAnim.Apply(heartBg, 0f, 0.6f);
            StartCoroutine(Run(cg));
        }

        IEnumerator Run(CanvasGroup cg)
        {
            yield return new WaitForSeconds(0.35f);
            Haptics.Heartbeat(0.25f);
            yield return new WaitForSeconds(1.55f);
            float t = 0;
            while (t < 0.45f) { t += Time.deltaTime; cg.alpha = 1f - Ease.OutCubic(t / 0.45f); yield return null; }
            Destroy(gameObject);
        }
    }
}
namespace MgSO4AR
{
    public enum SwipeDir { None, Left, Right, Up, Down }

    /// <summary>
    /// Horizontal page transition (like Android/iOS navigation): the new page slides in from the right
    /// when going forward and from the left when going back, with a short fade. Also supports a
    /// finger-following drag offset for interactive "swipe back".
    /// </summary>
    public class PageSlide : MonoBehaviour
    {
        RectTransform rt; CanvasGroup cg;
        float from, to, t, dur = 0.3f, a0 = 1f, a1 = 1f;
        bool running;
        System.Action done;

        static PageSlide Get(RectTransform r)
        {
            var p = r.GetComponent<PageSlide>();
            if (p == null) { p = r.gameObject.AddComponent<PageSlide>(); p.rt = r; p.cg = r.GetComponent<CanvasGroup>() ?? r.gameObject.AddComponent<CanvasGroup>(); }
            return p;
        }
        float Width => rt.parent is RectTransform pr && pr.rect.width > 1 ? pr.rect.width : Screen.width;

        /// <summary>dir = +1 forward (enters from the right), -1 back (enters from the left).</summary>
        public static void In(RectTransform r, int dir, float startOffset = float.NaN)
        {
            var p = Get(r);
            p.from = float.IsNaN(startOffset) ? dir * p.Width * 0.45f : startOffset;
            p.to = 0; p.a0 = 0.15f; p.a1 = 1f; p.dur = 0.32f; p.done = null;
            p.Begin();
        }

        /// <summary>Slides the current page out (dir +1 = to the right), then calls onDone.</summary>
        public static void Out(RectTransform r, int dir, System.Action onDone)
        {
            var p = Get(r);
            p.from = r.anchoredPosition.x; p.to = dir * p.Width; p.a0 = p.cg.alpha; p.a1 = 0.2f; p.dur = 0.18f; p.done = onDone;
            p.Begin();
        }

        /// <summary>Springs back to rest after an aborted drag.</summary>
        public static void Settle(RectTransform r)
        {
            var p = Get(r);
            p.from = r.anchoredPosition.x; p.to = 0; p.a0 = p.cg.alpha; p.a1 = 1f; p.dur = 0.22f; p.done = null;
            p.Begin();
        }

        /// <summary>Follow the finger while dragging (offset in canvas units).</summary>
        public static void Drag(RectTransform r, float offset)
        {
            var p = Get(r);
            p.running = false;
            Set(p, offset, Mathf.Lerp(1f, 0.55f, Mathf.Abs(offset) / Mathf.Max(1f, p.Width)));
        }

        public static float WidthOf(RectTransform r) => Get(r).Width;

        static void Set(PageSlide p, float x, float a)
        {
            var ap = p.rt.anchoredPosition; ap.x = x; p.rt.anchoredPosition = ap;
            p.cg.alpha = a;
        }

        void Begin() { t = 0; running = true; Set(this, from, a0); }

        void LateUpdate()
        {
            if (!running) return;
            t += Time.deltaTime / dur;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);     // ease-out cubic
            Set(this, Mathf.Lerp(from, to, k), Mathf.Lerp(a0, a1, k));
            if (t >= 1f)
            {
                running = false;
                var d = done; done = null;
                d?.Invoke();
            }
        }
    }

    /// <summary>
    /// Follows a horizontal drag of the primary finger; reports the live offset and whether the
    /// gesture should commit (dragged far or flicked fast) when released.
    /// </summary>
    public class DragFollow
    {
        Vector2 start, last; float t0, lastT, vel; bool down, horizontal, decided;
        public bool Active => down && horizontal;
        public float OffsetPx { get; private set; }

        /// <summary>Returns +1/-1 on release when the drag commits right/left, 0 otherwise. aborted = released without commit.</summary>
        public int Update(out bool aborted)
        {
            aborted = false;
            if (PointerInput.TouchCount >= 2) { if (Active) aborted = true; down = false; return 0; }
            if (PointerInput.Down(out var p)) { start = last = p; t0 = lastT = Time.time; down = true; horizontal = false; decided = false; vel = 0; OffsetPx = 0; }
            if (down && PointerInput.Held(out var h))
            {
                var d = h - start;
                float thr = Screen.dpi > 0 ? Screen.dpi * 0.12f : 18f;
                if (!decided && d.magnitude > thr) { decided = true; horizontal = Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.4f; }
                float dt = Time.time - lastT;
                if (dt > 0) vel = Mathf.Lerp(vel, (h.x - last.x) / dt, 0.5f);
                last = h; lastT = Time.time;
                if (horizontal) OffsetPx = d.x;
                return 0;
            }
            if (down && PointerInput.Up(out _))
            {
                down = false;
                if (!horizontal) return 0;
                float dist = OffsetPx;
                bool commit = Mathf.Abs(dist) > Screen.width * 0.28f || (Mathf.Abs(vel) > Screen.width * 1.1f && Mathf.Abs(dist) > Screen.width * 0.06f);
                if (commit) return dist > 0 ? 1 : -1;
                aborted = true;
            }
            return 0;
        }
    }

    /// <summary>Detects quick swipe gestures (screen-space) from the primary pointer.</summary>
    public class SwipeTracker
    {
        Vector2 start; float t0; bool tracking;
        public Vector2 StartPos => start;

        /// <summary>Call every frame. Returns a swipe direction on the frame the finger lifts.</summary>
        public SwipeDir Update()
        {
            if (PointerInput.TouchCount >= 2) { tracking = false; return SwipeDir.None; }
            if (PointerInput.Down(out var p)) { start = p; t0 = Time.time; tracking = true; }
            if (tracking && PointerInput.Up(out var e))
            {
                tracking = false;
                var d = e - start;
                float dt = Time.time - t0;
                float min = Screen.dpi > 0 ? Screen.dpi * 0.45f : Screen.width * 0.12f;
                if (dt > 0.6f || d.magnitude < min) return SwipeDir.None;
                if (Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.6f) return d.x > 0 ? SwipeDir.Right : SwipeDir.Left;
                if (Mathf.Abs(d.y) > Mathf.Abs(d.x) * 1.6f) return d.y > 0 ? SwipeDir.Up : SwipeDir.Down;
            }
            return SwipeDir.None;
        }
    }
}
#endif
