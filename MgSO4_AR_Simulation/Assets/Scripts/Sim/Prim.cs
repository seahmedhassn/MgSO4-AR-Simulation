#if MGSO4_READY
using System.Collections.Generic;
using UnityEngine;

namespace MgSO4AR
{
    /// <summary>
    /// Loads the material templates (created by the editor builder in Resources/SimMaterials, which
    /// guarantees the shaders are in the build) and the AR marker texture. No serialized fields, so the
    /// scenes stay minimal.
    /// </summary>
    public class SimAssets : MonoBehaviour
    {
        void Awake() => Init();

        public static void Init()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;   // never dim/turn off during training
            if (Prim.BaseLit == null) Prim.BaseLit = Resources.Load<Material>("SimMaterials/SimLit");
            if (Prim.BaseUnlit == null) Prim.BaseUnlit = Resources.Load<Material>("SimMaterials/SimUnlit");
            if (Prim.BaseTransparent == null) Prim.BaseTransparent = Resources.Load<Material>("SimMaterials/SimTransparent");
            if (Prim.Marker == null) Prim.Marker = Resources.Load<Texture2D>("MgSO4_Marker");
            UIKit.EnsureEventSystem();
        }
    }

    /// <summary>Procedural geometry helpers for the virtual ward.</summary>
    public static class Prim
    {
        public static Material BaseLit, BaseUnlit, BaseTransparent;
        public static Texture2D Marker;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        static Material Template(bool transparent)
        {
            if (transparent && BaseTransparent != null) return BaseTransparent;
            if (BaseLit != null) return BaseLit;
            var sh = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
            BaseLit = new Material(sh);
            return BaseLit;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        public static Color GetColor(Material m)
        {
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        public static Material Mat(Color c, bool transparent = false, float smooth = -1f)
        {
            if (smooth < 0) smooth = transparent ? 0.7f : 0.25f;
            string key = (transparent ? "T" : "O") + ColorUtility.ToHtmlStringRGBA(c) + Mathf.RoundToInt(smooth * 100);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Template(transparent));
            SetColor(m, c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            cache[key] = m;
            return m;
        }

        public enum Surface { Opaque, Transparent, Cutout }
        public static Material BaseCutout;
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        /// <summary>Texture from Resources/Models/Tex/&lt;name&gt; (null if empty or missing).</summary>
        public static Texture2D LoadTex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (texCache.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("Models/Tex/" + name);
            texCache[name] = t;
            return t;
        }

        public static bool TextureExists(string[] names)
        {
            if (names == null) return false;
            foreach (var n in names) if (LoadTex(n) != null) return true;
            return false;
        }

        /// <summary>Material with an optional albedo texture (tinted by c) and surface mode.</summary>
        public static Material MatTex(Color c, string tex, Surface mode, float smooth = -1f, float cutoff = 0.45f)
        {
            var t = LoadTex(tex);
            if (t == null && mode != Surface.Cutout) return Mat(c, mode == Surface.Transparent, smooth);
            if (smooth < 0) smooth = 0.3f;
            string key = "X" + mode + ColorUtility.ToHtmlStringRGBA(c) + (tex ?? "") + Mathf.RoundToInt(smooth * 100) + "_" + Mathf.RoundToInt(cutoff * 100);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            if (BaseCutout == null) BaseCutout = Resources.Load<Material>("SimMaterials/SimCutout");
            var template = mode == Surface.Cutout ? (BaseCutout != null ? BaseCutout : Template(false)) : Template(mode == Surface.Transparent);
            m = new Material(template);
            SetColor(m, c);
            if (t != null)
            {
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            }
            if (mode == Surface.Cutout)
            {
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = 2450;
            }
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            cache[key] = m;
            return m;
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(BaseUnlit != null ? BaseUnlit : Template(false));
            SetColor(m, c);
            return m;
        }

        public static GameObject Make(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color,
            string name = null, Vector3? euler = null, bool transparent = false)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.name = name ?? type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Mat(color, transparent);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public static Transform Group(string name, Transform parent, Vector3 pos, Vector3? euler = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            return go.transform;
        }

        /// <summary>Thin cylinder between two local points (tubing, cables).</summary>
        public static GameObject Tube(Transform parent, Vector3 a, Vector3 b, float diameter, Color c, bool transparent = false)
        {
            var mid = (a + b) * 0.5f;
            var dir = b - a;
            var go = Make(PrimitiveType.Cylinder, parent, mid, new Vector3(diameter, dir.magnitude * 0.5f, diameter), c, "Tube", null, transparent);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            return go;
        }
    }

    /// <summary>A tappable object in the ward. Pulses when highlighted.</summary>
    public class Interactable : MonoBehaviour
    {
        public string Id;
        public string DisplayName;
        readonly List<Material> mats = new List<Material>();
        readonly List<Color> baseColors = new List<Color>();
        Renderer[] rends;
        bool highlighted;

        public void Init(string id, string display)
        {
            Id = id;
            DisplayName = display;
            rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r is MeshRenderer == false && !(r is SkinnedMeshRenderer)) continue;   // skip UI/world-text
                var ms = r.materials;            // per-renderer instances (all submeshes)
                foreach (var m in ms) { mats.Add(m); baseColors.Add(Prim.GetColor(m)); }
            }
            AutoCollider();
        }

        public void RefreshBaseColors()
        {
            for (int i = 0; i < mats.Count; i++) if (mats[i]) baseColors[i] = Prim.GetColor(mats[i]);
        }

        void AutoCollider()
        {
            bool first = true;
            var b = new Bounds();
            foreach (var r in rends)
            {
                if (!(r is MeshRenderer)) continue;
                var rb = r.bounds;
                var c = rb.center; var e = rb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    var lp = transform.InverseTransformPoint(corner);
                    if (first) { b = new Bounds(lp, Vector3.zero); first = false; }
                    else b.Encapsulate(lp);
                }
            }
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size + Vector3.one * 0.04f;
        }

        public void SetHighlight(bool on)
        {
            highlighted = on;
            if (!on)
                for (int i = 0; i < mats.Count; i++) if (mats[i]) Prim.SetColor(mats[i], baseColors[i]);
        }

        void Update()
        {
            if (!highlighted) return;
            float k = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f * 0.6f;
            for (int i = 0; i < mats.Count; i++)
                if (mats[i])
                {
                    var c = Color.Lerp(baseColors[i], Theme.Highlight, k);
                    c.a = baseColors[i].a;
                    Prim.SetColor(mats[i], c);
                }
        }
    }
}
#endif
