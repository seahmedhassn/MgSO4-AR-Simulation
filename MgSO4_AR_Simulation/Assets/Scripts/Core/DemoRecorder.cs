#if MGSO4_READY
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MgSO4AR
{
    /// <summary>
    /// Presentation recorder. Launch the player with "-demo": it plays a scripted tour of the app
    /// (splash, menu navigation, learning, dose calculation, quiz, AR clinical simulation in 3D view,
    /// patellar reflex, toxicity explorer, debrief) at a fixed 30 fps and writes every frame as JPG to
    /// ../BuildLogs/demo/ (and key screenshots to ../BuildLogs/demo_shots/). The frames are encoded to
    /// MP4 afterwards with ffmpeg. Tap / swipe indicators show where a user would touch.
    /// </summary>
    public class DemoRecorder : MonoBehaviour
    {
        const int Fps = 30;
        string frameDir, shotDir;
        int frame;
        bool recording = true;
        RectTransform tapRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-demo") < 0) return;
            var go = new GameObject("DemoRecorder");
            DontDestroyOnLoad(go);
            go.AddComponent<DemoRecorder>();
        }

        void Awake()
        {
            Time.captureFramerate = Fps;   // game time advances exactly 1/30 s per rendered frame
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../BuildLogs"));
            frameDir = Path.Combine(root, "demo");
            shotDir = Path.Combine(root, "demo_shots");
            Directory.CreateDirectory(frameDir);
            Directory.CreateDirectory(shotDir);
            foreach (var f in Directory.GetFiles(frameDir, "*.jpg")) { try { File.Delete(f); } catch { } }
            PlayerPrefs.SetString("tracking_mode_user", "3D");
            BuildOverlay();
            StartCoroutine(CaptureLoop());
            StartCoroutine(Script());
        }

        // ------------------------------------------------------------------ capture
        IEnumerator CaptureLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (recording)
            {
                yield return eof;
                var tex = ScreenCapture.CaptureScreenshotAsTexture(2);
                File.WriteAllBytes(Path.Combine(frameDir, $"f{frame:00000}.jpg"), tex.EncodeToJPG(88));
                Destroy(tex);
                frame++;
            }
        }

        void Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name + ".png"), 2);
        }

        static WaitForSeconds Wait(float s) => new WaitForSeconds(s);

        // ------------------------------------------------------------------ tap / swipe indicator
        void BuildOverlay()
        {
            var go = new GameObject("DemoOverlay");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 999;
            tapRoot = (RectTransform)go.transform;
        }

        IEnumerator Tap(Vector2 screenPos, float hold = 0.35f)
        {
            // press the real UI element under the finger so its touch ripple / press animation plays
            GameObject pressed = null;
            UnityEngine.EventSystems.PointerEventData ped = null;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPos };
                var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                es.RaycastAll(ped, hits);
                if (hits.Count > 0)
                {
                    ped.pointerCurrentRaycast = hits[0];
                    pressed = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                }
            }
            StartCoroutine(Release(pressed, ped, hold));
            var ring = new GameObject("Tap").AddComponent<Image>();
            ring.transform.SetParent(tapRoot, false);
            ring.sprite = UIKit.Circle; ring.raycastTarget = false;
            var rt = ring.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.position = screenPos;
            float size = Screen.height * 0.07f;
            float t = 0;
            while (t < hold + 0.35f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / (hold + 0.35f));
                rt.sizeDelta = Vector2.one * size * (0.6f + 0.8f * k);
                ring.color = new Color(1f, 1f, 1f, 0.55f * (1f - k * k));
                yield return null;
            }
            Destroy(ring.gameObject);
        }

        IEnumerator Release(GameObject go, UnityEngine.EventSystems.PointerEventData ped, float after)
        {
            yield return new WaitForSeconds(after);
            if (go != null && ped != null) UnityEngine.EventSystems.ExecuteEvents.Execute(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
        }

        IEnumerator Swipe(Vector2 from, Vector2 to, float dur = 0.45f)
        {
            var dot = new GameObject("Swipe").AddComponent<Image>();
            dot.transform.SetParent(tapRoot, false);
            dot.sprite = UIKit.Circle; dot.raycastTarget = false;
            var rt = dot.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = Vector2.one * Screen.height * 0.055f;
            float t = 0;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Ease.OutCubic(Mathf.Clamp01(t / dur));
                rt.position = Vector2.Lerp(from, to, k);
                dot.color = new Color(1f, 1f, 1f, 0.6f * (1f - 0.6f * k));
                yield return null;
            }
            Destroy(dot.gameObject);
        }

        /// <summary>Screen position of the first visible UI Text containing the given label.</summary>
        static Vector2 UiPos(string label, Vector2 fallbackFrac)
        {
            foreach (var t in FindObjectsByType<Text>(FindObjectsSortMode.None))
                if (t.isActiveAndEnabled && t.text != null && t.text.Contains(label))
                {
                    var p = RectTransformUtility.WorldToScreenPoint(null, t.rectTransform.position);
                    if (p.x > 0 && p.y > 0 && p.x < Screen.width && p.y < Screen.height) return p;
                }
            return new Vector2(Screen.width * fallbackFrac.x, Screen.height * fallbackFrac.y);
        }

        // ------------------------------------------------------------------ the tour
        IEnumerator Script()
        {
            // 1. splash + home
            yield return Wait(4.8f);
            Shot("01_home");
            yield return Wait(1.2f);
            var menu = FindFirstObjectByType<MenuController>();

            // 2. Learn (tap tile -> page slides in), swipe back
            if (menu != null)
            {
                yield return Tap(UiPos("Learn", new Vector2(0.75f, 0.45f)));
                menu.DemoGo("learn");
                yield return Wait(2.6f);
                Shot("02_learn");
                yield return Swipe(new Vector2(Screen.width * 0.15f, Screen.height * 0.5f), new Vector2(Screen.width * 0.85f, Screen.height * 0.5f));
                menu.DemoBack();
                yield return Wait(1.2f);

                // 3. Dose calculation
                yield return Tap(UiPos("Dose", new Vector2(0.25f, 0.3f)));
                menu.DemoGo("dose");
                yield return Wait(3.0f);
                Shot("03_dose");
                menu.DemoBack();
                yield return Wait(1.0f);

                // 4. Knowledge test
                yield return Tap(UiPos("Knowledge", new Vector2(0.75f, 0.3f)));
                menu.DemoGo("quiz");
                yield return Wait(2.6f);
                Shot("04_quiz");
                menu.DemoBack();
                yield return Wait(1.0f);

                // 5. open the AR clinical simulation
                yield return Tap(UiPos("AR Clinical", new Vector2(0.25f, 0.45f)));
            }
            SceneManager.LoadScene("ARSim");
            yield return Wait(2.2f);
            var sim = FindFirstObjectByType<ARSimController>();
            var ward = FindFirstObjectByType<Ward>();
            var orbit = FreeOrbitMode.Active;

            // slow orbit around the bedside while the briefing shows
            if (orbit != null) yield return Orbit(orbit, 195f, 30f, 4.2f, 235f, 24f, 3.4f, new Vector3(0.05f, 0.75f, 0.15f), new Vector3(-0.2f, 0.75f, 0.1f), 4.5f);
            Shot("05_ar_briefing");
            if (sim != null)
            {
                foreach (var i in new[] { 1, 2, 4 })
                {
                    sim.DebugShowStep(i);
                    yield return Wait(2.6f);
                }
                Shot("06_ar_steps");
            }

            // close-up of the patient's face, then the patellar reflex test (labels hidden for clean close-ups)
            if (ward != null) ward.SetLabelsVisible(false);
            if (orbit != null) yield return Orbit(orbit, 235f, 24f, 3.4f, 255f, 38f, 1.5f, new Vector3(-0.2f, 0.75f, 0.1f), new Vector3(-0.75f, 0.8f, 0f), 3.0f);
            yield return Wait(1.5f);
            Shot("07_face");
            if (orbit != null) yield return Orbit(orbit, 255f, 38f, 1.5f, 192f, 52f, 1.8f, new Vector3(-0.75f, 0.8f, 0f), new Vector3(0.32f, 0.8f, 0.12f), 2.5f);
            if (ward != null && ward.Items.TryGetValue("knee", out var knee))
            {
                var cam = orbit != null ? orbit.Cam : Camera.main;
                Vector2 sp = cam != null ? (Vector2)cam.WorldToScreenPoint(knee.transform.position + Vector3.right * 0.38f) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                yield return Tap(sp);
                ward.Kick(2);
                yield return Wait(1.0f);
                Shot("08_reflex");          // knee lifted, lower leg hanging, mid-kick
                yield return Wait(1.9f);
            }

            // toxicity explorer: magnesium rising from therapeutic to toxic
            if (sim != null)
            {
                if (orbit != null) StartCoroutine(Orbit(orbit, 192f, 52f, 1.8f, 200f, 28f, 3.2f, new Vector3(0.32f, 0.8f, 0.12f), new Vector3(-0.55f, 0.95f, 0.2f), 3.0f));
                for (float mg = 4f; mg <= 16f; mg += 0.5f)
                {
                    sim.DebugExplorer(mg);
                    yield return Wait(0.3f);
                }
                yield return Wait(2.0f);
                Shot("09_explorer");
            }
            // closing wide shot of the whole bedside
            if (ward != null) ward.SetLabelsVisible(true);
            if (orbit != null) yield return Orbit(orbit, 200f, 28f, 3.2f, 160f, 32f, 4.4f, new Vector3(-0.55f, 0.95f, 0.2f), new Vector3(0.05f, 0.75f, 0.15f), 4.0f);
            yield return Wait(1.5f);
            Shot("10_overview");
            yield return Wait(0.5f);
            recording = false;
            yield return null;
            File.WriteAllText(Path.Combine(frameDir, "done.txt"), frame + " frames @ " + Fps + " fps");
            Application.Quit();
        }

        IEnumerator Orbit(FreeOrbitMode o, float y0, float p0, float d0, float y1, float p1, float d1, Vector3 t0, Vector3 t1, float dur)
        {
            float t = 0;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Ease.InOutSine(Mathf.Clamp01(t / dur));
                o.SetView(Mathf.Lerp(y0, y1, k), Mathf.Lerp(p0, p1, k), Mathf.Lerp(d0, d1, k), Vector3.Lerp(t0, t1, k));
                yield return null;
            }
        }
    }
}
#endif
