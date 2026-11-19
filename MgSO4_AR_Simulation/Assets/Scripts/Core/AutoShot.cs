#if MGSO4_READY
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MgSO4AR
{
    /// <summary>
    /// QA helper: when the player is launched with "-autoshot" it walks through the main screens,
    /// saves screenshots next to the build (../../BuildLogs/screens) and quits. Not used in normal runs.
    /// </summary>
    public class AutoShot : MonoBehaviour
    {
        string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autoshot") < 0) return;
            var go = new GameObject("AutoShot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoShot>();
        }

        IEnumerator Start()
        {
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../BuildLogs/screens"));
            Directory.CreateDirectory(dir);
            PlayerPrefs.SetString("tracking_mode_user", "3D");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("01_home");
            var menu = FindFirstObjectByType<MenuController>();
            foreach (var p in new[] { "learn", "dose", "quiz", "results", "help", "participant" })
            {
                menu.DebugOpen(p);
                yield return new WaitForSeconds(0.6f);
                yield return Shot("02_" + p);
            }
            foreach (var p in new[] { "learn_open", "dose_question", "dose_result", "quiz_question", "quiz_feedback", "quiz_result", "marker" })
            {
                menu.DebugInner(p);
                yield return new WaitForSeconds(0.9f);
                yield return Shot("02b_" + p);
            }
            menu.DebugOpen("home");
            yield return new WaitForSeconds(0.6f);
            SceneManager.LoadScene("ARSim");
            yield return new WaitForSeconds(2.5f);
            var sim = FindFirstObjectByType<ARSimController>();
            foreach (var i in new[] { 0, 1, 3, 4, 5, 7, 12, 14, 16 })
            {
                sim.DebugShowStep(i);
                yield return new WaitForSeconds(1.2f);
                yield return Shot($"03_step{i:00}");
            }
            sim.DebugCollapse();
            yield return new WaitForSeconds(0.8f);
            yield return Shot("04_scene_collapsed");
            sim.DebugCollapse();
            sim.DebugExplorer(14f);
            yield return new WaitForSeconds(2f);
            yield return Shot("05_explorer");
            yield return CloseUps();
            sim.DebugDebrief();
            yield return new WaitForSeconds(1f);
            yield return Shot("06_debrief");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        IEnumerator CloseUps()
        {
            // close-ups of the 3D ward (off-screen cameras, ward-local coordinates)
            var ward = FindFirstObjectByType<Ward>();
            if (ward != null)
            {
                ward.SetSyringeFill(0.6f);
                CloseUp(ward, "07_patient_cam", new Vector3(-0.1f, 1.55f, 1.7f), new Vector3(-0.2f, 0.8f, 0f), 45f);
                CloseUp(ward, "08_face_cam", new Vector3(-0.45f, 1.25f, 0.3f), new Vector3(-0.8f, 0.86f, 0f), 35f);
                CloseUp(ward, "09_tray_cam", new Vector3(0.95f, 1.3f, 1.45f), new Vector3(1.12f, 0.85f, 0.75f), 40f);
                CloseUp(ward, "10_equipment_cam", new Vector3(0.1f, 1.5f, 1.3f), new Vector3(-1.13f, 1.35f, 0f), 45f);
                CloseUp(ward, "11_legs_cam", new Vector3(0.9f, 1.1f, 1.1f), new Vector3(0.4f, 0.7f, 0f), 45f);
                ward.SetOxygen(true);
                ward.Somnolent = true;
                yield return null;
                yield return null;
                CloseUp(ward, "12_face_o2_cam", new Vector3(-0.45f, 1.25f, 0.3f), new Vector3(-0.8f, 0.86f, 0f), 35f);
                ward.Somnolent = false;
                ward.Kick(2);
                // phases: 0.4 s hip lift, +0.35 s shin hangs, +0.2 s tap, kick 0.47 s, hold 0.45 s
                float[] at = { 0.38f, 0.8f, 1.08f, 1.75f };
                string[] nm = { "13a_lift", "13b_hang", "13c_kick", "13d_hold" };
                float elapsed = 0f;
                for (int i = 0; i < at.Length; i++)
                {
                    yield return new WaitForSeconds(at[i] - elapsed); elapsed = at[i];
                    CloseUp(ward, nm[i] + "_side", new Vector3(0.62f, 0.86f, 1.25f), new Vector3(0.35f, 0.74f, 0.1f), 50f);
                    CloseUp(ward, nm[i] + "_top", new Vector3(0.35f, 1.35f, 1.0f), new Vector3(0.35f, 0.72f, 0.12f), 50f);
                }
                CloseUp(ward, "13_kick_cam", new Vector3(0.9f, 1.1f, 1.1f), new Vector3(0.4f, 0.7f, 0f), 45f);
            }
        }

        void CloseUp(Ward ward, string name, Vector3 eyeLocal, Vector3 targetLocal, float fov)
        {
            var go = new GameObject("AutoShotCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
                cam.fieldOfView = fov;
                var wt = ward.transform;
                cam.nearClipPlane = 0.01f * wt.lossyScale.x;
                cam.farClipPlane = 20f * Mathf.Max(1f, wt.lossyScale.x);
                go.transform.position = wt.TransformPoint(eyeLocal);
                go.transform.LookAt(wt.TransformPoint(targetLocal), wt.up);
                int w = 1080, h = 1080;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
            }
            catch (System.Exception e) { Debug.LogWarning("[AutoShot] " + e.Message); }
            Destroy(go);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            CameraShot(name + "_cam");
            yield return null;
            yield return null;
        }

        /// <summary>Off-screen render of the 3D cameras (works when the window is hidden/locked; no overlay UI).</summary>
        void CameraShot(string name)
        {
            try
            {
                var cams = new System.Collections.Generic.List<Camera>(Camera.allCameras);
                if (cams.Count == 0) return;
                cams.Sort((a, b) => a.depth.CompareTo(b.depth));
                int w = 540, h = 1080;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                foreach (var c in cams)
                {
                    var old = c.targetTexture;
                    c.targetTexture = rt;
                    c.Render();
                    c.targetTexture = old;
                }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
            }
            catch (System.Exception e) { Debug.LogWarning("[AutoShot] " + e.Message); }
        }
    }
}
#endif
