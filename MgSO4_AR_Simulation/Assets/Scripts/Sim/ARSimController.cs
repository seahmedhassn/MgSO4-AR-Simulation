#if MGSO4_READY
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MgSO4AR
{
    /// <summary>
    /// The AR simulation scene: builds the ward, chooses a tracking mode (Vuforia marker ->
    /// camera/gyro overlay -> 3D), runs the clinical scenario and the toxicity explorer, and
    /// records results for the study.
    /// </summary>
    public class ARSimController : MonoBehaviour
    {
        [System.NonSerialized] public Camera simCamera;
        [System.NonSerialized] public GameObject vuforiaCameraObject;

        public float SheetFraction => sheetCollapsed ? 0.08f : 0.46f;

        Ward ward;
        VitalsMonitor monitor;
        List<TrackingMode> modes;
        TrackingMode mode;

        // UI
        Canvas canvas;
        RectTransform safe, sheet, sheetContent, hintBox, toastBox;
        ScrollRect sheetScroll;
        Text modeLabel, vitalsText, hintText, toastText, collapseLabel, labelsBtnLabel;
        bool sheetCollapsed, labelsOn = true;
        float toastUntil;

        // scenario state
        enum Tab { Scenario, Explorer }
        Tab tab = Tab.Scenario;
        ScenarioStep[] steps;
        int stepIndex;
        float stepStart, sessionStart;
        int attempts;
        bool stepDone;
        readonly List<StepRecord> records = new List<StepRecord>();
        readonly HashSet<string> tapped = new HashSet<string>();
        readonly HashSet<int> multiSel = new HashSet<int>();
        readonly HashSet<string> assessed = new HashSet<string>();
        RectTransform feedbackSlot;
        Text tapChecklist;
        float pumpRate;
        bool pumpRunning;
        float syringeFill;
        string sessionId;

        // explorer state
        float explorerMg = 6f;

        // ------------------------------------------------------------------
        void Start()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            SimAssets.Init();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (simCamera == null && root.name == "SimCamera") simCamera = root.GetComponent<Camera>();
                if (vuforiaCameraObject == null && root.name == "VuforiaARCamera") vuforiaCameraObject = root;
            }
            if (simCamera == null) simCamera = Camera.main;
            CamRef.Active = simCamera;

            ward = Ward.Build(null);
            monitor = gameObject.AddComponent<VitalsMonitor>();
            monitor.ward = ward;

            modes = new List<TrackingMode>
            {
                gameObject.AddComponent<ARFoundationMode>(),
                gameObject.AddComponent<MarkerMode>(),
                gameObject.AddComponent<CameraOverlayMode>(),
                gameObject.AddComponent<FreeOrbitMode>(),
            };
            foreach (var m in modes) { m.Ctl = this; m.enabled = false; }

            BuildHud();
            steps = ScenarioData.Build();
            StartScenario();

            // Start in the 3D view while the phone's AR support is checked, then pick automatically:
            // ARCore/ARKit available -> markerless Surface (AR Foundation, no watermark)
            // otherwise -> Vuforia marker tracking -> Camera (gyro) -> 3D.
            SetMode(modes.First(m => m is FreeOrbitMode));
            Toast("Checking AR support on this phone...", 2f);
            StartCoroutine(ARFoundationMode.Probe(ok =>
            {
                if (this == null) return;
                var preferred = PlayerPrefs.GetString("tracking_mode_user", "");
                var chosen = modes.FirstOrDefault(m => m.Short == preferred && m.Available && !(m is FreeOrbitMode && preferred != "3D"));
                if (chosen == null)
                {
                    if (ok) chosen = modes.First(m => m is ARFoundationMode);
                    else chosen = modes.FirstOrDefault(m => m is MarkerMode && m.Available)
                                  ?? modes.FirstOrDefault(m => m is CameraOverlayMode && m.Available)
                                  ?? modes.First(m => m is FreeOrbitMode);
                }
                SetMode(chosen);
                Toast(ok ? "ARCore found - markerless Surface AR" : "No ARCore on this phone - using " + chosen.Label, 3f);
            }));
        }

        void OnDestroy()
        {
            if (mode != null) { mode.SetTorch(false); mode.Exit(ward); }
        }

        // QA hooks used by AutoShot
        public void DebugShowStep(int i) { if (tab != Tab.Scenario) SetTab(Tab.Scenario); ShowStep(i, true); }
        public void DebugCollapse() => ToggleSheet();
        public void DebugExplorer(float mg) { explorerMg = mg; SetTab(Tab.Explorer); }
        public void DebugDebrief() { SetTab(Tab.Scenario); stepIndex = steps.Length - 1; ShowStep(steps.Length, true); }

        // ------------------------------------------------------------------ modes
        void SetMode(TrackingMode m)
        {
            if (mode == m) return;
            if (mode != null) { if (torchOn) mode.SetTorch(false); mode.Exit(ward); }
            torchOn = false;
            SetTorchVisual();
            if (!m.UsesVuforia) VuforiaSession.Release(this);
            mode = m;
            mode.enabled = true;
            mode.Enter(ward);
            CamRef.Active = mode.Cam;
            modeLabel.text = "AR-mode: " + mode.Short;
            AppearAnim.Apply(modeLabel.transform.parent, 0f, 0.85f);
            PlayerPrefs.SetString("tracking_mode", mode.Short);
            Toast(mode.Label);
        }

        GameObject modeMenu;
        Button torchBtn;
        bool torchOn;

        void ToggleTorch()
        {
            bool want = !torchOn;
            bool ok = mode != null && mode.SetTorch(want);
            torchOn = ok && want;
            SetTorchVisual();
            if (want && !ok)
                Toast(mode is FreeOrbitMode ? "The 3D view does not use the camera - no flashlight needed."
                    : "Flashlight is not available on this phone in " + (mode != null ? mode.Short : "") + " mode. Try Surface or Marker mode.", 4f);
        }

        Image torchIcon;
        void SetTorchVisual()
        {
            if (torchBtn == null) return;
            ((Image)torchBtn.targetGraphic).color = torchOn ? Theme.Highlight : Theme.HudButton;
            if (torchIcon != null) torchIcon.color = torchOn ? Theme.Text : Color.white;
        }

        void CycleMode()
        {
            if (modeMenu != null) { Destroy(modeMenu); modeMenu = null; return; }
            var bg = UIKit.MakeImage(canvas.transform, new Color(0, 0, 0, 0.6f), false, "ModeMenu");
            UIKit.Stretch(bg.rectTransform);
            modeMenu = bg.gameObject;
            var close = modeMenu.AddComponent<Button>();
            close.onClick.AddListener(() => { Destroy(modeMenu); modeMenu = null; });
            var cardImg = UIKit.MakeImage(bg.transform, Theme.Panel, true, "ModeCard");
            var card = cardImg.rectTransform;
            UIKit.VStack(card, 18, 36);
            AppearAnim.Apply(card, 0f, 0.9f);
            UIKit.Anchor(card, new Vector2(0.06f, 0.5f), new Vector2(0.94f, 0.5f), Vector2.zero, Vector2.zero);
            card.pivot = new Vector2(0.5f, 0.5f);
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.MakeText(card, "Choose AR mode", 42, Theme.Text, FontStyle.Bold);
            var info = new Dictionary<string, string>
            {
                { "Surface", "No marker, no watermark (Google ARCore). Scan a table or the floor and tap to place the bed. Pinch or +/- to resize; Recenter to move it." },
                { "Marker", "Vuforia: point the camera at the printed MgSO4 AR marker. Works without ARCore (may show a Vuforia watermark)." },
                { "Camera", "Camera + gyroscope, no marker. Works on almost every phone." },
                { "3D", "No camera. Drag to rotate, pinch to zoom." },
            };
            foreach (var m in modes)
            {
                var mm = m;
                bool avail = m.Available;
                string label = (m == mode ? "> " : "") + m.Label + (avail ? "" : "  (not available on this device)");
                var b = UIKit.MakeButton(card, label + "\n<size=26>" + info[m.Short] + "</size>", m == mode ? Theme.Primary : Theme.Card, () =>
                {
                    Destroy(modeMenu); modeMenu = null;
                    if (mm.Available) { PlayerPrefs.SetString("tracking_mode_user", mm.Short); SetMode(mm); }
                }, 32, -1, null, TextAnchor.MiddleLeft);
                b.interactable = avail;
            }
        }

        TrackingMode Fallback(TrackingMode failed)
        {
            // no ARCore -> Vuforia marker; Vuforia failure -> Camera (gyro); last resort 3D
            if (failed is ARFoundationMode)
            {
                var marker = modes.FirstOrDefault(m => m is MarkerMode && m.Available);
                if (marker != null) return marker;
            }
            return modes.FirstOrDefault(m => m is CameraOverlayMode && m != failed && m.Available)
                ?? modes.First(m => m is FreeOrbitMode);
        }

        public void OnTrackingFailed(TrackingMode failed, string message)
        {
            if (mode == failed) SetMode(Fallback(failed));
            Toast(message, 6f);
        }

        public void OnVuforiaFailed(string message)
        {
            if (mode != null && mode.UsesVuforia) SetMode(Fallback(mode));
            Toast(message + " Switched to " + (mode != null ? mode.Short : "3D") + " mode.", 6f);
        }

        // ------------------------------------------------------------------ HUD
        Button HudIconButton(Transform parent, string icon, System.Action onClick, out Image iconImg, float w = 100)
        {
            var b = UIKit.MakeButton(parent, "", Theme.HudButton, onClick, 24, 96);
            UIKit.LE(b, 96, 96, -1, w);
            iconImg = UIKit.MakeIcon(b.transform, icon, Color.white, 46);
            iconImg.rectTransform.anchorMin = iconImg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            return b;
        }

        Text[] vitalVals;
        Widgets.Segmented tabsSeg;

        void BuildHud()
        {
            canvas = UIKit.CreateCanvas("HUD", 10);
            safe = UIKit.SafeRoot(canvas);

            // --- top row: Back pill | flash | labels | "AR-mode: Surface v" pill
            var topRow = UIKit.NewRect("TopRow", safe);
            UIKit.Anchor(topRow, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -116), new Vector2(-18, -18));
            var h = UIKit.HStack(topRow, 12);
            h.childForceExpandWidth = false; h.childAlignment = TextAnchor.MiddleLeft;
            var back = UIKit.MakeButton(topRow, "", Theme.Hud, () => SceneManager.LoadScene("Menu"), 28, 92);
            UIKit.LE(back, 92, 92, -1, 190);
            var bh = back.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.padding = new RectOffset(22, 26, 0, 0); bh.spacing = 10; bh.childAlignment = TextAnchor.MiddleCenter;
            bh.childControlWidth = bh.childControlHeight = true; bh.childForceExpandWidth = bh.childForceExpandHeight = false;
            Destroy(back.Label().gameObject);
            UIKit.MakeIcon(back.transform, "back", Color.white, 36);
            UIKit.MakeText(back.transform, "Back", 30, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
            torchBtn = HudIconButton(topRow, "flash", ToggleTorch, out torchIcon, 92);
            HudIconButton(topRow, "tag", ToggleLabels, out labelsIcon, 92);
            var spacer = UIKit.NewRect("Flex", topRow); UIKit.LE(spacer, -1, -1, -1, -1, 1);
            var modeBtn = UIKit.MakeButton(topRow, "", Theme.Hud, CycleMode, 26, 92);
            UIKit.LE(modeBtn, 92, 92, -1, 330);
            var mh = modeBtn.gameObject.AddComponent<HorizontalLayoutGroup>();
            mh.padding = new RectOffset(22, 22, 0, 0); mh.spacing = 10; mh.childAlignment = TextAnchor.MiddleCenter;
            mh.childControlWidth = true; mh.childControlHeight = true; mh.childForceExpandWidth = false; mh.childForceExpandHeight = false;
            Destroy(modeBtn.Label().gameObject);
            UIKit.MakeIcon(modeBtn.transform, "layers", Color.white, 36);
            modeLabel = UIKit.MakeText(modeBtn.transform, "AR-mode: 3D", 27, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
            var chev = UIKit.MakeIcon(modeBtn.transform, "chevron", new Color(1, 1, 1, 0.7f), 26);
            chev.rectTransform.localEulerAngles = new Vector3(0, 0, -90);

            // --- vitals monitor card: labelled columns with coloured values + live ECG
            var strip = UIKit.MakeImage(safe, Theme.Hud, true, "Vitals");
            UIKit.Anchor(strip.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -268), new Vector2(-18, -130));
            var cols = UIKit.NewRect("Cols", strip.transform);
            UIKit.Stretch(cols, 14, 10, 14, 26);
            UIKit.HStack(cols, 4);
            string[] names = { "HR", "BP", "SpO2", "RR", "Urine", "Reflex" };
            Color[] colours = { Theme.VitalGreen, Theme.VitalRed, Theme.VitalCyan, Theme.VitalYellow, Color.white, Theme.VitalYellow };
            vitalVals = new Text[names.Length];
            for (int k = 0; k < names.Length; k++)
            {
                var c = UIKit.NewRect(names[k], cols);
                UIKit.VStack(c, 0, 0, TextAnchor.UpperCenter);
                UIKit.LE(c, -1, -1, -1, -1, k == 1 ? 1.5f : 1f);
                var l = UIKit.MakeText(c, names[k], 21, new Color(1, 1, 1, 0.6f), FontStyle.Normal, TextAnchor.MiddleCenter);
                vitalVals[k] = UIKit.MakeText(c, "--", 38, colours[k], FontStyle.Bold, TextAnchor.MiddleCenter);
                vitalVals[k].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            var ecgRt = UIKit.NewRect("ECG", strip.transform);
            UIKit.Anchor(ecgRt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 4), new Vector2(-20, 30));
            stripEcg = ecgRt.gameObject.AddComponent<EcgGraphic>();
            stripEcg.color = new Color(0.23f, 0.89f, 0.48f, 0.8f); stripEcg.Thickness = 2.5f; stripEcg.raycastTarget = false;
            vitalsText = UIKit.MakeText(strip.transform, "", 20, new Color(1, 1, 1, 0.7f), FontStyle.Normal, TextAnchor.LowerRight);
            UIKit.Stretch(vitalsText.rectTransform, 20, 0, 24, 2);
            vitalsText.gameObject.SetActive(false);

            // --- right-side tools: recenter, +, -
            var tools = UIKit.NewRect("Tools", safe);
            UIKit.Anchor(tools, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-104, -600), new Vector2(-18, -290));
            UIKit.VStack(tools, 14, 0);
            var recenter = UIKit.MakeButton(tools, "", Theme.Hud, () => mode?.Recenter(), 26, 86);
            var ri = UIKit.MakeIcon(recenter.transform, "recenter", Color.white, 44);
            ri.rectTransform.anchorMin = ri.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            UIKit.MakeButton(tools, "+", Theme.Hud, () => ResizeWard(1.25f), 48, 86, Color.white);
            UIKit.MakeButton(tools, "-", Theme.Hud, () => ResizeWard(0.8f), 48, 86, Color.white);

            // --- hint pill (sits just above the sheet)
            var hint = UIKit.MakeImage(safe, Theme.Hud, true, "Hint");
            hintBox = hint.rectTransform;
            hintText = UIKit.MakeText(hint.transform, "", 27, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Stretch(hintText.rectTransform, 26, 6, 26, 6);
            Pulse.On(hint, 0.012f, 1.2f);

            // --- toast
            var toast = UIKit.MakeImage(safe, new Color(0.05f, 0.09f, 0.14f, 0.94f), true, "Toast");
            toastBox = toast.rectTransform;
            toastText = UIKit.MakeText(toast.transform, "", 28, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Stretch(toastText.rectTransform, 24, 8, 24, 8);
            toastBox.gameObject.SetActive(false);

            // --- bottom sheet: handle, segmented tabs, scrolling content
            var shadow = UIKit.NewRect("Shadow", safe).gameObject.AddComponent<Image>();
            shadow.sprite = UIKit.ShadowSprite; shadow.type = Image.Type.Sliced; shadow.color = new Color(1, 1, 1, 0.6f); shadow.raycastTarget = false;
            sheetShadow = shadow.rectTransform;
            var sh = UIKit.MakeImage(safe, Theme.Panel, true, "Sheet");
            sheet = sh.rectTransform;
            var collapse = UIKit.MakeButton(sheet, "", new Color(1, 1, 1, 0.01f), ToggleSheet, 24, 54);
            var crt = (RectTransform)collapse.transform;
            UIKit.Anchor(crt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -54), Vector2.zero);
            var handle = UIKit.MakeImage(crt, Theme.Border, true, "Handle");
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(0.5f, 0.55f);
            handle.rectTransform.sizeDelta = new Vector2(110, 12);
            collapseLabel = UIKit.MakeText(crt, "", 1, Theme.SubText);
            collapseLabel.gameObject.SetActive(false);
            var segHolder = UIKit.NewRect("Tabs", sheet);
            UIKit.Anchor(segHolder, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -142), new Vector2(-30, -54));
            UIKit.VStack(segHolder, 0, 0);
            tabsSeg = Widgets.MakeSegmented(segHolder, new[] { "Clinical Scenario", "Toxicity Explorer" }, k => SetTab(k == 0 ? Tab.Scenario : Tab.Explorer), 84);
            sheetContent = UIKit.MakeScroll(sheet, out sheetScroll, 22, 30);
            UIKit.Stretch((RectTransform)sheetScroll.transform, 0, 150, 0, 0);
            LayoutSheet(true);
        }

        void ResizeWard(float f)
        {
            if (mode == null || !mode.Resize(f))
                Toast(mode is ARFoundationMode ? "Place the bed first: tap the ring on the surface." : "Point the camera at the marker first.", 2.5f);
        }

        Image labelsIcon;
        EcgGraphic stripEcg;
        RectTransform sheetShadow;
        const float SheetTop = 0.47f, SheetCollapsed = 0.045f;

        void LayoutSheet(bool instant = false)
        {
            var max = new Vector2(1, sheetCollapsed ? SheetCollapsed : SheetTop);
            if (instant) { sheet.anchorMin = Vector2.zero; sheet.anchorMax = max; sheet.offsetMin = sheet.offsetMax = Vector2.zero; }
            else { sheet.offsetMin = sheet.offsetMax = Vector2.zero; AnchorTween.To(sheet, Vector2.zero, max, 0.32f); }
            sheetScroll.gameObject.SetActive(!sheetCollapsed);
            collapseLabel.text = sheetCollapsed ? "show panel" : "hide panel";
            float y = sheetCollapsed ? SheetCollapsed : SheetTop;
            UIKit.Anchor(toastBox, new Vector2(0, y), new Vector2(1, y), new Vector2(60, 130), new Vector2(-60, 236));
            UIKit.Anchor(hintBox, new Vector2(0, y), new Vector2(1, y), new Vector2(70, 24), new Vector2(-70, 112));
        }

        void LateUpdate()
        {
            if (sheetShadow != null && sheet != null)
            {
                sheetShadow.anchorMin = sheet.anchorMin; sheetShadow.anchorMax = sheet.anchorMax;
                sheetShadow.offsetMin = new Vector2(-30, -30); sheetShadow.offsetMax = new Vector2(30, 24);
            }
            if (stripEcg != null && monitor != null)
            {
                stripEcg.Bpm = Mathf.Max(30f, monitor.Current.hr);
                stripEcg.Flat = monitor.Target.hr <= 0.5f;
            }
        }

        void ToggleSheet() { sheetCollapsed = !sheetCollapsed; LayoutSheet(); Haptics.Tap(); }

        readonly SwipeTracker swipe = new SwipeTracker();

        /// <summary>Swipe gestures: on the panel left/right switches tabs, down/up hides/shows it.</summary>
        void HandleSwipes()
        {
            var dir = swipe.Update();
            if (dir == SwipeDir.None || modeMenu != null) return;
            var s0 = swipe.StartPos;
            float sheetTopPx = Screen.height * (sheetCollapsed ? SheetCollapsed : SheetTop) + 20;
            bool onSheet = s0.y <= sheetTopPx;
            if (!onSheet) { if (dir == SwipeDir.Up && sheetCollapsed && s0.y < Screen.height * 0.3f) ToggleSheet(); return; }
            switch (dir)
            {
                case SwipeDir.Left: if (tab == Tab.Scenario) { SetTab(Tab.Explorer); Haptics.Tap(); } break;
                case SwipeDir.Right: if (tab == Tab.Explorer) { SetTab(Tab.Scenario); Haptics.Tap(); } break;
                case SwipeDir.Down:
                    // only collapse when the list is scrolled to the top (otherwise it is a scroll gesture)
                    if (!sheetCollapsed && (sheetScroll.verticalNormalizedPosition > 0.98f || s0.y > sheetTopPx - Screen.height * 0.12f)) ToggleSheet();
                    break;
                case SwipeDir.Up: if (sheetCollapsed) ToggleSheet(); break;
            }
        }

        void ToggleLabels()
        {
            labelsOn = !labelsOn;
            ward.SetLabelsVisible(labelsOn);
            if (labelsIcon != null) labelsIcon.color = labelsOn ? Color.white : new Color(1, 1, 1, 0.35f);
            Toast(labelsOn ? "Labels shown" : "Labels hidden", 1.2f);
        }

        public void Toast(string msg, float secs = 2.5f)
        {
            toastText.text = msg;
            toastBox.gameObject.SetActive(true);
            AppearAnim.Apply(toastBox, 0f, 0.9f);
            toastUntil = Time.time + secs;
        }

        void SetTab(Tab t)
        {
            var prevTab = tab;
            tab = t;
            if (tabsSeg != null && tabsSeg.Index != (t == Tab.Scenario ? 0 : 1)) tabsSeg.Select(t == Tab.Scenario ? 0 : 1);
            if (sheetCollapsed) ToggleSheet();
            if (t == Tab.Scenario)
            {
                monitor.ShowMgOnMonitor = false;
                for (int k = Mathf.Min(stepIndex, steps.Length - 1); k >= 0; k--)
                    if (steps[k].Vitals.HasValue) { monitor.SetTarget(steps[k].Vitals.Value); break; }
                if (debriefSaved) ShowDebriefView(); else ShowStep(stepIndex, false);
            }
            else ShowExplorer();
            if (prevTab != t) PageSlide.In(sheetContent, t == Tab.Explorer ? 1 : -1);
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            // hint
            string hint = mode != null ? mode.Hint : null;
            if (string.IsNullOrEmpty(hint) && mode != null && !(mode is FreeOrbitMode) && MotionSense.Intensity > 0.55f)
                hint = "Hold the phone steady for better tracking";
            hintBox.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            if (!string.IsNullOrEmpty(hint)) hintText.text = hint;
            if (toastBox.gameObject.activeSelf && Time.time > toastUntil) toastBox.gameObject.SetActive(false);

            UpdateVitalsStrip();
            HandleSwipes();

            // taps on 3D objects
            if (PointerInput.Down(out var pos) && !UIKit.OverUI(pos))
            {
                var cam = mode != null ? mode.Cam : simCamera;
                if (cam != null && cam.isActiveAndEnabled)
                {
                    var ray = cam.ScreenPointToRay(pos);
                    if (Physics.Raycast(ray, out var hit, 100f))
                    {
                        var it = hit.collider.GetComponentInParent<Interactable>();
                        if (it != null) OnItemTapped(it.Id);
                    }
                }
            }
        }

        float nextSeizureBuzz;

        void UpdateVitalsStrip()
        {
            if (ward.Seizing && Time.time > nextSeizureBuzz) { Haptics.Seizure(); nextSeizureBuzz = Time.time + 1.6f; }
            var c = monitor.Current;
            bool explorer = tab == Tab.Explorer;
            bool alive = explorer || monitor.Target.hr > 0;
            const string Q = "?";
            vitalVals[0].text = alive ? $"{c.hr:0}" : "--";
            vitalVals[1].text = alive ? $"{c.sys:0}/{c.dia:0}" : "--";
            vitalVals[2].text = alive ? $"{c.spo2:0}%" : "--";
            vitalVals[3].text = explorer || assessed.Contains("chest") ? $"{c.rr:0}" : Q;
            vitalVals[4].text = explorer || assessed.Contains("urine") ? $"{c.urine:0}<size=20>mL/h</size>" : Q;
            int rf = monitor.Target.reflex;
            vitalVals[5].text = explorer || assessed.Contains("knee") ? (rf == 0 ? "0" : rf + "+") : Q;
            vitalVals[1].color = c.sys >= 160 || c.dia >= 110 ? Theme.VitalRed : Color.Lerp(Theme.VitalRed, Color.white, 0.4f);
            vitalVals[2].color = c.spo2 < 94 ? Theme.VitalRed : Theme.VitalCyan;
            vitalVals[3].color = (explorer || assessed.Contains("chest")) && c.rr < 16 ? Theme.VitalRed : Theme.VitalYellow;
            vitalVals[5].color = (explorer || assessed.Contains("knee")) && rf == 0 ? Theme.VitalRed : Theme.VitalYellow;
        }

        // ------------------------------------------------------------------ taps
        void OnItemTapped(string id)
        {
            Haptics.Click();
            var step = tab == Tab.Scenario && stepIndex < steps.Length ? steps[stepIndex] : null;
            bool isTapStep = step != null && step.Kind == StepKind.Tap && !stepDone;

            if (isTapStep && step.Targets.Contains(id))
            {
                if (tapped.Contains(id)) return;
                if (step.Ordered)
                {
                    var next = step.Targets.First(t => !tapped.Contains(t));
                    if (next != id)
                    {
                        attempts++;
                        Toast("Not yet - follow the checklist order. Next: " + ward.Items[next].DisplayName, 3f);
                        return;
                    }
                }
                tapped.Add(id);
                PerformAction(id, step);
                UpdateTapChecklist(step);
                if (step.Targets.All(t => tapped.Contains(t))) StartCoroutine(CompleteTapStep(step));
                return;
            }

            // free exploration: assessments always allowed
            switch (id)
            {
                case "knee":
                case "chest":
                case "urine":
                case "monitor":
                    PerformAction(id, step);
                    break;
                default:
                    if (ward.Items.TryGetValue(id, out var it)) Toast(it.DisplayName + (isTapStep ? " - not needed for this step" : ""));
                    if (isTapStep) attempts++;
                    break;
            }
        }

        void PerformAction(string id, ScenarioStep step)
        {
            var v = monitor.Target;
            switch (id)
            {
                case "knee":
                    ward.Kick(v.reflex);
                    assessed.Add("knee");
                    Toast("Patellar reflex: " + Vitals.ReflexText(v.reflex), 3f);
                    break;
                case "chest":
                    assessed.Add("chest");
                    Toast($"Respirations counted for 1 minute: {v.rr:0}/min", 3f);
                    break;
                case "urine":
                    assessed.Add("urine");
                    Toast($"Urine output last hour: {v.urine:0} mL", 3f);
                    break;
                case "monitor":
                    assessed.Add("monitor");
                    Toast($"Monitor: HR {v.hr:0}, BP {v.sys:0}/{v.dia:0}, SpO2 {v.spo2:0}%", 3f);
                    break;
                case "water":
                    syringeFill = 12f / 20f; ward.SetSyringeFill(syringeFill);
                    Toast("Drew 12 mL sterile water into the 20 mL syringe");
                    break;
                case "mgso4":
                    if (step != null && step.Id == "S10") { syringeFill = 10f / 20f; Toast("Drew 10 mL MgSO4 50% (5 g)"); }
                    else { syringeFill = 1f; Toast("Added 8 mL MgSO4 50% -> 20 mL of 20% (4 g)"); }
                    ward.SetSyringeFill(syringeFill);
                    break;
                case "lignocaine":
                    syringeFill = 11f / 20f; ward.SetSyringeFill(syringeFill);
                    Toast("Added 1 mL lignocaine 2% (5 g MgSO4 + 1 mL lignocaine)");
                    break;
                case "syringe":
                    Toast("Labelled: MgSO4 4 g / 20 mL (20%)");
                    break;
                case "pump":
                    pumpRunning = false;
                    ward.SetPump(pumpRate, false);
                    Toast("Infusion STOPPED");
                    break;
                case "o2":
                    ward.SetOxygen(true);
                    var t = monitor.Target; t.spo2 = Mathf.Max(t.spo2, 94f); monitor.SetTarget(t);
                    Toast("Oxygen applied by face mask");
                    break;
                case "cagluc":
                    Toast("Calcium gluconate 10%, 10 mL (1 g) drawn up");
                    break;
                case "hammer":
                    Toast("Reflex hammer - tap the KNEE to test the patellar reflex");
                    break;
            }
        }

        IEnumerator CompleteTapStep(ScenarioStep step)
        {
            stepDone = true;
            ward.Highlight(null);
            yield return new WaitForSeconds(0.8f);
            float pts = Mathf.Max(0f, step.Points - Mathf.Max(0, attempts) * 2f);
            Record(step, pts, attempts == 0);
            ShowFeedback(true, step, pts);
        }

        // ------------------------------------------------------------------ scenario
        void StartScenario()
        {
            stepIndex = 0;
            debriefSaved = false;
            records.Clear();
            sessionStart = Time.time;
            sessionId = ResultsStore.NewSessionId();
            ShowStep(0, true);
        }

        void ShowStep(int i, bool enter)
        {
            UIKit.Clear(sheetContent);
            if (i >= steps.Length) { ShowDebrief(); return; }
            var step = steps[i];
            if (enter)
            {
                stepIndex = i;
                stepStart = Time.time;
                attempts = 0;
                stepDone = false;
                tapped.Clear();
                multiSel.Clear();
                if (step.Vitals.HasValue) monitor.SetTarget(step.Vitals.Value, i == 0);
                ApplyEvent(step.OnEnter);
            }
            ward.Highlight(step.Kind == StepKind.Tap && !stepDone ? step.Targets : null);

            // header: "Step 5 of 22 / Phase: NOTICING" + score pill
            var head = UIKit.NewRect("Header", sheetContent);
            var hh = UIKit.HStack(head, 12);
            hh.childForceExpandWidth = false; hh.childAlignment = TextAnchor.MiddleLeft;
            var col = UIKit.NewRect("Col", head);
            UIKit.VStack(col, 2, 0);
            UIKit.LE(col, -1, -1, -1, -1, 1);
            UIKit.MakeText(col, $"Step {i + 1} of {steps.Length}", 38, Theme.Text, FontStyle.Bold);
            UIKit.MakeText(col, $"Phase: <color={Theme.Html(PhaseColor(step.Phase))}><b>{step.Phase.ToUpperInvariant()}</b></color>", 25, Theme.SubText);
            UIKit.Chip(head, $"Score: {records.Sum(r => r.Points):0} pts", Theme.CardLight, 25);
            var prog = ProgressBar.Make(sheetContent, (i + 1f) / steps.Length, PhaseColor(step.Phase), 8);

            UIKit.MakeText(sheetContent, step.Title, 40, Theme.Text, FontStyle.Bold);
            UIKit.MakeText(sheetContent, step.Narrative, 31, Theme.Text);

            switch (step.Kind)
            {
                case StepKind.Info:
                    break;
                case StepKind.Single:
                {
                    var opts = new List<Widgets.Option>();
                    int chosen = -1;
                    for (int k = 0; k < step.Options.Length; k++)
                    {
                        int idx = k;
                        opts.Add(Widgets.MakeOption(sheetContent, step.Options[k], false, () =>
                        {
                            if (stepDone) return;
                            chosen = idx;
                            for (int q = 0; q < opts.Count; q++) if (opts[q].Button.interactable) opts[q].SetSelected(q == idx);
                        }));
                    }
                    UIKit.MakeButton(sheetContent, "Submit", Theme.Navy, () =>
                    {
                        if (stepDone) return;
                        if (chosen < 0) { Toast("Select an answer first.", 1.5f); return; }
                        AnswerSingle(step, chosen, opts);
                    }, 34, 104);
                    break;
                }
                case StepKind.Multi:
                {
                    var opts = new List<Widgets.Option>();
                    for (int k = 0; k < step.Options.Length; k++)
                    {
                        int idx = k;
                        opts.Add(Widgets.MakeOption(sheetContent, step.Options[k], true, () =>
                        {
                            if (stepDone) return;
                            if (!multiSel.Add(idx)) multiSel.Remove(idx);
                            opts[idx].SetSelected(multiSel.Contains(idx));
                        }));
                    }
                    UIKit.MakeButton(sheetContent, "Submit", Theme.Navy, () => AnswerMulti(step, opts), 34, 104);
                    break;
                }
                case StepKind.Numeric:
                    Widgets.MakeKeypad(sheetContent, step.Unit, v => AnswerNumeric(step, v));
                    break;
                case StepKind.Tap:
                    tapChecklist = UIKit.MakeText(sheetContent, "", 32, Theme.Orange, FontStyle.Bold);
                    UpdateTapChecklist(step);
                    UIKit.MakeText(sheetContent, "<i>Tip: highlighted (flashing yellow) objects are the ones to tap. Hide the panel to see more of the scene.</i>", 26, Theme.SubText);
                    break;
                case StepKind.Pump:
                    BuildPumpUI(step);
                    break;
            }

            feedbackSlot = UIKit.NewRect("Feedback", sheetContent);
            UIKit.VStack(feedbackSlot, 18, 0);

            if (step.Kind == StepKind.Info)
            {
                var cta = UIKit.MakeButton(feedbackSlot, i == 0 ? "Start the scenario" : "Continue", Theme.Primary, NextStep, 36, 120);
                if (i == 0) Pulse.On(cta, 0.025f, 1.4f);
            }
            else if (stepDone)
                ShowFeedback(true, step, records.Count > 0 ? records[records.Count - 1].Points : 0);

            sheetScroll.verticalNormalizedPosition = 1f;
            if (enter) AppearAnim.Stagger(sheetContent, 0.035f);
        }

        void UpdateTapChecklist(ScenarioStep step)
        {
            if (tapChecklist == null) return;
            var lines = step.Targets.Select(t => (tapped.Contains(t) ? "<color=#1FAA6B>[done]</color>  " : "[  ]  ") + ward.Items[t].DisplayName);
            tapChecklist.text = string.Join("\n", lines);
        }

        void BuildPumpUI(ScenarioStep step)
        {
            var disp = UIKit.Card(sheetContent, new Color(0.04f, 0.08f, 0.1f));
            var rateText = UIKit.MakeText(disp, "", 54, new Color(0.4f, 1f, 0.75f), FontStyle.Bold, TextAnchor.MiddleCenter);
            System.Action refresh = () =>
            {
                rateText.text = $"{pumpRate:0.0} mL/h";
                ward.SetPump(pumpRate, pumpRunning);
            };
            refresh();
            var row = UIKit.Row(sheetContent, 100, 12);
            foreach (var d in new[] { -10f, -1f, -0.5f, 0.5f, 1f, 10f })
            {
                float delta = d;
                UIKit.MakeButton(row, (d > 0 ? "+" : "") + d.ToString("0.#", CultureInfo.InvariantCulture), Theme.Neutral, () =>
                {
                    if (stepDone) return;
                    pumpRate = Mathf.Clamp(pumpRate + delta, 0f, 999f);
                    refresh();
                }, 32, 100);
            }
            UIKit.MakeButton(sheetContent, "START infusion", Theme.Success, () =>
            {
                if (stepDone) return;
                attempts++;
                bool ok = Mathf.Abs(pumpRate - step.Answer) <= 0.01f;
                if (ok)
                {
                    pumpRunning = true; refresh();
                    stepDone = true;
                    float pts = attempts == 1 ? step.Points : attempts == 2 ? step.Points * 0.5f : 0f;
                    Record(step, pts, attempts == 1);
                    ShowFeedback(true, step, pts);
                }
                else if (attempts >= 3)
                {
                    pumpRate = step.Answer; pumpRunning = true; refresh();
                    stepDone = true;
                    Record(step, 0, false);
                    ShowFeedback(false, step, 0, "The correct rate is 25 mL/h - the pump has been corrected by your colleague during the double-check.");
                }
                else
                {
                    ShowRetry($"The pump would deliver {pumpRate * 0.04f:0.##} g/h. Recalculate: g/h divided by bag concentration (g/mL).");
                }
            }, 36, 110);
        }

        void AnswerSingle(ScenarioStep step, int idx, List<Widgets.Option> opts)
        {
            if (stepDone) return;
            attempts++;
            if (idx == step.Correct[0])
            {
                stepDone = true;
                opts[idx].SetResult(1, true);
                foreach (var o in opts) o.Button.interactable = false;
                float pts = attempts == 1 ? step.Points : attempts == 2 ? step.Points * 0.5f : 0f;
                Record(step, pts, attempts == 1);
                ShowFeedback(true, step, pts);
            }
            else if (attempts >= 2)
            {
                stepDone = true;
                for (int q = 0; q < opts.Count; q++) opts[q].SetResult(q == step.Correct[0] ? 1 : q == idx ? -1 : 0, q == idx);
                Record(step, 0, false);
                ShowFeedback(false, step, 0, "Correct answer: " + step.Options[step.Correct[0]]);
            }
            else
            {
                opts[idx].SetResult(-1, true);
                opts[idx].Button.interactable = false;
                ShowRetry("Not quite - think again and choose another answer.");
            }
        }

        void AnswerMulti(ScenarioStep step, List<Widgets.Option> buttons)
        {
            if (stepDone) return;
            attempts++;
            stepDone = true;
            var correct = new HashSet<int>(step.Correct);
            int hits = multiSel.Count(i => correct.Contains(i));
            int wrong = multiSel.Count(i => !correct.Contains(i));
            float pts = Mathf.Clamp(step.Points * (hits - wrong) / correct.Count, 0f, step.Points);
            for (int i = 0; i < buttons.Count; i++)
            {
                bool c = correct.Contains(i), s = multiSel.Contains(i);
                buttons[i].SetResult(c ? 1 : s ? -1 : 0, s);
            }
            Record(step, pts, hits == correct.Count && wrong == 0);
            ShowFeedback(hits == correct.Count && wrong == 0, step, pts,
                $"You selected {hits} of {correct.Count} correct actions" + (wrong > 0 ? $" and {wrong} incorrect." : "."));
        }

        void AnswerNumeric(ScenarioStep step, string text)
        {
            if (stepDone) return;
            if (!float.TryParse((text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            { ShowRetry("Enter a number."); return; }
            attempts++;
            bool ok = Mathf.Abs(val - step.Answer) <= Mathf.Max(step.Tolerance, step.Answer * 0.01f);
            if (ok)
            {
                stepDone = true;
                float pts = attempts == 1 ? step.Points : attempts == 2 ? step.Points * 0.5f : 0f;
                Record(step, pts, attempts == 1);
                ShowFeedback(true, step, pts);
            }
            else if (attempts >= 2)
            {
                stepDone = true;
                Record(step, 0, false);
                ShowFeedback(false, step, 0, $"Correct answer: {step.Answer:0.##} {step.Unit}");
            }
            else ShowRetry($"{val:0.##} {step.Unit} is not correct. Try again.");
        }

        void ShowRetry(string msg)
        {
            Haptics.Error();
            UIKit.Clear(feedbackSlot);
            var card = UIKit.Card(feedbackSlot, Theme.WarnBg);
            UIKit.MakeText(card, msg, 30, Theme.AccentText, FontStyle.Bold);
            AppearAnim.Apply(card.parent, 0f, 0.92f);
        }

        void ShowFeedback(bool good, ScenarioStep step, float pts, string extra = null)
        {
            if (good) Haptics.Success(); else Haptics.Error();
            UIKit.Clear(feedbackSlot);
            var card = UIKit.Card(feedbackSlot, good ? Theme.SuccessBg : Theme.DangerBg);
            var hdr = UIKit.Row(card, 64, 14);
            var hh = hdr.GetComponent<HorizontalLayoutGroup>(); hh.childForceExpandWidth = false; hh.childAlignment = TextAnchor.MiddleLeft;
            UIKit.IconBadge(hdr, good ? "check" : "info", good ? Theme.Success : Theme.Danger, 60);
            UIKit.MakeText(hdr, (good ? "Correct" : "Review") + $"  (+{pts:0.#} pts)", 36, good ? Theme.Success : Theme.Danger, FontStyle.Bold, TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(extra)) UIKit.MakeText(card, extra, 30, Theme.Text, FontStyle.Bold);
            AppearAnim.Apply(card.parent, 0f, 0.92f);
            UIKit.MakeText(card, step.Rationale, 30, Theme.Text);
            if (!string.IsNullOrEmpty(step.Source)) UIKit.MakeText(card, "<i>Source: " + step.Source + "</i>", 24, Theme.SubText);
            UIKit.MakeButton(feedbackSlot, stepIndex == steps.Length - 1 ? "Finish & debrief" : "Next step", Theme.Primary, NextStep, 36, 120);
            StartCoroutine(ScrollToBottom());
        }

        IEnumerator ScrollToBottom()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            sheetScroll.verticalNormalizedPosition = 0f;
        }

        void NextStep()
        {
            var step = steps[stepIndex];
            if (step.Kind == StepKind.Info && !records.Any(r => r.StepId == step.Id)) Record(step, 0, true);
            ShowStep(stepIndex + 1, true);
            PageSlide.In(sheetContent, 1);
        }

        void Record(ScenarioStep step, float pts, bool firstTry)
        {
            records.RemoveAll(r => r.StepId == step.Id);
            records.Add(new StepRecord
            {
                StepId = step.Id, Phase = step.Phase, Title = step.Title, Points = pts, MaxPoints = step.Points,
                Attempts = Mathf.Max(1, attempts), FirstTryCorrect = firstTry, Seconds = Time.time - stepStart
            });
        }

        void ApplyEvent(string ev)
        {
            switch (ev)
            {
                case "reset":
                    ward.Seizing = false;
                    ward.SetOxygen(false);
                    pumpRate = 0; pumpRunning = false; ward.SetPump(0, false);
                    syringeFill = 0; ward.SetSyringeFill(0);
                    assessed.Clear();
                    break;
                case "seizure_on": ward.Seizing = true; Haptics.Seizure(); nextSeizureBuzz = Time.time + 1.6f; break;
                case "seizure_off": ward.Seizing = false; break;
                case "seizure_brief": StartCoroutine(BriefSeizure()); break;
                case "syringe_empty": syringeFill = 0; ward.SetSyringeFill(0); break;
                case "pump_reset": pumpRate = 0; pumpRunning = false; ward.SetPump(0, false); break;
                case "timeskip":
                    assessed.Clear();
                    Toast("4 HOURS LATER...", 3.5f);
                    break;
                case "recovery":
                    ward.SetOxygen(true);
                    break;
            }
        }

        IEnumerator BriefSeizure()
        {
            ward.Seizing = true;
            var before = monitor.Target;
            var t = before; t.hr = 126; t.spo2 = 92; t.loc = "Seizing"; monitor.SetTarget(t);
            yield return new WaitForSeconds(4f);
            ward.Seizing = false;
            before.loc = "Post-ictal, drowsy";
            monitor.SetTarget(before);
        }

        static Color PhaseColor(string p)
        {
            switch (p)
            {
                case ScenarioData.Noticing: return Theme.Info;
                case ScenarioData.Interpreting: return Theme.Purple;
                case ScenarioData.Responding: return Theme.Orange;
                case ScenarioData.Reflecting: return Theme.Success;
                default: return Theme.SubText;
            }
        }

        // ------------------------------------------------------------------ debrief
        bool debriefSaved;
        void ShowDebrief()
        {
            if (!debriefSaved)
            {
                debriefSaved = true;
                SaveSession();
            }
            ShowDebriefView();
        }

        void SaveSession()
        {
            float total = records.Sum(r => r.Points), max = steps.Sum(s => s.Points);
            float dur = Time.time - sessionStart;
            var phases = new[] { ScenarioData.Noticing, ScenarioData.Interpreting, ScenarioData.Responding, ScenarioData.Reflecting };
            var details = string.Join("; ", phases.Select(p =>
            {
                float s = records.Where(r => r.Phase == p).Sum(r => r.Points), m = steps.Where(x => x.Phase == p).Sum(x => x.Points);
                return $"{p} {s:0}/{m:0}";
            }));
            ResultsStore.Save(new SessionRecord
            {
                SessionId = sessionId, Activity = "AR Scenario", Mode = mode != null ? mode.Short : "",
                Score = total, MaxScore = max, DurationSec = dur, Details = details
            }, records);
            sessionEnd = Time.time;
        }

        float sessionEnd;
        void ShowDebriefView()
        {
            UIKit.Clear(sheetContent);
            ward.Highlight(null);
            float total = records.Sum(r => r.Points), max = steps.Sum(s => s.Points);
            float dur = sessionEnd - sessionStart;
            var phases = new[] { ScenarioData.Noticing, ScenarioData.Interpreting, ScenarioData.Responding, ScenarioData.Reflecting };

            UIKit.MakeText(sheetContent, "Test Debrief & Results", 44, Theme.Text, FontStyle.Bold);
            float pct = max > 0 ? total / max : 0;
            var top = UIKit.Card(sheetContent, Theme.Surface, 30, 10);
            var tr = UIKit.Row(top, 300, 30);
            var trh = tr.GetComponent<HorizontalLayoutGroup>(); trh.childForceExpandWidth = false; trh.childAlignment = TextAnchor.MiddleLeft;
            Widgets.Donut(tr, pct, pct >= 0.75f ? Theme.Success : pct >= 0.5f ? Theme.Accent : Theme.Danger, 280, "Score");
            var bars = UIKit.NewRect("Phases", tr);
            UIKit.VStack(bars, 8, 0, TextAnchor.MiddleLeft);
            UIKit.LE(bars, -1, -1, -1, -1, 1);
            foreach (var p in phases)
            {
                float s = records.Where(r => r.Phase == p).Sum(r => r.Points), m = steps.Where(x => x.Phase == p).Sum(x => x.Points);
                UIKit.MakeText(bars, $"{p}  <color={Theme.Html(Theme.SubText)}>{s:0}/{m:0}</color>", 26, Theme.Text, FontStyle.Bold);
                ProgressBar.Make(bars, m > 0 ? s / m : 0, PhaseColor(p), 12);
            }
            var scoreText = UIKit.MakeText(top, "", 30, Theme.Text, FontStyle.Bold);
            CountUp.On(scoreText, total, "{0:0} / " + max.ToString("0") + " points");
            UIKit.MakeText(top, $"Time {dur / 60f:0.0} min  |  {(mode != null ? mode.Label : "-")}  |  {Participant.Display} ({Participant.Group})", 23, Theme.SubText);
            if (pct >= 0.75f) Haptics.Success();

            UIKit.MakeText(sheetContent, "Trial results", 34, Theme.Text, FontStyle.Bold);
            foreach (var s in steps.Where(x => x.Points > 0))
            {
                var r = records.FirstOrDefault(x => x.StepId == s.Id);
                float p = r != null ? r.Points : 0, f = s.Points > 0 ? p / s.Points : 0;
                var row = UIKit.Card(sheetContent, Theme.Surface, 20, 4);
                var rr = UIKit.Row(row, 84, 18);
                var rrh = rr.GetComponent<HorizontalLayoutGroup>(); rrh.childForceExpandWidth = false; rrh.childAlignment = TextAnchor.MiddleLeft;
                var c = f >= 1 ? Theme.Success : f > 0 ? Theme.Accent : Theme.Danger;
                UIKit.IconBadge(rr, f >= 1 ? "check" : f > 0 ? "info" : "cross", c, 70);
                var colm = UIKit.NewRect("Col", rr);
                UIKit.VStack(colm, 6, 0);
                UIKit.LE(colm, -1, -1, -1, -1, 1);
                UIKit.MakeText(colm, $"{s.Title}", 28, Theme.Text, FontStyle.Bold);
                var pr = UIKit.Row(colm, 26, 12);
                var prh = pr.GetComponent<HorizontalLayoutGroup>(); prh.childForceExpandWidth = false; prh.childAlignment = TextAnchor.MiddleLeft;
                var bar = ProgressBar.Make(pr, f, c, 10);
                UIKit.LE(bar, 10, 10, -1, -1, 1);
                UIKit.MakeText(pr, $"{p:0.#}/{s.Points:0}", 22, Theme.SubText, FontStyle.Normal, TextAnchor.MiddleRight);
            }
            UIKit.MakeText(sheetContent, "Key messages: give MgSO4 for eclampsia; prepare 4 g as 20% (8 mL of 50% + 12 mL water); check RR >= 16, reflexes and urine >= 30 mL/h every hour; stop, call, oxygen and calcium gluconate 1 g slowly if toxic.", 28, Theme.SubText);
            UIKit.MakeText(sheetContent, "<i>Results saved on this device for the study.</i>", 26, Theme.SubText);
            UIKit.MakeButton(sheetContent, "Restart scenario", Theme.Navy, StartScenario, 34, 110);
            UIKit.MakeButton(sheetContent, "Back to main menu", Theme.Neutral, () => SceneManager.LoadScene("Menu"), 34, 110);
            sheetScroll.verticalNormalizedPosition = 1f;
            AppearAnim.Stagger(sheetContent, 0.03f);
        }

        // ------------------------------------------------------------------ explorer
        void ShowExplorer()
        {
            UIKit.Clear(sheetContent);
            ward.Highlight(null);
            ward.Seizing = false;
            monitor.ShowMgOnMonitor = true;

            // header row: "Serum Mg"  ...  value + conversions
            var head = UIKit.Row(sheetContent, 90, 10);
            var hh = head.GetComponent<HorizontalLayoutGroup>(); hh.childForceExpandWidth = false; hh.childAlignment = TextAnchor.MiddleLeft;
            var title = UIKit.MakeText(head, "Serum Mg", 44, Theme.Text, FontStyle.Bold, TextAnchor.MiddleLeft);
            UIKit.LE(title, -1, -1, -1, -1, 1);
            var value = UIKit.MakeText(head, "", 34, Theme.Text, FontStyle.Bold, TextAnchor.MiddleRight);
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.LE(value, -1, -1, -1, 420);

            // gradient slider: green (therapeutic) -> amber -> red (toxic)
            var sliderHolder = UIKit.NewRect("GradSlider", sheetContent);
            UIKit.LE(sliderHolder, 120, 120);
            var zone = UIKit.MakeText(sliderHolder, "Toxic", 24, Theme.Danger, FontStyle.Bold, TextAnchor.UpperCenter);
            UIKit.Anchor(zone.rectTransform, new Vector2(0.25f, 0.72f), new Vector2(0.75f, 1f), Vector2.zero, Vector2.zero);
            var trackA = UIKit.MakeImage(sliderHolder, Color.white, true, "TrackA");
            UIKit.Anchor(trackA.rectTransform, new Vector2(0, 0.42f), new Vector2(0.24f, 0.62f), new Vector2(34, 0), Vector2.zero);
            UIGradient.On(trackA, Theme.Success, Theme.Accent, true);
            var trackB = UIKit.MakeImage(sliderHolder, Color.white, true, "TrackB");
            UIKit.Anchor(trackB.rectTransform, new Vector2(0.23f, 0.42f), new Vector2(1f, 0.62f), Vector2.zero, new Vector2(-34, 0));
            UIGradient.On(trackB, Theme.Accent, Theme.Danger, true);
            foreach (var (pos, txt) in new[] { (0f, "1.7"), (0.26f, "10.5"), (1f, "35") })
            {
                var t = UIKit.MakeText(sliderHolder, txt, 22, Theme.SubText, FontStyle.Normal, TextAnchor.LowerCenter);
                UIKit.Anchor(t.rectTransform, new Vector2(pos, 0), new Vector2(pos, 0.38f), new Vector2(-50, 0), new Vector2(50, 0));
            }
            var slider = UIKit.MakeSlider(sliderHolder, 1.7f, 35f, explorerMg, null, 120);
            var srt = (RectTransform)slider.transform; UIKit.Stretch(srt);
            Destroy(srt.GetComponent<LayoutElement>());
            foreach (var img in slider.GetComponentsInChildren<Image>())
                if (img.name == "Background" || img.name == "Fill") img.color = new Color(1, 1, 1, 0);
            var handleImg = slider.handleRect.GetComponent<Image>();
            handleImg.sprite = UIKit.Circle; handleImg.type = Image.Type.Simple; handleImg.color = Color.white;
            slider.handleRect.anchorMin = new Vector2(0, 0.3f); slider.handleRect.anchorMax = new Vector2(0, 0.74f); slider.handleRect.sizeDelta = new Vector2(52, 0);
            Widgets.Border(slider.handleRect, Theme.Navy);

            // alerts card
            var alertsCard = UIKit.Card(sheetContent, Theme.Surface, 0, 0);
            alertsCard.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var alertHead = UIKit.MakeImage(alertsCard, Theme.Accent, false, "AlertHead");
            var ah = alertHead.gameObject.AddComponent<HorizontalLayoutGroup>();
            ah.padding = new RectOffset(26, 26, 16, 16); ah.spacing = 14; ah.childAlignment = TextAnchor.MiddleLeft;
            ah.childControlWidth = ah.childControlHeight = true; ah.childForceExpandWidth = ah.childForceExpandHeight = false;
            UIKit.MakeIcon(alertHead.transform, "info", Color.white, 38);
            UIKit.MakeText(alertHead.transform, "Alerts:", 30, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
            var alertRows = new List<Image>();
            for (int k = 0; k < VitalsMonitor.ToxTable.Length; k++)
            {
                var row = UIKit.MakeImage(alertsCard, Theme.Surface, false, "Alert" + k);
                var rv = UIKit.VStack(row.rectTransform, 0, 0);
                rv.padding = new RectOffset(28, 28, 12, 12);
                UIKit.MakeText(row.transform, "", 28, Color.white, FontStyle.Bold);
                alertRows.Add(row);
            }

            // patient status + recommended action
            var statusRow = UIKit.Card(sheetContent, Theme.Surface, 22, 6);
            var sr = UIKit.Row(statusRow, 56, 10);
            var srh = sr.GetComponent<HorizontalLayoutGroup>(); srh.childForceExpandWidth = false; srh.childAlignment = TextAnchor.MiddleLeft;
            var stl = UIKit.MakeText(sr, "Patient status:", 30, Theme.Text, FontStyle.Bold, TextAnchor.MiddleLeft);
            UIKit.LE(stl, -1, -1, -1, -1, 1);
            var statusChipHolder = UIKit.NewRect("ChipHolder", sr);
            var shl = statusChipHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
            shl.childControlWidth = shl.childControlHeight = true; shl.childForceExpandWidth = shl.childForceExpandHeight = false;
            var actionCard = UIKit.Card(sheetContent, Theme.InfoBg, 26, 6);
            var ar = UIKit.Row(actionCard, -1, 18);
            var arh = ar.GetComponent<HorizontalLayoutGroup>(); arh.childForceExpandWidth = false; arh.childForceExpandHeight = false; arh.childAlignment = TextAnchor.MiddleLeft;
            Destroy(ar.GetComponent<LayoutElement>());
            UIKit.IconBadge(ar, "info", Theme.Blue, 72);
            var action = UIKit.MakeText(ar, "", 28, Theme.Text, FontStyle.Normal, TextAnchor.MiddleLeft);
            UIKit.LE(action, -1, -1, -1, -1, 1);
            UIKit.MakeText(sheetContent, "<i>Tap the knee to test the reflex and watch the breathing rate. Source: AHRQ table, adapted from Simpson 2004.</i>", 23, Theme.SubText);

            string lastStatus = null;
            System.Action<float> apply = mg =>
            {
                explorerMg = mg;
                var v = Vitals.FromSerumMg(mg);
                v.urine = 40;
                monitor.SetTarget(v);
                value.text = $"<color={Theme.Html(mg >= 9.5f ? Theme.Danger : mg >= 5f ? Theme.Success : Theme.AccentText)}>{mg:0.0} mg/dL</color>\n<size=24><color={Theme.Html(Theme.SubText)}>{mg / 1.215f:0.0} mEq/L  |  {mg / 2.43f:0.00} mmol/L</color></size>";
                int shown = 0;
                for (int k = 0; k < alertRows.Count; k++)
                {
                    var tr = VitalsMonitor.ToxTable[k];
                    bool sign = k >= 2;   // first two rows are reference ranges, not alerts
                    bool on = sign && mg >= tr.lo;
                    alertRows[k].gameObject.SetActive(on);
                    if (!on) continue;
                    shown++;
                    float sev = Mathf.InverseLerp(2, 8, k);
                    alertRows[k].color = Color.Lerp(Theme.Accent, Theme.Danger, sev);
                    alertRows[k].GetComponentInChildren<Text>().text = $"{tr.sign}  <size=22>(>= {tr.lo:0.#} mg/dL)</size>";
                }
                alertHead.color = shown == 0 ? Theme.Success : mg >= 12f ? Theme.Danger : Theme.Accent;
                alertHead.GetComponentInChildren<Text>().text = shown == 0 ? "No toxicity alerts" : $"Alerts: {shown}";
                string status = mg < 5f ? "Sub-therapeutic" : mg < 8f ? "Therapeutic" : mg < 30f ? "Toxicity detected" : "Cardiac arrest";
                if (status != lastStatus)
                {
                    lastStatus = status;
                    UIKit.Clear(statusChipHolder);
                    var chip = UIKit.Chip(statusChipHolder, status, status == "Therapeutic" ? Theme.SuccessBg : status == "Sub-therapeutic" ? Theme.WarnBg : Theme.DangerBg, 26);
                    chip.GetComponentInChildren<Text>().color = status == "Therapeutic" ? Theme.Success : status == "Sub-therapeutic" ? Theme.AccentText : Theme.Danger;
                    AppearAnim.Apply(chip, 0f, 0.8f);
                }
                action.text = mg < 5f ? "<b>Check dose and infusion</b>\nBelow therapeutic range - seizure protection may be inadequate."
                    : mg <= 8f ? "<b>Continue hourly monitoring</b>\nRR >= 16, patellar reflexes present, urine >= 30 mL/h."
                    : mg < 12f ? "<b>Stop MgSO4 and notify the provider</b>\nCheck urine output and serum Mg; prepare calcium gluconate."
                    : mg < 30f ? "<b>Administer Calcium Gluconate</b>\n1 g (10 mL of 10%) IV slowly; oxygen; call for help; support ventilation."
                    : "<b>Start resuscitation</b>\nCalcium gluconate, advanced airway, cardiac arrest protocol.";
            };
            slider.onValueChanged.AddListener(v => apply(v));
            var presets = UIKit.Row(sheetContent, 80, 10);
            foreach (var p in new[] { 2f, 7f, 10f, 14f, 20f, 32f })
            {
                float pv = p;
                UIKit.MakeButton(presets, pv.ToString("0"), Theme.Neutral, () => slider.value = pv, 28, 80);
            }
            apply(explorerMg);
            assessed.Add("chest"); assessed.Add("urine"); assessed.Add("knee");
            sheetScroll.verticalNormalizedPosition = 1f;
            AppearAnim.Stagger(sheetContent, 0.04f);
        }

        void ShowExplorerKeep()
        {
            // rebuild so the slider handle reflects the preset value
            ShowExplorer();
        }
    }
}
#endif
