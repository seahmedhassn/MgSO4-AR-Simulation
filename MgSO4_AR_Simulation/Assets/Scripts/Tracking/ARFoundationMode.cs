#if MGSO4_READY
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ARF_PRESENT
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
#endif

namespace MgSO4AR
{
    /// <summary>
    /// Markerless "Surface AR" on Google ARCore (Android) / Apple ARKit (iOS) through Unity AR Foundation.
    /// Free, no licence and no watermark. The app probes ARCore support when the AR scene opens; if the
    /// phone is not ARCore-certified it falls back to Vuforia marker tracking, then Camera mode.
    /// XR is started only while this mode is active, so Vuforia and ARCore never run at the same time.
    /// </summary>
    public class ARFoundationMode : TrackingMode
    {
        public override string Label => "Surface AR (ARCore, no marker)";
        public override string Short => "Surface";
        public static bool? Supported;        // null = not probed yet

#if ARF_PRESENT
        GameObject sessionGo, originGo, reticle, anchorGo;
        ARSession session;
        ARRaycastManager raycaster;
        ARPlaneManager planes;
        Camera cam;
        Ward ward;
        bool active, placed, surfaceSeen, starting;
        float wardScale = DefaultTableScale, lastPinch;
        public const float DefaultTableScale = 0.5f, DefaultFloorScale = 0.7f;
        string status = "Starting ARCore...";
        static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        public override bool Available => Supported != false && Application.isMobilePlatform;
        public override Camera Cam => cam != null && active ? cam : Ctl.simCamera;
        public override bool IsTracking => placed;
        public override string Hint => placed ? null
            : starting ? status
            : surfaceSeen ? "Surface found - tap the ring to place the patient's bed."
            : "Move the phone slowly over a table or the floor to find a surface.";

        // ------------------------------------------------------------------ support probe
        static bool LoaderReady => XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
                                   && XRGeneralSettings.Instance.Manager.activeLoader != null;

        static IEnumerator InitXR()
        {
            var gs = XRGeneralSettings.Instance;
            if (gs == null || gs.Manager == null) yield break;
            if (gs.Manager.activeLoader == null) yield return gs.Manager.InitializeLoader();
        }

        static void DeinitXR()
        {
            var gs = XRGeneralSettings.Instance;
            if (gs == null || gs.Manager == null || gs.Manager.activeLoader == null) return;
            gs.Manager.StopSubsystems();
            gs.Manager.DeinitializeLoader();
        }

        /// <summary>Checks ARCore/ARKit availability once (installs Google Play Services for AR if needed).</summary>
        public static IEnumerator Probe(Action<bool> done)
        {
            if (Supported.HasValue) { done(Supported.Value); yield break; }
            if (!Application.isMobilePlatform) { Supported = false; done(false); yield break; }
            yield return InitXR();
            if (!LoaderReady) { Supported = false; Debug.Log("[MgSO4] ARCore loader unavailable"); done(false); yield break; }
            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
                yield return ARSession.CheckAvailability();
            if (ARSession.state == ARSessionState.NeedsInstall)
                yield return ARSession.Install();
            Supported = ARSession.state >= ARSessionState.Ready;
            Debug.Log("[MgSO4] ARCore/ARKit supported: " + Supported + " (" + ARSession.state + ")");
            if (!Supported.Value) DeinitXR();
            done(Supported.Value);
        }

        // ------------------------------------------------------------------ enter / exit
        public override void Enter(Ward w)
        {
            ward = w;
            active = true; placed = false; surfaceSeen = false; starting = true;
            status = "Starting ARCore...";
            ward.SetVisible(false);
            enabled = true;
            StartCoroutine(StartAR());
        }

        IEnumerator StartAR()
        {
            yield return InitXR();
            if (!LoaderReady) { Fail("ARCore could not start on this phone."); yield break; }
            if (Supported != true)
            {
                bool ok = false;
                yield return Probe(r => ok = r);
                if (!ok) { Fail("This phone does not support ARCore."); yield break; }
                yield return InitXR();
            }
            BuildRig();
            XRGeneralSettings.Instance.Manager.StartSubsystems();
            Ctl.simCamera.gameObject.SetActive(false);
            sessionGo.SetActive(true);
            originGo.SetActive(true);
            CamRef.Active = cam;
            starting = false;
        }

        void Fail(string msg)
        {
            Supported = false;
            starting = false;
            DeinitXR();
            Ctl.OnTrackingFailed(this, msg + " Using marker / camera mode instead.");
        }

        void BuildRig()
        {
            if (sessionGo != null) return;
            sessionGo = new GameObject("AR Session");
            session = sessionGo.AddComponent<ARSession>();
            sessionGo.AddComponent<ARInputManager>();
            sessionGo.SetActive(false);

            originGo = new GameObject("XR Origin");
            originGo.SetActive(false);
            var origin = originGo.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("AR Camera");
            camGo.transform.SetParent(offset.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            var pos = new InputAction("Position", binding: "<XRHMD>/centerEyePosition");
            pos.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rot = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation");
            rot.AddBinding("<HandheldARInputDevice>/deviceRotation");
            pos.Enable(); rot.Enable();
            tpd.positionInput = new InputActionProperty(pos);
            tpd.rotationInput = new InputActionProperty(rot);
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            raycaster = originGo.AddComponent<ARRaycastManager>();
            planes = originGo.AddComponent<ARPlaneManager>();
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            originGo.AddComponent<ARAnchorManager>();

            reticle = new GameObject("PlacementReticle");
            Prim.Make(PrimitiveType.Cylinder, reticle.transform, Vector3.zero, new Vector3(0.3f, 0.002f, 0.3f), new Color(0.09f, 0.64f, 0.6f, 0.55f), "Ring", null, true);
            Prim.Make(PrimitiveType.Cylinder, reticle.transform, new Vector3(0, 0.002f, 0), new Vector3(0.05f, 0.002f, 0.05f), Color.white, "Dot");
            reticle.SetActive(false);
        }

        public override void Exit(Ward w)
        {
            active = false;
            enabled = false;
            StopAllCoroutines();
            if (w != null) { w.transform.SetParent(null, false); w.SetVisible(true); }
            if (anchorGo != null) Destroy(anchorGo);
            anchorGo = null;
            if (reticle != null) reticle.SetActive(false);
            if (originGo != null) originGo.SetActive(false);
            if (sessionGo != null) sessionGo.SetActive(false);
            SetTorch(false);
            DeinitXR();
            Ctl.simCamera.gameObject.SetActive(true);
            placed = false;
        }

        // ------------------------------------------------------------------ placement
        void Update()
        {
            if (!active || starting || raycaster == null) return;
            if (!placed)
            {
                var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
                surfaceSeen = raycaster.Raycast(center, hits, TrackableType.PlaneWithinPolygon);
                reticle.SetActive(surfaceSeen);
                if (surfaceSeen) reticle.transform.SetPositionAndRotation(hits[0].pose.position, hits[0].pose.rotation);
                if (PointerInput.Down(out var p) && !UIKit.OverUI(p))
                {
                    if (raycaster.Raycast(p, hits, TrackableType.PlaneWithinPolygon)) Place(hits[0].pose);
                    else if (surfaceSeen) Place(new Pose(reticle.transform.position, reticle.transform.rotation));
                }
                return;
            }
            if (PointerInput.TouchCount >= 2)
            {
                float d = Vector2.Distance(PointerInput.TouchPos(0), PointerInput.TouchPos(1));
                if (lastPinch > 0) Resize(d / Mathf.Max(1f, lastPinch));
                lastPinch = d;
            }
            else lastPinch = 0;
        }

        void Place(Pose pose)
        {
            anchorGo = new GameObject("WardAnchor");
            anchorGo.transform.SetPositionAndRotation(pose.position, Quaternion.identity);
            try { anchorGo.AddComponent<ARAnchor>(); } catch (Exception e) { Debug.LogWarning("[MgSO4] anchor: " + e.Message); }
            ward.transform.SetParent(anchorGo.transform, false);
            ward.transform.position = pose.position;
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            ward.transform.rotation = Quaternion.LookRotation(-fwd.normalized, Vector3.up);
            // Interactive size (not life size, but big enough to see and tap everything):
            // on the floor the bed is ~1.4 m long (scale 0.7), on a table ~1 m (scale 0.5).
            float camHeight = cam.transform.position.y - pose.position.y;
            wardScale = camHeight > 0.8f ? DefaultFloorScale : DefaultTableScale;
            ward.transform.localScale = Vector3.one * wardScale;
            ward.SetVisible(true);
            placed = true;
            reticle.SetActive(false);
            Ctl.Toast("Placed at a hands-on size. Step back to see the whole bedside; pinch or + / - to resize.", 4.5f);
            Haptics.Success();
        }

        public override bool Resize(float factor)
        {
            if (!placed) return false;
            wardScale = Mathf.Clamp(wardScale * factor, 0.15f, 1.2f);
            ward.transform.localScale = Vector3.one * wardScale;
            return true;
        }

        public override void Recenter()
        {
            if (!active || starting) return;
            placed = false;
            ward.SetVisible(false);
            ward.transform.SetParent(null, false);
            if (anchorGo != null) Destroy(anchorGo);
            anchorGo = null;
        }

        // ARCore owns the camera while running; the torch is switched via ARCore's camera config where supported,
        // otherwise via Android's CameraManager.
        public override bool SetTorch(bool on)
        {
            try
            {
                var cm = cam != null ? cam.GetComponent<ARCameraManager>() : null;
                var prop = cm != null ? cm.GetType().GetProperty("requestedTorchMode") : null;   // AR Foundation 6.1+
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(cm, Enum.Parse(prop.PropertyType, on ? "On" : "Off"));
                    return true;
                }
            }
            catch (Exception e) { Debug.LogWarning("[MgSO4] ARCore torch: " + e.Message); }
            return AndroidTorch.Set(on);
        }
#else
        public override bool Available => false;
        public override Camera Cam => Ctl.simCamera;
        public override void Enter(Ward w) { }
        public override void Exit(Ward w) { }
        public static IEnumerator Probe(Action<bool> done) { Supported = false; done(false); yield break; }
#endif
    }
}
#endif
