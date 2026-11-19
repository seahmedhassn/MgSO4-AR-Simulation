#if MGSO4_READY
using System;
using UnityEngine;
#if VUFORIA_PRESENT
using Vuforia;
#endif

namespace MgSO4AR
{
    /// <summary>Shared start-up of the Vuforia camera used by the Surface and Marker modes.</summary>
    public static class VuforiaSession
    {
        public static string Error;
        public static event Action Started;

        public static string LicenseKey
        {
            get
            {
                var ta = Resources.Load<TextAsset>("VuforiaLicense");
                return ta != null ? ta.text.Trim() : "";
            }
        }

#if VUFORIA_PRESENT
        static bool subscribed;
        static ARSimController ctl;

        public static bool Running => VuforiaApplication.Instance.IsRunning;

        public static bool CanUse(ARSimController c)
        {
#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
            return c != null && c.vuforiaCameraObject != null && !string.IsNullOrEmpty(LicenseKey) && string.IsNullOrEmpty(Error);
#else
            return false;   // desktop preview builds: Vuforia runtime is not available
#endif
        }

        public static Camera Ensure(ARSimController c)
        {
            ctl = c;
            c.simCamera.gameObject.SetActive(false);
            try { VuforiaConfiguration.Instance.Vuforia.LicenseKey = LicenseKey; } catch (Exception e) { Debug.LogWarning(e); }
            c.vuforiaCameraObject.SetActive(true);
            if (!subscribed)
            {
                VuforiaApplication.Instance.OnVuforiaInitialized += OnInit;
                VuforiaApplication.Instance.OnVuforiaStarted += OnStarted;
                subscribed = true;
            }
            if (VuforiaApplication.Instance.IsRunning) Started?.Invoke();
            else if (!VuforiaApplication.Instance.IsInitialized && VuforiaConfiguration.Instance.Vuforia.DelayedInitialization)
                VuforiaApplication.Instance.Initialize();
            return c.vuforiaCameraObject.GetComponent<Camera>();
        }

        public static bool SetFlash(bool on)
        {
            try
            {
                if (!VuforiaApplication.Instance.IsRunning || VuforiaBehaviour.Instance == null) return false;
                var cd = VuforiaBehaviour.Instance.CameraDevice;
                if (on && !cd.IsFlashSupported()) return false;
                return cd.SetFlash(on);
            }
            catch (Exception e) { Debug.LogWarning("[MgSO4] flash: " + e.Message); return false; }
        }

        public static void Release(ARSimController c)
        {
            SetFlash(false);
            if (c.vuforiaCameraObject != null) c.vuforiaCameraObject.SetActive(false);
            c.simCamera.gameObject.SetActive(true);
        }

        static void OnInit(VuforiaInitError err)
        {
            if (err == VuforiaInitError.NONE) return;
            Error = err.ToString();
            Debug.LogWarning("[MgSO4] Vuforia init error: " + Error);
            if (ctl != null) ctl.OnVuforiaFailed("Vuforia AR is not available on this device (" + Error + ").");
        }

        static void OnStarted()
        {
            // continuous autofocus helps tracking in poorly lit rooms
            try { VuforiaBehaviour.Instance.CameraDevice.SetFocusMode(FocusMode.FOCUS_MODE_CONTINUOUSAUTO); } catch (Exception) { }
            Started?.Invoke();
        }
#else
        public static bool Running => false;
        public static bool CanUse(ARSimController c) => false;
        public static bool SetFlash(bool on) => false;
#endif
    }

    /// <summary>
    /// Marker AR with Vuforia Engine (free Basic licence). Vuforia's image tracker does not
    /// require ARCore, so it runs on any ARM64 Android 10+ phone with a camera. The marker is an
    /// "instant image target" generated at runtime from the bundled MgSO4_Marker texture, so no
    /// Target Manager database is needed. Print it at 18 cm width (Documents/App Guide).
    /// </summary>
    public class MarkerMode : TrackingMode
    {
        public const float PrintedWidthMeters = 0.18f;
        const float WardScaleOnMarker = 0.5f;     // 2.9 m ward -> ~1.45 m around the marker: big enough to see and tap

        public override string Label => "Marker AR (printed marker)";
        public override string Short => "Marker";
        public override bool UsesVuforia => true;
        public static string LicenseKey => VuforiaSession.LicenseKey;
        bool tracked;

#if VUFORIA_PRESENT
        ObserverBehaviour target;
        Ward ward;
        Camera cam;
        bool active;

        public override bool Available => VuforiaSession.CanUse(Ctl);
        public override Camera Cam => cam != null ? cam : Ctl.simCamera;
        public override bool IsTracking => tracked;
        public override string Hint => !string.IsNullOrEmpty(VuforiaSession.Error) ? "Vuforia error: " + VuforiaSession.Error
            : !VuforiaSession.Running ? "Starting AR camera..."
            : tracked ? null : "Point the camera at the printed MgSO4 AR marker (18 cm wide).";

        public override void Enter(Ward w)
        {
            ward = w;
            active = true;
            ward.SetVisible(false);
            VuforiaSession.Started -= OnStarted;
            VuforiaSession.Started += OnStarted;
            cam = VuforiaSession.Ensure(Ctl);
        }

        void OnStarted()
        {
            if (!active || ward == null) return;
            if (target == null)
            {
                var tex = Prim.Marker != null ? Prim.Marker : Resources.Load<Texture2D>("MgSO4_Marker");
                target = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(tex, PrintedWidthMeters, "MgSO4Marker");
                target.OnTargetStatusChanged += OnStatus;
            }
            target.enabled = true;
            if (ward.transform.parent != target.transform)
            {
                ward.transform.SetParent(target.transform, false);
                float s = markerScale / Mathf.Max(1e-4f, target.transform.lossyScale.x);
                ward.transform.localScale = Vector3.one * s;
                ward.transform.localPosition = Vector3.zero;
                ward.transform.localRotation = Quaternion.Euler(0, 180, 0);
                ward.SetVisible(tracked);
            }
        }

        void OnStatus(ObserverBehaviour b, TargetStatus s)
        {
            bool was = tracked;
            tracked = s.Status == Status.TRACKED || s.Status == Status.EXTENDED_TRACKED;
            if (tracked && !was) Haptics.Click();
            if (active && ward != null && ward.transform.parent == b.transform) ward.SetVisible(tracked);
        }

        public override bool SetTorch(bool on) => VuforiaSession.SetFlash(on);

        float markerScale = WardScaleOnMarker;
        public override bool Resize(float factor)
        {
            if (target == null || ward == null || ward.transform.parent != target.transform) return false;
            markerScale = Mathf.Clamp(markerScale * factor, 0.15f, 1.2f);
            ward.transform.localScale = Vector3.one * markerScale / Mathf.Max(1e-4f, target.transform.lossyScale.x);
            return true;
        }

        public override void Exit(Ward w)
        {
            active = false;
            VuforiaSession.Started -= OnStarted;
            if (target != null) target.enabled = false;
            if (w != null)
            {
                w.transform.SetParent(null, false);
                w.SetVisible(true);
            }
            ward = null;
            tracked = false;
        }
#else
        public override bool Available => false;
        public override Camera Cam => Ctl.simCamera;
        public override void Enter(Ward w) { }
        public override void Exit(Ward w) { }
#endif
    }

    /// <summary>
    /// Markerless "Surface AR" with Vuforia Ground Plane: the student scans a table or the floor and
    /// taps to place the ward; it stays anchored while they walk around it. Needs a phone with
    /// ARCore support (Vuforia Fusion uses ARCore); otherwise the app falls back to Camera mode.
    /// </summary>
    public class SurfaceMode : TrackingMode
    {
        public override string Label => "Surface AR (no marker)";
        public override string Short => "Surface";
        public override bool UsesVuforia => true;
        public static bool Unsupported;

#if VUFORIA_PRESENT
        Ward ward;
        Camera cam;
        bool active, placed, surfaceSeen;
        PlaneFinderBehaviour finder;
        GameObject reticle;
        AnchorBehaviour anchor;
        int anchorCount;
        float wardScale = 0.3f;
        float lastPinch;

        public override bool Available => !Unsupported && VuforiaSession.CanUse(Ctl);
        public override Camera Cam => cam != null ? cam : Ctl.simCamera;
        public override bool IsTracking => placed;
        public override string Hint => !string.IsNullOrEmpty(VuforiaSession.Error) ? "Vuforia error: " + VuforiaSession.Error
            : !VuforiaSession.Running ? "Starting AR camera..."
            : placed ? null
            : surfaceSeen ? "Surface found - tap the ring to place the patient's bed."
            : "Move the phone slowly over a table or the floor to find a surface.";

        public override void Enter(Ward w)
        {
            ward = w;
            active = true;
            placed = false;
            surfaceSeen = false;
            ward.SetVisible(false);
            VuforiaSession.Started -= OnStarted;
            VuforiaSession.Started += OnStarted;
            cam = VuforiaSession.Ensure(Ctl);
            enabled = true;
        }

        void OnStarted()
        {
            if (!active) return;
            bool supported = false;
            try { supported = VuforiaBehaviour.Instance.World.AnchorsSupported; } catch (Exception e) { Debug.LogWarning(e); }
            Debug.Log("[MgSO4] Ground plane supported: " + supported);
            if (!supported)
            {
                Unsupported = true;
                Ctl.OnTrackingFailed(this, "This phone has no surface tracking (ARCore). Switched to Camera mode - you can also use the printed marker.");
                return;
            }
            if (reticle == null)
            {
                reticle = new GameObject("PlacementReticle");
                Prim.Make(PrimitiveType.Cylinder, reticle.transform, Vector3.zero, new Vector3(0.3f, 0.002f, 0.3f), new Color(0.09f, 0.64f, 0.6f, 0.55f), "Ring", null, true);
                Prim.Make(PrimitiveType.Cylinder, reticle.transform, new Vector3(0, 0.002f, 0), new Vector3(0.05f, 0.002f, 0.05f), Color.white, "Dot");
            }
            if (finder == null)
            {
                var go = new GameObject("PlaneFinder");
                finder = go.AddComponent<PlaneFinderBehaviour>();
                finder.Height = 1.4f;
                finder.PlaneIndicator = reticle;
                try { finder.HitTestMode = HitTestMode.AUTOMATIC; } catch (Exception e) { Debug.LogWarning(e.Message); }
                if (finder.OnAutomaticHitTest == null) finder.OnAutomaticHitTest = new HitTestEvent();
                if (finder.OnInteractiveHitTest == null) finder.OnInteractiveHitTest = new HitTestEvent();
                finder.OnAutomaticHitTest.AddListener(OnAutoHit);
                finder.OnInteractiveHitTest.AddListener(OnPlace);
            }
            finder.enabled = true;
            reticle.SetActive(false);
        }

        void OnAutoHit(HitTestResult r)
        {
            if (!active || placed || reticle == null) return;
            surfaceSeen = r != null;
            reticle.SetActive(surfaceSeen);
            if (r != null) reticle.transform.SetPositionAndRotation(r.Position, r.Rotation);
        }

        void OnPlace(HitTestResult r)
        {
            if (!active || placed || r == null) return;
            try
            {
                anchor = VuforiaBehaviour.Instance.ObserverFactory.CreateAnchorBehaviour("WardAnchor" + (++anchorCount), r);
            }
            catch (Exception e) { Debug.LogWarning("[MgSO4] anchor: " + e.Message); }
            var parent = anchor != null ? anchor.transform : null;
            ward.transform.SetParent(parent, false);
            ward.transform.position = r.Position;
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            ward.transform.rotation = Quaternion.LookRotation(-fwd.normalized, Vector3.up);
            // auto-fit: make the ward fill ~85% of the screen width at the distance it was placed
            wardScale = cam.transform.position.y - r.Position.y > 0.8f ? 0.7f : 0.5f;   // interactive size
            ward.transform.localScale = Vector3.one * wardScale / Mathf.Max(1e-4f, parent != null ? parent.lossyScale.x : 1f);
            ward.SetVisible(true);
            placed = true;
            Ctl.Toast("Placed. Pinch to resize - use the + / - buttons - Recenter to move it.", 4f);
            Haptics.Success();
            if (reticle != null) reticle.SetActive(false);
            if (finder != null) finder.enabled = false;
        }

        void Update()
        {
            if (!active || !VuforiaSession.Running) return;
            if (!placed)
            {
                if (finder != null && surfaceSeen && PointerInput.Down(out var p) && !UIKit.OverUI(p))
                    finder.PerformHitTest(p);
                return;
            }
            // pinch to resize the placed ward (table size ... life size)
            if (PointerInput.TouchCount >= 2)
            {
                float d = Vector2.Distance(PointerInput.TouchPos(0), PointerInput.TouchPos(1));
                if (lastPinch > 0)
                {
                    wardScale = Mathf.Clamp(wardScale * d / Mathf.Max(1f, lastPinch), 0.15f, 1.2f);
                    var parent = ward.transform.parent;
                    ward.transform.localScale = Vector3.one * wardScale / Mathf.Max(1e-4f, parent != null ? parent.lossyScale.x : 1f);
                }
                lastPinch = d;
            }
            else lastPinch = 0;
        }

        public override bool Resize(float factor)
        {
            if (!placed || ward == null) return false;
            wardScale = Mathf.Clamp(wardScale * factor, 0.15f, 1.2f);
            var parent = ward.transform.parent;
            ward.transform.localScale = Vector3.one * wardScale / Mathf.Max(1e-4f, parent != null ? parent.lossyScale.x : 1f);
            return true;
        }

        /// <summary>"Recenter" = pick up the ward and place it again.</summary>
        public override void Recenter()
        {
            if (!active) return;
            placed = false;
            surfaceSeen = false;
            ward.SetVisible(false);
            ward.transform.SetParent(null, false);
            if (anchor != null) Destroy(anchor.gameObject);
            anchor = null;
            if (finder != null) finder.enabled = true;
        }

        public override bool SetTorch(bool on) => VuforiaSession.SetFlash(on);

        public override void Exit(Ward w)
        {
            active = false;
            enabled = false;
            VuforiaSession.Started -= OnStarted;
            if (finder != null) finder.enabled = false;
            if (reticle != null) reticle.SetActive(false);
            if (w != null)
            {
                w.transform.SetParent(null, false);
                w.SetVisible(true);
            }
            if (anchor != null) Destroy(anchor.gameObject);
            anchor = null;
            placed = false;
        }
#else
        public override bool Available => false;
        public override Camera Cam => Ctl.simCamera;
        public override void Enter(Ward w) { }
        public override void Exit(Ward w) { }
#endif
    }
}
#endif
