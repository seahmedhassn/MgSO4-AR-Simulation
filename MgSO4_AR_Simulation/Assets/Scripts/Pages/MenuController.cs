#if MGSO4_READY
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MgSO4AR
{
    /// <summary>Main menu and all non-AR learning pages (learn, dose practice, knowledge test, results, help).</summary>
    public class MenuController : MonoBehaviour
    {
        Canvas canvas;
        RectTransform safe, header, body;     // body/scroll = the page currently being built / shown
        ScrollRect scroll;

        /// <summary>One full-screen page layer (iOS-style navigation keeps two: shown + incoming/underneath).</summary>
        class PageView { public RectTransform root, content; public ScrollRect scroll; public Image shade; }
        PageView viewA, viewB, shown;
        PageView Spare => shown == viewA ? viewB : viewA;
        bool animating;
        float pageW => safe.rect.width > 1 ? safe.rect.width : 1080f;
        Text headerTitle;
        Button backBtn;
        readonly Stack<Action> history = new Stack<Action>();
        readonly Stack<Action> forward = new Stack<Action>();   // pages left with "back", reachable by swiping left
        Action current;
        readonly DragFollow drag = new DragFollow();
        bool dragging;
        Image homeLogo;
        GameObject toastGo;

        void Toast(string msg)
        {
            if (toastGo != null) Destroy(toastGo);
            var t = UIKit.MakeImage(canvas.transform, new Color(0.05f, 0.09f, 0.14f, 0.94f), true, "Toast");
            UIKit.Anchor(t.rectTransform, new Vector2(0.1f, 0.06f), new Vector2(0.9f, 0.06f), new Vector2(0, 0), new Vector2(0, 100));
            var tx = UIKit.MakeText(t.transform, msg, 28, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Stretch(tx.rectTransform, 20, 6, 20, 6);
            AppearAnim.Apply(t, 0f, 0.9f);
            toastGo = t.gameObject;
            Destroy(toastGo, 2.2f);
        }

        /// <summary>Square grid tile: icon in a rounded tinted frame, label below. The selection
        /// (tint, coloured border, check badge) moves to the tile you tap, then the page opens.</summary>
        class TileRefs { public Image bg, border, badge; public Color accent; public Pulse pulse; }
        readonly List<TileRefs> tiles = new List<TileRefs>();
        static int selectedTile;

        void SelectTile(int idx, bool animate)
        {
            selectedTile = idx;
            for (int i = 0; i < tiles.Count; i++)
            {
                var r = tiles[i]; bool on = i == idx;
                r.bg.color = on ? Theme.Tint(r.accent, 0.10f) : Theme.Surface;
                r.border.color = on ? r.accent : Theme.Border;
                r.badge.color = r.accent;
                r.badge.gameObject.SetActive(on);
                r.pulse.enabled = on;
                if (!on) r.pulse.transform.localScale = Vector3.one;
                if (on && animate) AppearAnim.Apply(r.badge, 0f, 0.3f);
            }
        }

        Button GridTile(Transform parent, string label, string icon, Color accent, Action onClick)
        {
            int idx = tiles.Count;
            var img = UIKit.MakeImage(parent, Theme.Surface, true, "GridTile");
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            img.gameObject.AddComponent<PressFx>();
            var border = Widgets.Border(img.transform, Theme.Border);
            var frame = UIKit.MakeImage(img.transform, Theme.Tint(accent, 0.12f), true, "Frame");
            frame.raycastTarget = false;
            frame.rectTransform.anchorMin = frame.rectTransform.anchorMax = new Vector2(0.5f, 0.64f);
            frame.rectTransform.sizeDelta = new Vector2(120, 120);
            Widgets.Border(frame.transform, Theme.Tint(accent, 0.45f));
            var ic = UIKit.MakeIcon(frame.transform, icon, accent, 66);
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var t = UIKit.MakeText(img.transform, label, 31, Theme.Text, FontStyle.Bold, TextAnchor.UpperCenter);
            UIKit.Anchor(t.rectTransform, new Vector2(0, 0), new Vector2(1, 0.34f), new Vector2(16, 10), new Vector2(-16, 0));
            var badge = UIKit.MakeImage(img.transform, accent, true, "Check");
            badge.raycastTarget = false;
            badge.sprite = UIKit.Circle; badge.type = Image.Type.Simple;
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(1, 1);
            badge.rectTransform.sizeDelta = new Vector2(44, 44); badge.rectTransform.anchoredPosition = new Vector2(-34, -34);
            var ck = UIKit.MakeIcon(badge.transform, "check", Color.white, 28);
            ck.rectTransform.anchorMin = ck.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var pulse = Pulse.On(frame, 0.04f, 1.4f, true);
            tiles.Add(new TileRefs { bg = img, border = border, badge = badge, accent = accent, pulse = pulse });
            b.onClick.AddListener(() =>
            {
                StartCoroutine(After(0.22f, onClick));      // let the selection animation be seen
            });
            return b;
        }

        System.Collections.IEnumerator After(float s, Action a) { yield return new WaitForSeconds(s); a(); }

        GameObject overlay;

        void Start()
        {
            Application.targetFrameRate = 60;
            SimAssets.Init();
            canvas = UIKit.CreateCanvas("MenuCanvas", 0);
            var bg = UIKit.MakeImage(canvas.transform, Theme.Bg, false, "Background");
            UIKit.Stretch(bg.rectTransform);
            bg.transform.SetAsFirstSibling();
            safe = UIKit.SafeRoot(canvas);

            var head = UIKit.MakeImage(safe, Theme.Panel, false, "Header");
            header = head.rectTransform;
            UIKit.Anchor(header, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -130), Vector2.zero);
            var line = UIKit.MakeImage(header, Theme.Border, false, "Divider");
            UIKit.Anchor(line.rectTransform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 3));
            var h = UIKit.HStack(header, 16, 16);
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            backBtn = UIKit.MakeButton(header, "", Theme.Neutral, Back, 44, 98);
            UIKit.LE(backBtn, 98, 98, -1, 98);
            var bi = UIKit.MakeIcon(backBtn.transform, "back", Theme.Text, 44);
            bi.rectTransform.anchorMin = bi.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            headerTitle = UIKit.MakeText(header, "", 40, Theme.Text, FontStyle.Bold, TextAnchor.MiddleLeft);
            UIKit.LE(headerTitle, 98, 98, -1, -1, 1);
            homeLogo = UIKit.IconBadge(header, "heart", Theme.Primary, 84);
            homeLogo.transform.SetSiblingIndex(1);
            var lp = Pulse.On(homeLogo, 0.12f, 1.8f, true);
            lp.HapticBeats = true; lp.MotionOnly = true;
            var bell = UIKit.MakeButton(header, "", Theme.Panel, () => Toast("No new notifications"), 24, 84);
            UIKit.LE(bell, 84, 84, -1, 84);
            var bellIc = UIKit.MakeIcon(bell.transform, "info", Theme.SubText, 44);
            bellIc.rectTransform.anchorMin = bellIc.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var avatar = UIKit.MakeButton(header, "", Theme.Tint(Theme.Blue, 0.18f), () => Go(ParticipantPage), 24, 84);
            ((Image)avatar.targetGraphic).sprite = UIKit.Circle; ((Image)avatar.targetGraphic).type = Image.Type.Simple;
            UIKit.LE(avatar, 84, 84, -1, 84);
            var avIc = UIKit.MakeIcon(avatar.transform, "user", Theme.Blue, 44);
            avIc.rectTransform.anchorMin = avIc.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            viewA = MakeView(); viewB = MakeView();
            viewB.root.gameObject.SetActive(false);
            shown = viewA; Use(viewA);
            header.SetAsLastSibling();
            Go(Home, false);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autoshot") < 0) Splash.ShowOnce(canvas);
        }

        void Update()
        {
            bool back = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            back = Input.GetKeyDown(KeyCode.Escape);
#elif ENABLE_INPUT_SYSTEM
            back = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
#endif
            if (back) { if (overlay != null) CloseOverlay(); else Back(); }
            // iOS-style interactive navigation: drag right = back, drag left = forward again.
            // The page follows the finger with the other page visible underneath; letting go past
            // the threshold (or flicking) completes it, otherwise the page springs back.
            if (overlay != null || animating) { return; }
            int commit = drag.Update(out bool aborted);
            float sf = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
            float off = drag.OffsetPx / sf;
            if (drag.Active && !dragging && Mathf.Abs(off) > 1f)
            {
                if (off > 0 && history.Count > 0) BeginDrag(+1);
                else if (off < 0 && forward.Count > 0) BeginDrag(-1);
            }
            if (dragging)
            {
                if (drag.Active) DragTo(dragDir > 0 ? Mathf.Clamp01(off / pageW) : Mathf.Clamp01(-off / pageW));
                if (commit == dragDir) { Haptics.Tap(); FinishDrag(true); }
                else if (commit != 0 || aborted || !drag.Active) FinishDrag(false);
            }
        }

        // ------------------------------------------------------------------ page layers & transitions
        PageView MakeView()
        {
            var v = new PageView();
            v.content = UIKit.MakeScroll(safe, out v.scroll, 26, 36);
            v.root = (RectTransform)v.scroll.transform;
            UIKit.Stretch(v.root, 0, 130, 0, 0);
            var bgImg = v.root.gameObject.AddComponent<Image>();          // opaque so the page underneath is hidden
            bgImg.color = Theme.Bg;
            v.root.gameObject.AddComponent<CanvasGroup>();
            v.shade = UIKit.MakeImage(v.root, Color.black, false, "Shade");
            UIKit.Stretch(v.shade.rectTransform);
            v.shade.raycastTarget = false;
            v.shade.color = new Color(0, 0, 0, 0);
            v.shade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return v;
        }

        void Use(PageView v) { body = v.content; scroll = v.scroll; }

        void Build(PageView v, Action page)
        {
            Use(v);
            UIKit.Clear(v.content);
            v.root.gameObject.SetActive(true);
            page();
            v.shade.transform.SetAsLastSibling();
            v.scroll.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
            v.scroll.verticalNormalizedPosition = 1f;
        }

        // x offsets as a fraction of the page width; the lower page is shaded like iOS
        void Place(PageView top, float topX, PageView under, float underX)
        {
            SetX(top, topX); SetX(under, underX);
            top.shade.color = new Color(0, 0, 0, 0);
            under.shade.color = new Color(0, 0, 0, 0.18f * Mathf.Clamp01(-underX / 0.3f));
        }
        void SetX(PageView v, float f) { var ap = v.root.anchoredPosition; ap.x = f * pageW; v.root.anchoredPosition = ap; }

        System.Collections.IEnumerator Animate(PageView top, float t0, float t1, PageView under, float u0, float u1, float dur, Action done)
        {
            animating = true;
            top.scroll.vertical = under.scroll.vertical = false;
            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);    // ease-out like UIKit's spring
                Place(top, Mathf.Lerp(t0, t1, k), under, Mathf.Lerp(u0, u1, k));
                yield return null;
            }
            top.scroll.vertical = under.scroll.vertical = true;
            animating = false;
            done?.Invoke();
        }

        void UpdateChrome()
        {
            backBtn.gameObject.SetActive(history.Count > 0);
            homeLogo.gameObject.SetActive(history.Count == 0);
        }

        // finish a transition: 'next' is now the visible page, the other layer is hidden
        void Settle(PageView next)
        {
            var other = next == viewA ? viewB : viewA;
            SetX(next, 0); next.shade.color = new Color(0, 0, 0, 0);
            other.root.gameObject.SetActive(false);
            UIKit.Clear(other.content);
            SetX(other, 0);
            shown = next; Use(next);
            header.SetAsLastSibling();
        }

        // ---- interactive drag
        int dragDir; float dragP; string titleBefore;
        PageView dragTop, dragUnder;

        void BeginDrag(int dir)
        {
            dragging = true; dragDir = dir; dragP = 0;
            titleBefore = headerTitle.text;
            shown.scroll.vertical = false;
            var spare = Spare;
            if (dir > 0)
            {   // back: current page on top moves right, previous page underneath
                Build(spare, history.Peek());
                builtTitle[spare] = headerTitle.text;
                dragTop = shown; dragUnder = spare;
                spare.root.SetSiblingIndex(shown.root.GetSiblingIndex());
            }
            else
            {   // forward: the next page comes in from the right over the current one
                Build(spare, forward.Peek());
                builtTitle[spare] = headerTitle.text;
                dragTop = spare; dragUnder = shown;
                spare.root.SetSiblingIndex(shown.root.GetSiblingIndex() + 1);
            }
            header.SetAsLastSibling();
            headerTitle.text = titleBefore;      // keep the current title until the gesture completes
            Use(shown);
            DragTo(0);
        }

        void DragTo(float p)
        {
            dragP = p;
            if (dragDir > 0) Place(dragTop, p, dragUnder, -0.3f * (1f - p));
            else Place(dragTop, 1f - p, dragUnder, -0.3f * p);
        }

        void FinishDrag(bool commit)
        {
            dragging = false;
            var top = dragTop; var under = dragUnder; float p = dragP;
            float dur = 0.28f;
            if (dragDir > 0)
            {
                if (commit)
                    StartCoroutine(Animate(top, p, 1f, under, -0.3f * (1f - p), 0f, dur, () =>
                    {
                        forward.Push(current); current = history.Pop();
                        Settle(under); RefreshTitle(); UpdateChrome();
                    }));
                else
                    StartCoroutine(Animate(top, p, 0f, under, -0.3f * (1f - p), -0.3f, dur, () => { Settle(top); headerTitle.text = titleBefore; }));
            }
            else
            {
                if (commit)
                    StartCoroutine(Animate(top, 1f - p, 0f, under, -0.3f * p, -0.3f, dur, () =>
                    {
                        history.Push(current); current = forward.Pop();
                        Settle(top); RefreshTitle(); UpdateChrome();
                    }));
                else
                    StartCoroutine(Animate(top, 1f - p, 1f, under, -0.3f * p, 0f, dur, () => { Settle(under); headerTitle.text = titleBefore; }));
            }
        }

        // page titles are set by the page builders; rebuild silently into nothing is not needed -
        // we remember the title each page set when it was built.
        readonly Dictionary<PageView, string> builtTitle = new Dictionary<PageView, string>();
        void RefreshTitle() { if (builtTitle.TryGetValue(shown, out var t)) headerTitle.text = t; }

        /// <summary>QA hook used by AutoShot.</summary>
        /// <summary>Presentation recorder: open a page with the normal forward animation.</summary>
        public void DemoGo(string page)
        {
            switch (page)
            {
                case "learn": Go(LearnPage); break;
                case "dose": Go(DosePage); break;
                case "quiz": Go(QuizMenu); break;
                case "results": Go(ResultsPage); break;
                case "help": Go(HelpPage); break;
                case "participant": Go(ParticipantPage); break;
            }
        }
        public void DemoBack() => Back();

        /// <summary>QA hook (AutoShot): open screens inside the pages for screenshots.</summary>
        public void DebugInner(string what)
        {
            history.Clear(); forward.Clear(); current = null;
            var rnd = new System.Random(7);
            switch (what)
            {
                case "learn_open":
                    Go(LearnPage);
                    var bs = body.GetComponentsInChildren<Button>();
                    if (bs.Length > 0) bs[0].onClick.Invoke();
                    break;
                case "dose_question":
                    doseQs = Enumerable.Range(0, 10).Select(i => GenDose(rnd, i % 6)).ToList();
                    doseIndex = 0; doseScore = 0; doseStart = Time.time;
                    Go(DoseQuestion);
                    break;
                case "dose_result":
                    doseQs = Enumerable.Range(0, 10).Select(i => GenDose(rnd, i % 6)).ToList();
                    doseIndex = 10; doseScore = 8; doseStart = Time.time - 312f;
                    Go(DoseResult);
                    break;
                case "quiz_question":
                    StartQuiz("Knowledge Practice");
                    break;
                case "quiz_feedback":
                    StartQuiz("Knowledge Practice");
                    var ob = body.GetComponentsInChildren<Button>();
                    if (ob.Length > 1) ob[1].onClick.Invoke();
                    break;
                case "quiz_result":
                    StartQuiz("Knowledge Post-test");
                    for (int i = 0; i < quizAnswers.Length; i++) quizAnswers[i] = i % 5 == 4 ? (ClinicalContent.Quiz[i].Correct + 1) % ClinicalContent.Quiz[i].Options.Length : ClinicalContent.Quiz[i].Correct;
                    quizScore = quizAnswers.Where((a, i) => a == ClinicalContent.Quiz[i].Correct).Count();
                    quizIndex = quizOrder.Count; quizStart = Time.time - 540f;
                    Go(QuizResult);
                    break;
                case "marker":
                    Go(HelpPage);
                    ShowMarkerFullscreen();
                    break;
            }
        }

        public void DebugOpen(string page)
        {
            history.Clear(); forward.Clear(); current = null;
            switch (page)
            {
                case "learn": Go(LearnPage); break;
                case "dose": Go(DosePage); break;
                case "quiz": Go(QuizMenu); break;
                case "results": Go(ResultsPage); break;
                case "help": Go(HelpPage); break;
                case "participant": Go(ParticipantPage); break;
                default: Go(Home, false); break;
            }
        }

        // ------------------------------------------------------------------ navigation
        void Go(Action page, bool push = true)
        {
            if (animating || dragging) return;
            if (!push || current == null)
            {   // first page / reset: no slide
                current = page;
                Build(shown, page);
                builtTitle[shown] = headerTitle.text;
                UpdateChrome();
                AppearAnim.Stagger(body);
                return;
            }
            history.Push(current);
            forward.Clear();
            current = page;
            var from = shown; var to = Spare;
            Build(to, page);
            builtTitle[to] = headerTitle.text;
            to.root.SetSiblingIndex(from.root.GetSiblingIndex() + 1);
            header.SetAsLastSibling();
            UpdateChrome();
            Place(to, 1f, from, 0f);
            StartCoroutine(Animate(to, 1f, 0f, from, 0f, -0.3f, 0.34f, () => Settle(to)));
        }

        /// <summary>Back with the iOS pop animation (header button, Android back button/gesture, swipe).</summary>
        void Back()
        {
            if (history.Count == 0 || animating || dragging) return;
            forward.Push(current);
            current = history.Pop();
            var from = shown; var to = Spare;
            Build(to, current);
            builtTitle[to] = headerTitle.text;
            to.root.SetSiblingIndex(from.root.GetSiblingIndex());     // previous page goes underneath
            header.SetAsLastSibling();
            UpdateChrome();
            Place(from, 0f, to, -0.3f);
            StartCoroutine(Animate(from, 0f, 1f, to, -0.3f, 0f, 0.3f, () => Settle(to)));
        }

        void Refresh()
        {
            Build(shown, current);
            builtTitle[shown] = headerTitle.text;
            AppearAnim.Stagger(body, 0.04f);
        }

        void SetTitle(string t) => headerTitle.text = t;

        // ------------------------------------------------------------------ home
        void Home()
        {
            SetTitle("MgSO4 AR Sim");
            // gradient hero with live ECG
            var heroImg = UIKit.MakeImage(body, Color.white, true, "Hero");
            UIGradient.On(heroImg, Theme.Primary, Theme.Blue, true);
            var hero = heroImg.rectTransform;
            UIKit.VStack(hero, 10, 40);
            var ecgRect = UIKit.NewRect("ECG", hero);
            UIKit.LE(ecgRect, 90, 90);
            var ecg = ecgRect.gameObject.AddComponent<EcgGraphic>();
            ecg.color = new Color(1, 1, 1, 0.85f); ecg.Thickness = 5; ecg.Bpm = 78; ecg.raycastTarget = false; ecg.MotionReactive = true;
            UIKit.MakeText(hero, "Magnesium Sulfate for Eclampsia", 52, Color.white, FontStyle.Bold);
            UIKit.MakeText(hero, "AR simulation for nursing students: knowledge, self-efficacy and clinical reasoning in MgSO4 administration.", 29, new Color(1, 1, 1, 0.92f));
            var chips = UIKit.NewRect("Chips", hero);
            var ch = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            ch.spacing = 12; ch.childControlWidth = true; ch.childControlHeight = true; ch.childForceExpandWidth = false; ch.childForceExpandHeight = false;
            UIKit.Chip(chips, "22-step scenario", new Color(1, 1, 1, 0.22f), 24);
            UIKit.Chip(chips, "Markerless AR", new Color(1, 1, 1, 0.22f), 24);
            UIKit.Chip(chips, "Research data", new Color(1, 1, 1, 0.22f), 24);
            foreach (var t in chips.GetComponentsInChildren<Text>()) t.color = Color.white;
            UIKit.MakeText(hero, "Faculty of Nursing  -  Mansoura University", 24, new Color(1, 1, 1, 0.78f));
            UIKit.WithShadow(hero, 0.14f, 26, 12);

            var p = UIKit.Card(body, Theme.Surface, 26, 12);
            var row = UIKit.Row(p, 96, 20);
            var hr = row.GetComponent<HorizontalLayoutGroup>(); hr.childForceExpandWidth = false; hr.childAlignment = TextAnchor.MiddleLeft;
            UIKit.IconBadge(row, "user", Theme.Blue, 84);
            var who = UIKit.MakeText(row, $"<b>{Participant.Display}</b>\n<size=25><color={Theme.Html(Theme.SubText)}>Group: {Participant.Group}</color></size>", 31, Theme.Text, FontStyle.Normal, TextAnchor.MiddleLeft);
            UIKit.LE(who, 90, 90, -1, -1, 1);
            var edit = UIKit.MakeButton(row, "Edit", Theme.Neutral, () => Go(ParticipantPage), 28, 80);
            UIKit.LE(edit, 80, 80, -1, 150);

            var grid = UIKit.NewRect("Grid", body);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(492, 270); g.spacing = new Vector2(24, 24);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = 2;
            g.childAlignment = TextAnchor.UpperCenter;
            tiles.Clear();
            GridTile(grid, "AR Clinical Simulation", "heart", Theme.Primary, () => SceneManager.LoadScene("ARSim"));
            GridTile(grid, "Learn", "book", Theme.Blue, () => Go(LearnPage));
            GridTile(grid, "Dose Calculations", "calc", Theme.Purple, () => Go(DosePage));
            GridTile(grid, "Knowledge Test", "clipboard", Theme.Orange, () => Go(QuizMenu));
            GridTile(grid, "Results & Data", "chart", Theme.Success, () => Go(ResultsPage));
            GridTile(grid, "AR Marker & Help", "marker", Theme.SubText, () => Go(HelpPage));
            SelectTile(-1, false);          // no permanent selection; touch feedback is the ripple
            UIKit.MakeText(body, "<i>Educational simulation only. Always follow your institution's protocols and national guidelines.</i>", 23, Theme.SubText, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------------ participant
        void ParticipantPage()
        {
            SetTitle("Participant");
            var c = UIKit.Card(body);
            UIKit.MakeText(c, "Participant / student ID", 32, Theme.Text, FontStyle.Bold);
            UIKit.MakeText(c, "Used only to label your results for the study. Leave empty to practise as a guest.", 26, Theme.SubText);
            var input = UIKit.MakeInput(c, "e.g. AR-017", InputField.ContentType.Alphanumeric);
            input.text = Participant.Id;
            UIKit.MakeText(c, "Study group", 32, Theme.Text, FontStyle.Bold);
            string group = Participant.Group;
            var groups = new[] { "AR Intervention", "Control", "Training" };
            var buttons = new List<Button>();
            foreach (var g in groups)
            {
                string gg = g;
                var b = UIKit.MakeButton(c, g, g == group ? Theme.Primary : Theme.Neutral, null, 32, 100);
                b.onClick.AddListener(() =>
                {
                    group = gg;
                    for (int i = 0; i < groups.Length; i++) buttons[i].SetColor(groups[i] == group ? Theme.Primary : Theme.Neutral);
                });
                buttons.Add(b);
            }
            var hc = UIKit.Card(body);
            var hr2 = UIKit.Row(hc, 80, 16);
            var hrl = hr2.GetComponent<HorizontalLayoutGroup>(); hrl.childForceExpandWidth = false; hrl.childAlignment = TextAnchor.MiddleLeft;
            var ht = UIKit.MakeText(hr2, "<b>Vibration feedback</b>\n<size=24>Haptics for taps, answers, alarms and seizures</size>", 30, Theme.Text, FontStyle.Normal, TextAnchor.MiddleLeft);
            UIKit.LE(ht, -1, -1, -1, -1, 1);
            Button hb = null;
            hb = UIKit.MakeButton(hr2, Haptics.Enabled ? "On" : "Off", Haptics.Enabled ? Theme.Primary : Theme.Neutral, () =>
            {
                Haptics.Enabled = !Haptics.Enabled;
                hb.SetColor(Haptics.Enabled ? Theme.Primary : Theme.Neutral);
                hb.Label().text = Haptics.Enabled ? "On" : "Off";
                if (Haptics.Enabled) Haptics.Success();
            }, 30, 80);
            UIKit.LE(hb, 80, 80, -1, 150);
            UIKit.MakeButton(body, "Save", Theme.Navy, () =>
            {
                Participant.Id = input.text.Trim();
                Participant.Group = group;
                Back();
            }, 36, 120);
        }

        // ------------------------------------------------------------------ learn
        void LearnPage()
        {
            SetTitle("Learn");
            UIKit.MakeText(body, "Tap a topic to open it.", 28, Theme.SubText);
            foreach (var card in ClinicalContent.Cards)
            {
                var cc = card;
                var container = UIKit.Card(body, Theme.Surface, 0, 0);
                RectTransform content = null;
                var b = UIKit.MakeButton(container, cc.Title, Theme.Surface, null, 34, -1, Theme.Text, TextAnchor.MiddleLeft);
                b.Label().fontStyle = FontStyle.Bold;
                b.onClick.AddListener(() =>
                {
                    if (content == null)
                    {
                        content = UIKit.NewRect("Body", container);
                        UIKit.VStack(content, 14, 34);
                        UIKit.MakeText(content, cc.Body, 30, Theme.Text);
                        UIKit.MakeText(content, "<i>Source: " + cc.Source + "</i>", 24, Theme.SubText);
                    }
                    else content.gameObject.SetActive(!content.gameObject.activeSelf);
                });
            }
        }

        // ------------------------------------------------------------------ dose practice
        class DoseQ { public string Text, Unit, Solution; public float Answer; }

        List<DoseQ> doseQs;
        int doseIndex, doseScore, doseAttempts;
        float doseStart;

        void DosePage()
        {
            SetTitle("Dose Calculation Practice");
            var c = UIKit.Card(body);
            UIKit.MakeText(c, "10 randomised calculations", 36, Theme.Text, FontStyle.Bold);
            UIKit.MakeText(c, "Covers MgSO4 50% vials (1 g / 2 mL), dilution to 20%, infusion-pump rates, gravity drip rates and calcium gluconate. Two attempts per question; the worked solution is shown after.", 28, Theme.Text);
            UIKit.MakeButton(body, "Start", Theme.Primary, () =>
            {
                var rnd = new System.Random();
                doseQs = Enumerable.Range(0, 10).Select(i => GenDose(rnd, i % 6)).OrderBy(_ => rnd.Next()).ToList();
                doseIndex = 0; doseScore = 0; doseStart = Time.time;
                Go(DoseQuestion);
            }, 38, 130);
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static DoseQ GenDose(System.Random r, int type)
        {
            switch (type)
            {
                case 0:
                {
                    float g = new[] { 2f, 4f, 5f, 6f, 10f }[r.Next(5)];
                    float ans = g * 2f;
                    return new DoseQ { Text = $"Order: MgSO4 {F(g)} g.\nAvailable: MgSO4 50% vials (1 g / 2 mL).\nHow many mL do you draw up?", Unit = "mL", Answer = ans,
                        Solution = $"50% = 0.5 g/mL  ->  {F(g)} g / 0.5 g/mL = {F(ans)} mL" };
                }
                case 1:
                {
                    float g = new[] { 2f, 4f }[r.Next(2)];
                    float total = g / 0.2f, drug = g * 2f, ans = total - drug;
                    return new DoseQ { Text = $"You need {F(g)} g MgSO4 as a 20% solution, using MgSO4 50%.\nHow many mL of sterile water do you add to the {F(drug)} mL of MgSO4 50%?", Unit = "mL", Answer = ans,
                        Solution = $"20% = 0.2 g/mL  ->  {F(g)} g needs {F(total)} mL total.  {F(total)} - {F(drug)} = {F(ans)} mL water" };
                }
                case 2:
                {
                    var bags = new[] { (20f, 500f), (40f, 1000f), (4f, 100f), (20f, 250f) };
                    var bag = bags[r.Next(bags.Length)];
                    float rate = new[] { 1f, 1.5f, 2f }[r.Next(3)];
                    float conc = bag.Item1 / bag.Item2, ans = rate / conc;
                    return new DoseQ { Text = $"Premixed bag: MgSO4 {F(bag.Item1)} g in {F(bag.Item2)} mL.\nOrder: {F(rate)} g per hour.\nPump rate?", Unit = "mL/h", Answer = ans,
                        Solution = $"Concentration = {F(bag.Item1)} / {F(bag.Item2)} = {conc:0.###} g/mL  ->  {F(rate)} / {conc:0.###} = {F(ans)} mL/h" };
                }
                case 3:
                {
                    float g = new[] { 4f, 6f }[r.Next(2)];
                    float min = new[] { 20f, 30f }[r.Next(2)];
                    float ans = 100f / (min / 60f);
                    return new DoseQ { Text = $"Loading dose: MgSO4 {F(g)} g in 100 mL to run over {F(min)} minutes by infusion pump.\nPump rate?", Unit = "mL/h", Answer = ans,
                        Solution = $"100 mL / ({F(min)} / 60 h) = {F(ans)} mL/h" };
                }
                case 4:
                {
                    float df = new[] { 15f, 20f }[r.Next(2)];
                    float ans = 100f * df / 20f;
                    return new DoseQ { Text = $"MgSO4 4 g in 100 mL over 20 minutes by gravity giving set (drop factor {F(df)} drops/mL).\nDrops per minute?", Unit = "drops/min", Answer = ans,
                        Solution = $"100 mL x {F(df)} drops/mL / 20 min = {F(ans)} drops/min (MaMoni chart: 60-75 dpm with a 15-drop set)" };
                }
                default:
                {
                    if (r.Next(2) == 0)
                        return new DoseQ { Text = "Antidote: calcium gluconate 10% (100 mg/mL). Dose 1 g.\nHow many mL?", Unit = "mL", Answer = 10,
                            Solution = "10% = 100 mg/mL  ->  1000 mg / 100 mg/mL = 10 mL, IV slowly over at least 3 minutes" };
                    float ml = new[] { 5f, 10f, 12f }[r.Next(3)];
                    return new DoseQ { Text = $"How many grams of MgSO4 are in {F(ml)} mL of MgSO4 50%?", Unit = "g", Answer = ml * 0.5f,
                        Solution = $"{F(ml)} mL x 0.5 g/mL = {F(ml * 0.5f)} g" };
                }
            }
        }

        void DoseQuestion()
        {
            if (doseIndex >= doseQs.Count) { DoseResult(); return; }
            var q = doseQs[doseIndex];
            doseAttempts = 0;
            SetTitle($"Calculation {doseIndex + 1} / {doseQs.Count}");
            var c = UIKit.Card(body);
            UIKit.MakeText(c, q.Text, 34, Theme.Text);
            var fb = UIKit.NewRect("Feedback", body);
            UIKit.VStack(fb, 16, 0);
            bool done = false;
            Widgets.Keypad kp = null;
            kp = Widgets.MakeKeypad(body, q.Unit, val =>
            {
                if (done) return;
                if (!float.TryParse((val ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { Toast("Enter a number"); return; }
                doseAttempts++;
                bool ok = Mathf.Abs(v - q.Answer) <= Mathf.Max(0.05f, q.Answer * 0.01f);
                UIKit.Clear(fb);
                if (ok || doseAttempts >= 2)
                {
                    done = true;
                    if (ok && doseAttempts == 1) doseScore++;
                    if (ok) Haptics.Success(); else Haptics.Error();
                    var card = UIKit.Card(fb, ok ? Theme.SuccessBg : Theme.DangerBg);
                    UIKit.MakeText(card, ok ? "Correct!" : $"Answer: {F(q.Answer)} {q.Unit}", 36, ok ? Theme.Success : Theme.Danger, FontStyle.Bold);
                    UIKit.MakeText(card, "<b>Visual worked solution</b>\n" + q.Solution, 30, Theme.Text);
                    UIKit.MakeButton(fb, doseIndex == doseQs.Count - 1 ? "See result" : "Next", Theme.Navy, () => { doseIndex++; Refresh(); }, 34, 104);
                    AppearAnim.Stagger(fb, 0.05f);
                    StartCoroutine(ScrollDown());
                }
                else
                {
                    Haptics.Error();
                    kp.Clear();
                    var card = UIKit.Card(fb, Theme.WarnBg);
                    UIKit.MakeText(card, "Not correct - try once more.", 32, Theme.AccentText, FontStyle.Bold);
                    AppearAnim.Stagger(fb, 0.05f);
                }
            });
            fb.SetAsLastSibling();
        }

        System.Collections.IEnumerator ScrollDown()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }

        void DoseResult()
        {
            SetTitle("Dose practice result");
            float dur = Time.time - doseStart;
            ResultsStore.Save(new SessionRecord { Activity = "Dose Practice", Mode = "-", Score = doseScore, MaxScore = doseQs.Count, DurationSec = dur });
            var c = UIKit.Card(body, Theme.Surface, 30, 10);
            var dr = UIKit.Row(c, 300, 30);
            var drh = dr.GetComponent<HorizontalLayoutGroup>(); drh.childForceExpandWidth = false; drh.childAlignment = TextAnchor.MiddleLeft;
            float frac = (float)doseScore / Mathf.Max(1, doseQs.Count);
            Widgets.Donut(dr, frac, frac >= 0.75f ? Theme.Success : frac >= 0.5f ? Theme.Accent : Theme.Danger, 280, "First try");
            var big = UIKit.MakeText(dr, "", 40, Theme.Text, FontStyle.Bold, TextAnchor.MiddleLeft);
            UIKit.LE(big, -1, -1, -1, -1, 1);
            CountUp.On(big, doseScore, "{0:0} / " + doseQs.Count + " correct on first attempt");
            UIKit.MakeText(c, $"Time: {dur / 60f:0.0} min", 28, Theme.SubText);
            UIKit.MakeButton(body, "Practise again", Theme.Primary, () => { history.Clear(); forward.Clear(); current = null; Go(DosePage); }, 36, 120);
            UIKit.MakeButton(body, "Main menu", Theme.Neutral, () => { history.Clear(); Go(Home, false); }, 36, 120);
        }

        // ------------------------------------------------------------------ quiz
        string quizMode;
        List<int> quizOrder;
        int quizIndex, quizScore;
        int[] quizAnswers;
        float quizStart;

        void QuizMenu()
        {
            SetTitle("Knowledge Test");
            var c = UIKit.Card(body);
            UIKit.MakeText(c, "Structured knowledge questionnaire on MgSO4 indications, dosage, administration, monitoring, side effects and toxicity management (20 questions).", 30, Theme.Text);
            UIKit.MakeText(c, "<b>Pre-test / Post-test:</b> no feedback until the end, fixed order (for the study).\n<b>Practice:</b> shuffled, with feedback after each answer.", 28, Theme.SubText);
            UIKit.MakeButton(body, "Pre-test", Theme.Blue, () => StartQuiz("Knowledge Pre-test"), 36, 120);
            UIKit.MakeButton(body, "Post-test", Theme.Orange, () => StartQuiz("Knowledge Post-test"), 36, 120);
            UIKit.MakeButton(body, "Practice with feedback", Theme.Primary, () => StartQuiz("Knowledge Practice"), 36, 120);
        }

        void StartQuiz(string m)
        {
            quizMode = m;
            int n = ClinicalContent.Quiz.Length;
            quizOrder = Enumerable.Range(0, n).ToList();
            if (m == "Knowledge Practice") { var rnd = new System.Random(); quizOrder = quizOrder.OrderBy(_ => rnd.Next()).ToList(); }
            quizIndex = 0; quizScore = 0; quizAnswers = Enumerable.Repeat(-1, n).ToArray();
            quizStart = Time.time;
            Go(QuizQuestion);
        }

        void QuizQuestion()
        {
            if (quizIndex >= quizOrder.Count) { QuizResult(); return; }
            bool practice = quizMode == "Knowledge Practice";
            var q = ClinicalContent.Quiz[quizOrder[quizIndex]];
            SetTitle($"{quizMode.Replace("Knowledge ", "")}  {quizIndex + 1} / {quizOrder.Count}");
            ProgressBar.Make(body, (quizIndex + 1f) / quizOrder.Count, Theme.Orange, 12);
            var c = UIKit.Card(body);
            UIKit.MakeText(c, $"<size=24><color={Theme.Html(Theme.Orange)}>{q.Topic.ToUpperInvariant()}</color></size>\n{q.Question}", 36, Theme.Text, FontStyle.Bold);
            var buttons = new List<Widgets.Option>();
            var fb = UIKit.NewRect("fb", body);
            bool answered = false;
            for (int i = 0; i < q.Options.Length; i++)
            {
                int idx = i;
                buttons.Add(Widgets.MakeOption(body, q.Options[i], false, () =>
                {
                    if (answered) return;
                    answered = true;
                    quizAnswers[quizOrder[quizIndex]] = idx;
                    if (idx == q.Correct) quizScore++;
                    if (practice)
                    {
                        for (int k = 0; k < buttons.Count; k++)
                            buttons[k].SetResult(k == q.Correct ? 1 : k == idx ? -1 : 0, k == idx);
                        if (idx == q.Correct) Haptics.Success(); else Haptics.Error();
                        UIKit.VStack(fb, 16, 0);
                        var card = UIKit.Card(fb, idx == q.Correct ? Theme.SuccessBg : Theme.DangerBg);
                        UIKit.MakeText(card, idx == q.Correct ? "Correct" : "Incorrect", 34, idx == q.Correct ? Theme.Success : Theme.Danger, FontStyle.Bold);
                        UIKit.MakeText(card, q.Rationale, 30, Theme.Text);
                        UIKit.MakeButton(fb, "Next", Theme.Navy, () => { quizIndex++; Refresh(); }, 34, 104);
                        fb.SetAsLastSibling();
                        AppearAnim.Stagger(fb, 0.05f);
                        StartCoroutine(ScrollDown());
                    }
                    else
                    {
                        buttons[idx].SetSelected(true);
                        quizIndex++;
                        Invoke(nameof(Refresh), 0.3f);
                    }
                }));
            }
            fb.SetAsLastSibling();
        }

        void QuizResult()
        {
            SetTitle("Test result");
            float dur = Time.time - quizStart;
            int n = quizOrder.Count;
            var topics = ClinicalContent.Quiz.Select((q, i) => new { q.Topic, ok = quizAnswers[i] == q.Correct })
                .GroupBy(x => x.Topic).Select(g => $"{g.Key} {g.Count(x => x.ok)}/{g.Count()}");
            ResultsStore.Save(new SessionRecord
            {
                Activity = quizMode, Mode = "-", Score = quizScore, MaxScore = n, DurationSec = dur,
                Details = "answers=" + string.Join("", quizAnswers.Select(a => a < 0 ? "-" : ((char)('A' + a)).ToString())) + "; " + string.Join("; ", topics)
            });
            var c = UIKit.Card(body, Theme.Surface, 30, 10);
            var dr = UIKit.Row(c, 300, 30);
            var drh = dr.GetComponent<HorizontalLayoutGroup>(); drh.childForceExpandWidth = false; drh.childAlignment = TextAnchor.MiddleLeft;
            float frac = n > 0 ? (float)quizScore / n : 0;
            Widgets.Donut(dr, frac, frac >= 0.75f ? Theme.Success : frac >= 0.5f ? Theme.Accent : Theme.Danger, 280, "Score");
            var big = UIKit.MakeText(dr, "", 40, Theme.Text, FontStyle.Bold, TextAnchor.MiddleLeft);
            UIKit.LE(big, -1, -1, -1, -1, 1);
            CountUp.On(big, quizScore, "{0:0} / " + n + " correct");
            if (frac >= 0.75f) Haptics.Success();
            UIKit.MakeText(c, $"{quizMode}  -  {dur / 60f:0.0} min", 28, Theme.SubText);
            if (quizMode == "Knowledge Practice")
            {
                var r = UIKit.Card(body);
                UIKit.MakeText(r, "<b>Review</b>", 32, Theme.Text);
                for (int i = 0; i < n; i++)
                {
                    var q = ClinicalContent.Quiz[i];
                    bool ok = quizAnswers[i] == q.Correct;
                    UIKit.MakeText(r, (ok ? "<color=#1FAA6B>OK</color>  " : "<color=#E5484D>X</color>  ") + q.Question + (ok ? "" : $"\n     <color=#5E7184>Answer: {q.Options[q.Correct]}</color>"), 28, Theme.Text);
                }
            }
            else UIKit.MakeText(body, "Your answers were saved for the study. Feedback is not shown in pre/post-test mode.", 28, Theme.SubText);
            UIKit.MakeButton(body, "Main menu", Theme.Neutral, () => { history.Clear(); Go(Home, false); }, 36, 120);
        }

        // ------------------------------------------------------------------ results
        bool confirmClear;

        void ResultsPage()
        {
            SetTitle("Results & Data");
            var all = ResultsStore.LoadSessions();
            var mine = string.IsNullOrEmpty(Participant.Id) ? all : all.Where(r => r.ParticipantId == Participant.Id).ToList();
            var c = UIKit.Card(body);
            UIKit.MakeText(c, $"<b>{all.Count}</b> sessions on this device  |  <b>{mine.Count}</b> for {Participant.Display}", 30, Theme.Text);
            UIKit.MakeText(c, "Data files (CSV, open in Excel/SPSS):\n" + ResultsStore.SessionsCsv + "\n" + ResultsStore.StepsCsv +
                "\n<i>Copy them via USB: Android/data/<app>/files/</i>", 24, Theme.SubText);

            foreach (var r in mine.AsEnumerable().Reverse().Take(60))
            {
                var card = UIKit.Card(body, Theme.Surface, 28, 8);
                UIKit.MakeText(card, $"<b>{r.Activity}</b>   {r.Score:0.#}/{r.MaxScore:0.#}  ({r.Percent:0}%)", 32, Theme.Text);
                ProgressBar.Make(card, r.Percent / 100f, r.Percent >= 75 ? Theme.Success : r.Percent >= 50 ? Theme.Accent : Theme.Danger, 10);
                UIKit.MakeText(card, $"{r.Timestamp}  |  {r.ParticipantId} ({r.Group})  |  {r.DurationSec / 60f:0.0} min  {(string.IsNullOrEmpty(r.Mode) || r.Mode == "-" ? "" : "|  " + r.Mode)}", 24, Theme.SubText);
                if (!string.IsNullOrEmpty(r.Details) && !r.Details.StartsWith("answers")) UIKit.MakeText(card, r.Details, 24, Theme.SubText);
            }
            if (mine.Count == 0) UIKit.MakeText(body, "No results yet.", 30, Theme.SubText, FontStyle.Italic, TextAnchor.MiddleCenter);
            confirmClear = false;
            Button clear = null;
            clear = UIKit.MakeButton(body, "Delete all results on this device", Theme.DangerBg, () =>
            {
                if (!confirmClear) { confirmClear = true; clear.Label().text = "Tap again to confirm deleting ALL results"; clear.SetColor(Theme.Danger); return; }
                ResultsStore.ClearAll();
                Refresh();
            }, 30, 100);
        }

        // ------------------------------------------------------------------ help / marker
        void HelpPage()
        {
            SetTitle("AR Marker & Help");
            var m = UIKit.Card(body);
            UIKit.MakeText(m, "<b>The AR marker (optional)</b>", 34, Theme.Text);
            UIKit.MakeText(m, "Print the marker at <b>18 cm wide</b> (file 'MgSO4_AR_Marker_A4.pdf' in Documents/App Guide), or show it full-screen on a second phone, tablet or laptop. Place it flat on a table and point this phone at it.", 28, Theme.Text);
            if (Prim.Marker != null)
            {
                var holder = UIKit.NewRect("MarkerHolder", m);
                UIKit.LE(holder, 600, 600);
                var raw = UIKit.NewRect("Marker", holder).gameObject.AddComponent<RawImage>();
                raw.texture = Prim.Marker;
                raw.rectTransform.anchorMin = raw.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                raw.rectTransform.sizeDelta = new Vector2(600f * Prim.Marker.width / Prim.Marker.height, 600f);
            }
            UIKit.MakeButton(m, "Show marker full screen", Theme.Primary, ShowMarkerFullscreen, 34, 110);

            var h = UIKit.Card(body);
            UIKit.MakeText(h, "<b>AR modes</b> (switch with the mode button in the simulation)", 32, Theme.Text);
            UIKit.MakeText(h,
                "• <b>Surface</b> (default, no marker) - scan a table or the floor, tap the ring to place the bed, walk around it. Pinch to resize (table size to life size); Recenter to move it. Needs a phone with ARCore.\n" +
                "• <b>Marker</b> - optional printed marker (Vuforia image tracking). Works without ARCore on ARM64 Android 10+ phones.\n" +
                "• <b>Camera</b> - live camera + gyroscope. No marker needed; tap 'Recenter' to bring the ward in front of you.\n" +
                "• <b>3D</b> - no camera. Drag to rotate, pinch to zoom. Works on every device.\n\n" +
                "<b>In the scenario</b>: read the panel, answer, and tap flashing objects (knee = reflex, chest = respirations, urine bag, pump, vials, O2 mask, antidote). Hide the panel to see more of the scene.", 28, Theme.Text);

            var r = UIKit.Card(body);
            UIKit.MakeText(r, "<b>References used to build this app</b>", 32, Theme.Text);
            UIKit.MakeText(r,
                "1. El-Kurdy RM, Shiba HAA, Hassan MS, Osman NE. Effect of applying AR simulation on nursing students' knowledge, self-efficacy and clinical reasoning competency in MgSO4 administration for eclamptic patients: an RCT (proposal). Faculty of Nursing, Mansoura University, 2026.\n" +
                "2. Eclampsia and role of magnesium sulfate - summary notes (ACOG recommendations).\n" +
                "3. Jhpiego. MgSO4 Dosing and Monitoring Checklist. 2017.\n" +
                "4. USAID MaMoni MNCSP. Flow chart for specific management of severe pre-eclampsia and eclampsia (WHO guidelines). 2020.\n" +
                "5. AHRQ Safety Program for Perinatal Care. Safe Medication Administration: Magnesium Sulfate.", 26, Theme.SubText);
            UIKit.MakeText(body, $"Version {Application.version}  |  Vuforia licence {(string.IsNullOrEmpty(MarkerMode.LicenseKey) ? "missing" : "installed")}", 24, Theme.SubText, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void ShowMarkerFullscreen()
        {
            if (Prim.Marker == null) return;
            overlay = UIKit.MakeImage(canvas.transform, Color.white, false, "MarkerOverlay").gameObject;
            var rt = (RectTransform)overlay.transform;
            UIKit.Stretch(rt);
            var raw = UIKit.NewRect("Marker", rt).gameObject.AddComponent<RawImage>();
            raw.texture = Prim.Marker;
            raw.raycastTarget = false;
            var fitter = raw.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)Prim.Marker.width / Prim.Marker.height;
            var close = overlay.AddComponent<Button>();
            close.onClick.AddListener(CloseOverlay);
            var t = UIKit.MakeText(rt, "Tap anywhere to close", 28, new Color(0.3f, 0.3f, 0.3f), FontStyle.Normal, TextAnchor.LowerCenter);
            UIKit.Stretch(t.rectTransform, 0, 0, 0, 40);
            Screen.brightness = 1f;
        }

        void CloseOverlay()
        {
            if (overlay != null) Destroy(overlay);
            overlay = null;
        }
    }
}
#endif
