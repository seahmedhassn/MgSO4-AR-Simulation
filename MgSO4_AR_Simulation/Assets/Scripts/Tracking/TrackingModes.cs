#if MGSO4_READY
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MgSO4AR
{
    /// <summary>How the virtual ward is anchored to the student's view.</summary>
    public abstract class TrackingMode : MonoBehaviour
    {
        public ARSimController Ctl;
        public abstract string Label { get; }
        public abstract string Short { get; }
        public abstract bool Available { get; }
        public virtual bool UsesVuforia => false;
        public virtual bool IsTracking => true;
        public virtual string Hint => null;
        public abstract Camera Cam { get; }
        public abstract void Enter(Ward ward);
        public abstract void Exit(Ward ward);
        public virtual void Recenter() { }
        /// <summary>Turn the phone's flashlight on/off. Returns false if not possible in this mode.</summary>
        public virtual bool SetTorch(bool on) => false;
        /// <summary>Scale the placed ward by a factor. Returns false if not supported.</summary>
        public virtual bool Resize(float factor) => false;
    }

    // ------------------------------------------------------------------------------------------
    /// <summary>Pure 3D mode (no camera): orbit, pinch-zoom. Works on every device.</summary>
    public class FreeOrbitMode : TrackingMode
    {
        float yaw = 200f, pitch = 28f, dist = 3.6f;
        Vector3 target = new Vector3(0.05f, 0.75f, 0.15f);
        Vector2 lastPos;
        bool dragging;
        float lastPinch;

        public override string Label => "3D view (no camera)";
        public override string Short => "3D";
        public override bool Available => true;
        public override Camera Cam => Ctl.simCamera;
        public static FreeOrbitMode Active;

        /// <summary>Scripted camera (presentation recorder).</summary>
        public void SetView(float yawDeg, float pitchDeg, float distance, Vector3 lookAt)
        {
            yaw = yawDeg; pitch = pitchDeg; dist = distance; target = lookAt;
        }

        public override void Enter(Ward ward)
        {
            Active = this;
            ward.transform.SetParent(null, false);
            ward.transform.position = Vector3.zero;
            ward.transform.rotation = Quaternion.identity;
            ward.transform.localScale = Vector3.one;
            ward.SetVisible(true);
            var cam = Ctl.simCamera;
            cam.gameObject.SetActive(true);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.17f, 0.23f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            Recenter();
            enabled = true;
        }

        public override void Exit(Ward ward) { enabled = false; }
        public override bool Resize(float factor)
        {
            dist = Mathf.Clamp(dist / factor, 1.2f, 9f);
            return true;
        }

        public override void Recenter()
        {
            yaw = 195f; pitch = 30f;
            // fit the ~3 m wide ward horizontally (portrait screens have a narrow horizontal FOV)
            var cam = Ctl.simCamera;
            float halfW = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.3f, cam.aspect);
            dist = Mathf.Clamp(1.75f / halfW, 3.6f, 8f);
        }

        void LateUpdate()
        {
            if (Ctl == null) return;
            if (PointerInput.TouchCount >= 2)
            {
                float d = Vector2.Distance(PointerInput.TouchPos(0), PointerInput.TouchPos(1));
                if (lastPinch > 0) dist = Mathf.Clamp(dist * (lastPinch / Mathf.Max(1f, d)), 1.2f, 9f);
                lastPinch = d;
                dragging = false;
            }
            else
            {
                lastPinch = 0;
                if (PointerInput.Down(out var p)) { dragging = !UIKit.OverUI(p); lastPos = p; }
                if (dragging && PointerInput.Held(out var h))
                {
                    var delta = h - lastPos;
                    lastPos = h;
                    float s = 180f / Mathf.Max(400f, Screen.width);
                    yaw += delta.x * s;
                    pitch = Mathf.Clamp(pitch - delta.y * s, 5f, 80f);
                }
                else dragging = false;
            }
            float sc = PointerInput.Scroll;
            if (Mathf.Abs(sc) > 0.01f) dist = Mathf.Clamp(dist * (1f - sc * 0.1f), 1.2f, 9f);

            var cam = Ctl.simCamera.transform;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            cam.position = target + rot * new Vector3(0, 0, -dist);
            cam.rotation = rot;
            // keep the ward in the visible area above the bottom sheet
            cam.position -= cam.up * (Ctl.SheetFraction * dist * 0.42f);
        }
    }

    // ------------------------------------------------------------------------------------------
    /// <summary>
    /// "Camera overlay" AR (3-DoF): live rear-camera background + gyroscope orientation.
    /// Needs no ARCore/ARKit and no marker, so it runs on almost any phone with a camera.
    /// </summary>
    public class CameraOverlayMode : TrackingMode
    {
        WebCamTexture tex;
        Canvas bgCanvas;
        RawImage raw;
        bool gyro;
        bool placed;
        float yawManual, pitchManual;
        Vector2 lastPos;
        bool dragging;
        string hint;
        Ward ward;
        static readonly Quaternion GyroFix = Quaternion.Euler(90f, 0f, 0f);

        public override string Label => "Camera AR (gyroscope)";
        public override string Short => "Camera";
        public override bool Available => WebCamTexture.devices != null && WebCamTexture.devices.Length > 0
#if UNITY_ANDROID && !UNITY_EDITOR
            || true
#endif
            ;
        public override Camera Cam => Ctl.simCamera;
        public override string Hint => hint;

        public override void Enter(Ward w)
        {
            ward = w;
            ward.transform.SetParent(null, false);
            ward.SetVisible(true);
            var cam = Ctl.simCamera;
            cam.gameObject.SetActive(true);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;
            gyro = PointerInput.EnableGyro();
            placed = false;
            hint = "Starting camera...";
            enabled = true;
            StartCoroutine(StartCamera());
        }

        IEnumerator StartCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                float t = 0;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && t < 20f)
                { t += Time.deltaTime; yield return null; }
            }
#endif
            yield return null;
            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0) { hint = "No camera found - use 3D view."; yield break; }
            string name = devices[0].name;
            foreach (var d in devices) if (!d.isFrontFacing) { name = d.name; break; }
            tex = new WebCamTexture(name, 1280, 720, 30);
            tex.Play();

            var go = new GameObject("CameraBackground", typeof(RectTransform), typeof(Canvas));
            bgCanvas = go.GetComponent<Canvas>();
            bgCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            bgCanvas.worldCamera = Ctl.simCamera;
            bgCanvas.planeDistance = Ctl.simCamera.farClipPlane * 0.9f;
            bgCanvas.sortingOrder = -100;
            raw = UIKit.NewRect("Feed", go.transform).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.texture = tex;
            raw.rectTransform.anchorMin = raw.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hint = gyro ? "Hold the phone up and look around. Tap 'Recenter' to bring the ward in front of you."
                        : "No gyroscope: drag to look around.";
            yield return new WaitForSeconds(0.4f);
            Recenter();
        }

        public override void Recenter()
        {
            if (ward == null) return;
            var cam = Ctl.simCamera.transform;
            var fwd = cam.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            ward.transform.localScale = Vector3.one * overlayScale;
            ward.transform.position = cam.position + fwd * 2.6f + Vector3.down * 1.0f;
            ward.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            placed = true;
        }

        public override bool SetTorch(bool on) => AndroidTorch.Set(on);

        float overlayScale = 0.6f;
        public override bool Resize(float factor)
        {
            if (ward == null) return false;
            overlayScale = Mathf.Clamp(overlayScale * factor, 0.15f, 1.5f);
            ward.transform.localScale = Vector3.one * overlayScale;
            return true;
        }

        public override void Exit(Ward w)
        {
            AndroidTorch.Set(false);
            StopAllCoroutines();
            if (tex != null) { tex.Stop(); Destroy(tex); tex = null; }
            if (bgCanvas != null) Destroy(bgCanvas.gameObject);
            PointerInput.DisableGyro();
            enabled = false;
        }

        void LateUpdate()
        {
            var cam = Ctl.simCamera.transform;
            if (gyro)
            {
                var q = PointerInput.GyroAttitude;
                cam.localRotation = GyroFix * new Quaternion(q.x, q.y, -q.z, -q.w);
            }
            else
            {
                if (PointerInput.Down(out var p)) { dragging = !UIKit.OverUI(p); lastPos = p; }
                if (dragging && PointerInput.Held(out var h))
                {
                    var d = h - lastPos; lastPos = h;
                    yawManual -= d.x * 0.15f;
                    pitchManual = Mathf.Clamp(pitchManual + d.y * 0.15f, -60f, 60f);
                }
                else dragging = false;
                cam.rotation = Quaternion.Euler(pitchManual + 20f, yawManual, 0);
                if (!placed) Recenter();
            }
            UpdateFeedLayout();
        }

        void UpdateFeedLayout()
        {
            if (tex == null || raw == null || tex.width < 32) return;
            var canvasRt = (RectTransform)bgCanvas.transform;
            float W = canvasRt.rect.width, H = canvasRt.rect.height;
            int angle = tex.videoRotationAngle;
            bool rotated = angle % 180 != 0;
            float tw = tex.width, th = tex.height;
            float imgW = rotated ? th : tw, imgH = rotated ? tw : th;
            float s = Mathf.Max(W / imgW, H / imgH);
            raw.rectTransform.sizeDelta = new Vector2(tw * s, th * s);
            raw.rectTransform.localEulerAngles = new Vector3(0, 0, -angle);
            raw.rectTransform.localScale = new Vector3(1, tex.videoVerticallyMirrored ? -1 : 1, 1);
        }
    }
}
namespace MgSO4AR
{
    /// <summary>Flashlight through Android's CameraManager.setTorchMode (API 23+).</summary>
    public static class AndroidTorch
    {
        public static bool Set(bool on)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var mgr = activity.Call<AndroidJavaObject>("getSystemService", "camera"))
                {
                    var ids = mgr.Call<string[]>("getCameraIdList");
                    using (var keyFlash = new AndroidJavaClass("android.hardware.camera2.CameraCharacteristics"))
                    {
                        var flashKey = keyFlash.GetStatic<AndroidJavaObject>("FLASH_INFO_AVAILABLE");
                        var facingKey = keyFlash.GetStatic<AndroidJavaObject>("LENS_FACING");
                        foreach (var id in ids)
                        {
                            using (var ch = mgr.Call<AndroidJavaObject>("getCameraCharacteristics", id))
                            {
                                var hasFlash = ch.Call<AndroidJavaObject>("get", flashKey);
                                var facing = ch.Call<AndroidJavaObject>("get", facingKey);
                                bool flash = hasFlash != null && hasFlash.Call<bool>("booleanValue");
                                bool back = facing != null && facing.Call<int>("intValue") == 1; // LENS_FACING_BACK
                                if (flash && back)
                                {
                                    mgr.Call("setTorchMode", id, on);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[MgSO4] torch: " + e.Message); }
#endif
            return false;
        }
    }
}
#endif
