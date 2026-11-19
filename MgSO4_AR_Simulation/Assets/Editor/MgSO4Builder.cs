#if MGSO4_READY
using System;
using System.IO;
using System.Linq;
using MgSO4AR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if VUFORIA_PRESENT
using Vuforia;
#endif
#if ARF_PRESENT
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;
#endif

/// <summary>
/// Generates the scenes, materials and player settings from code and builds the Android APK.
/// Menu: "MgSO4 AR/..." - or command line: -executeMethod MgSO4Builder.BuildAndroid
/// </summary>
public static class MgSO4Builder
{
    const string MenuScene = "Assets/Scenes/Menu.unity";
    const string ARScene = "Assets/Scenes/ARSim.unity";
    const string MarkerPath = "Assets/Resources/MgSO4_Marker.png";
    public const string AppId = "edu.mansoura.nursing.mgso4ar";

    static string ApkPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/MgSO4_AR_Sim.apk"));

    public static void BuildAndroid()
    {
        int code = 1;
        try
        {
            SetupProject();
            code = Build() ? 0 : 1;
        }
        catch (Exception e) { Debug.LogException(e); }
        EditorApplication.Exit(code);
    }

    /// <summary>Windows build used only to preview the UI/3D on a desktop (3D and webcam modes).</summary>
    public static void BuildWindowsPreview()
    {
        int code = 1;
        try
        {
            SetupProject();
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;
            var exe = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/WindowsPreview/MgSO4_AR_Sim.exe"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MenuScene, ARScene }, locationPathName = exe,
                target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.None
            });
            Debug.Log($"[MgSO4] WINDOWS BUILD {report.summary.result} -> {exe}");
            code = report.summary.result == BuildResult.Succeeded ? 0 : 1;
        }
        catch (Exception e) { Debug.LogException(e); }
        EditorApplication.Exit(code);
    }

    [MenuItem("MgSO4 AR/1. Set up scenes + settings")]
    public static void SetupProject()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Resources/SimMaterials");
        AssetDatabase.Refresh();

        ConfigureMarker();
        ConfigureIcons();
        var mats = CreateMaterials();
        var marker = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerPath);

        BuildMenuScene(mats, marker);
        BuildARScene(mats, marker);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuScene, true), new EditorBuildSettingsScene(ARScene, true) };

        ConfigurePlayer();
        ConfigureXR();
        ConfigureVuforia();
        AssetDatabase.SaveAssets();
        Debug.Log("[MgSO4] project set up");
    }

    [MenuItem("MgSO4 AR/2. Build Android APK")]
    public static void BuildFromMenu()
    {
        SetupProject();
        if (Build()) EditorUtility.RevealInFinder(ApkPath);
    }

    static bool Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { MenuScene, ARScene },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[MgSO4] BUILD {s.result}: {s.totalSize / (1024f * 1024f):0.0} MB, errors {s.totalErrors}, warnings {s.totalWarnings}, time {s.totalTime} -> {ApkPath}");
        return s.result == BuildResult.Succeeded;
    }

    // ------------------------------------------------------------------
    static void ConfigureMarker()
    {
        var ti = AssetImporter.GetAtPath(MarkerPath) as TextureImporter;
        if (ti == null) { Debug.LogWarning("[MgSO4] marker texture missing"); return; }
        ti.textureType = TextureImporterType.Default;
        ti.isReadable = true;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.None;
        ti.maxTextureSize = 2048;
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.RGB24;
        android.maxTextureSize = 2048;
        ti.SetPlatformTextureSettings(android);
        ti.SaveAndReimport();
    }

    static void ConfigureIcons()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Icons" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/AppIcon.png");
    }

    struct Mats { public Material lit, unlit, transparent; }

    static Mats CreateMaterials()
    {
        bool urp = GraphicsSettings.defaultRenderPipeline != null;
        var litShader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
        var unlitShader = Shader.Find(urp ? "Universal Render Pipeline/Unlit" : "Unlit/Color");
        var m = new Mats
        {
            lit = SaveMat("Assets/Resources/SimMaterials/SimLit.mat", litShader),
            unlit = SaveMat("Assets/Resources/SimMaterials/SimUnlit.mat", unlitShader),
            transparent = SaveMat("Assets/Resources/SimMaterials/SimTransparent.mat", litShader),
        };
        var t = m.transparent;
        if (urp)
        {
            t.SetFloat("_Surface", 1); t.SetFloat("_Blend", 0);
            t.SetOverrideTag("RenderType", "Transparent");
            t.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            t.SetFloat("_ZWrite", 0);
        }
        else
        {
            t.SetFloat("_Mode", 2);
            t.SetOverrideTag("RenderType", "Transparent");
            t.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            t.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            t.SetInt("_ZWrite", 0);
            t.DisableKeyword("_ALPHATEST_ON");
            t.EnableKeyword("_ALPHABLEND_ON");
            t.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        t.renderQueue = (int)RenderQueue.Transparent;
        t.color = new Color(1, 1, 1, 0.5f);
        EditorUtility.SetDirty(t);

        // alpha-tested, double-sided-by-geometry material for hair cards / lashes / brows (textured models)
        var cut = SaveMat("Assets/Resources/SimMaterials/SimCutout.mat", litShader);
        if (urp)
        {
            cut.SetFloat("_AlphaClip", 1); cut.SetFloat("_Cutoff", 0.5f);
            cut.EnableKeyword("_ALPHATEST_ON");
            cut.SetOverrideTag("RenderType", "TransparentCutout");
        }
        else
        {
            cut.SetFloat("_Mode", 1); cut.SetFloat("_Cutoff", 0.5f);
            cut.SetOverrideTag("RenderType", "TransparentCutout");
            cut.SetInt("_SrcBlend", (int)BlendMode.One);
            cut.SetInt("_DstBlend", (int)BlendMode.Zero);
            cut.SetInt("_ZWrite", 1);
            cut.EnableKeyword("_ALPHATEST_ON");
            cut.DisableKeyword("_ALPHABLEND_ON");
            cut.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        cut.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(cut);
        AssetDatabase.SaveAssets();
        return m;
    }

    static Material SaveMat(string path, Shader shader)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.25f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject AddAssets(Mats m, Texture2D marker)
    {
        // materials/marker are loaded from Resources at runtime; the component has no serialized fields
        var go = new GameObject("SimAssets");
        go.AddComponent<SimAssets>();
        return go;
    }

    static void AddLight()
    {
        var l = new GameObject("Sun").AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.1f;
        l.shadows = LightShadows.None;
        l.transform.rotation = Quaternion.Euler(50, -30, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
    }

    static void BuildMenuScene(Mats m, Texture2D marker)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Theme.Bg;
        AddLight();
        AddAssets(m, marker);
        new GameObject("Menu").AddComponent<MenuController>();
        EditorSceneManager.SaveScene(scene, MenuScene);
    }

    static void BuildARScene(Mats m, Texture2D marker)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("SimCamera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.17f, 0.23f);
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 60f;
        AddLight();
        AddAssets(m, marker);

        var ctlGo = new GameObject("ARSim");
        ctlGo.AddComponent<ARSimController>();

#if VUFORIA_PRESENT
        var vgo = new GameObject("VuforiaARCamera", typeof(Camera));
        var vcam = vgo.GetComponent<Camera>();
        vcam.clearFlags = CameraClearFlags.SolidColor;
        vcam.backgroundColor = Color.black;
        vcam.nearClipPlane = 0.02f;
        vcam.farClipPlane = 20f;
        vgo.AddComponent<VuforiaBehaviour>();
        vgo.SetActive(false);
#endif
        EditorSceneManager.SaveScene(scene, ARScene);
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "Mansoura University Faculty of Nursing";
        PlayerSettings.productName = "MgSO4 AR Sim";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.Android.bundleVersionCode = 1;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AppId);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)29;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.Android.forceInternetPermission = true;   // Vuforia licence check
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/AppIcon.png");
        if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
    }

    /// <summary>
    /// Records acceptance of the Vuforia Developer Agreement (approved by the project owner) - the
    /// equivalent of clicking "Accept" in Help > Vuforia Engine > Show Developer Agreement.
    /// The Vuforia editor API for this is internal, so it is located by reflection.
    /// </summary>
    static void AcceptVuforiaEula()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Vuforia.Unity.Editor");
        if (asm == null) { Debug.LogWarning("[MgSO4] Vuforia editor assembly not found"); return; }
        const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
        Type[] types;
        try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        bool done = false;
        foreach (var t in types)
            foreach (var m in t.GetMethods(All).Where(m => m.IsStatic && m.DeclaringType == t && m.Name.StartsWith("SetEulaAccepted", StringComparison.OrdinalIgnoreCase)))
            {
                var ps = m.GetParameters();
                try
                {
                    if (ps.Length == 0) { m.Invoke(null, null); done = true; }
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) { m.Invoke(null, new object[] { true }); done = true; }
                    Debug.Log($"[MgSO4] invoked {t.FullName}.{m.Name}");
                }
                catch (Exception e) { Debug.LogWarning($"[MgSO4] {m.Name} failed: {e.InnerException?.Message ?? e.Message}"); }
            }
        var util = types.FirstOrDefault(t => t.Name == "EulaVersioningUtility");
        if (util != null)
        {
            try
            {
                var inst = util.GetMethod("CreateDefaultEulEulaVersioningUtility", All)?.Invoke(null, null)
                           ?? Activator.CreateInstance(util, true);
                util.GetMethod("AcceptCurrentVuforiaVersion", All)?.Invoke(inst, null);
                var ok = util.GetMethod("HasUserAcceptedCurrentVersionEula", All)?.Invoke(inst, null);
                Debug.Log("[MgSO4] AcceptCurrentVuforiaVersion done, accepted = " + ok);
                done = true;
            }
            catch (Exception e) { Debug.LogWarning("[MgSO4] AcceptCurrentVuforiaVersion: " + (e.InnerException?.Message ?? e.Message)); }
        }
        var task = types.FirstOrDefault(t => t.Name == "SetEulaAcceptedInConfigTask");
        if (task != null)
        {
            try
            {
                var inst = Activator.CreateInstance(task, true);
                task.GetMethod("Execute", All)?.Invoke(inst, null);
                Debug.Log("[MgSO4] SetEulaAcceptedInConfigTask executed");
            }
            catch (Exception e) { Debug.LogWarning("[MgSO4] config task: " + (e.InnerException?.Message ?? e.Message)); }
        }
        Debug.Log("[MgSO4] EULA accept attempted: " + done);
    }

    /// <summary>
    /// XR Plug-in Management: ARCore loader for Android, NOT started automatically (the app starts it
    /// only for Surface mode, so it never competes with Vuforia). ARCore is "Optional" so the app still
    /// installs on phones without ARCore.
    /// </summary>
    static void ConfigureXR()
    {
#if ARF_PRESENT
        try
        {
            const string dir = "Assets/XR";
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(dir + "/XRGeneralSettingsPerBuildTarget.asset");
                if (perTarget == null)
                {
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, dir + "/XRGeneralSettingsPerBuildTarget.asset");
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = "Android Settings";
                AssetDatabase.AddObjectToAsset(settings, perTarget);
                perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, settings);
            }
            if (settings.Manager == null)
            {
                var mgr = ScriptableObject.CreateInstance<XRManagerSettings>();
                mgr.name = "Android Providers";
                AssetDatabase.AddObjectToAsset(mgr, perTarget);
                settings.Manager = mgr;
            }
            settings.InitManagerOnStart = false;
            bool ok = XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android);
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(settings.Manager); EditorUtility.SetDirty(perTarget);
            Debug.Log($"[MgSO4] XR: ARCore loader assigned={ok}, loaders={settings.Manager.activeLoaders.Count}, initOnStart={settings.InitManagerOnStart}");

            // ARCore requirement = Optional
            var arcoreSettingsType = Type.GetType("UnityEditor.XR.ARCore.ARCoreSettings, Unity.XR.ARCore.Editor");
            var get = arcoreSettingsType?.GetMethod("GetOrCreateSettings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var arcoreSettings = get?.Invoke(null, null);
            if (arcoreSettings != null)
            {
                var req = arcoreSettingsType.GetProperty("requirement");
                if (req != null) req.SetValue(arcoreSettings, Enum.Parse(req.PropertyType, "Optional"));
                // depth must be optional too, otherwise Google Play would hide the app from phones without a depth API
                var depth = arcoreSettingsType.GetProperty("depth");
                if (depth != null) depth.SetValue(arcoreSettings, Enum.Parse(depth.PropertyType, "Optional"));
                Debug.Log("[MgSO4] ARCore depth = " + depth?.GetValue(arcoreSettings));
                EditorUtility.SetDirty((UnityEngine.Object)arcoreSettings);
                Debug.Log("[MgSO4] ARCore requirement = " + req?.GetValue(arcoreSettings));
            }
            else Debug.LogWarning("[MgSO4] ARCoreSettings not found");
            AssetDatabase.SaveAssets();
        }
        catch (Exception e) { Debug.LogWarning("[MgSO4] XR config: " + e); }
#endif
    }

    static void SetByReflection(object target, string prop, object value)
    {
        try
        {
            var p = target.GetType().GetProperty(prop);
            if (p == null || !p.CanWrite) { Debug.LogWarning("[MgSO4] no writable property " + prop); return; }
            object v = value;
            if (p.PropertyType.IsEnum && value is string sv) v = Enum.Parse(p.PropertyType, sv);
            else if (p.PropertyType != value.GetType()) v = Convert.ChangeType(value, p.PropertyType);
            p.SetValue(target, v);
            Debug.Log($"[MgSO4] {prop} = {p.GetValue(target)}");
        }
        catch (Exception e) { Debug.LogWarning($"[MgSO4] set {prop}: {e.Message}"); }
    }

    static void ConfigureVuforia()
    {
#if VUFORIA_PRESENT
        AcceptVuforiaEula();
        try
        {
            var key = File.Exists("Assets/Resources/VuforiaLicense.txt") ? File.ReadAllText("Assets/Resources/VuforiaLicense.txt").Trim() : "";
            var cfg = VuforiaConfiguration.Instance;
            cfg.Vuforia.LicenseKey = key;
            cfg.Vuforia.DelayedInitialization = true;
            // Device tracking (Vuforia Fusion) is required for markerless Ground Plane ("Surface" mode).
            // ARCore is OPTIONAL so phones without ARCore can still install the app (Marker/Camera/3D modes).
            SetByReflection(cfg.DeviceTracker, "AutoInitAndStartTracker", true);
            SetByReflection(cfg.DeviceTracker, "ARCoreRequirementSetting", "OPTIONAL");
#if ARF_PRESENT
            // the ARCore XR Plug-in already ships the ARCore library -> don't import a second copy
            SetByReflection(cfg.DeviceTracker, "AutoImportArcoreSetting", false);
#else
            SetByReflection(cfg.DeviceTracker, "AutoImportArcoreSetting", true);
#endif
            EditorUtility.SetDirty(cfg);
            var eula = cfg.Vuforia.GetType().GetProperty("EULAAcceptedVersions");
            Debug.Log("[MgSO4] EULAAcceptedVersions = " + (eula != null ? eula.GetValue(cfg.Vuforia) : "n/a"));
            Debug.Log("[MgSO4] Vuforia configured (licence " + (key.Length > 0 ? "set" : "MISSING") + ", delayed init)");
        }
        catch (Exception e) { Debug.LogWarning("[MgSO4] Vuforia config: " + e.Message); }
#endif
    }
}
#if ARF_PRESENT
/// <summary>
/// Safety net for the ARCore manifest: the ARCore XR Plug-in only ever ADDS
/// &lt;uses-feature com.google.ar.core.depth required="true"&gt; (when its depth setting reads "Required",
/// e.g. a stale/fresh default ARCoreSettings instance) and never removes it, and the tag survived into
/// the APK even though ConfigureXR sets depth = Optional. Google Play would then hide the app from every
/// phone without the ARCore Depth API. This runs after ARCoreManifest (callbackOrder 2) and strips the tag.
/// </summary>
#pragma warning disable 0618
class MgSO4ManifestFixer : UnityEditor.Android.IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 100;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        try
        {
            string manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;
            var doc = new System.Xml.XmlDocument();
            doc.Load(manifestPath);
            const string androidNs = "http://schemas.android.com/apk/res/android";
            int removed = 0;
            var nodes = doc.GetElementsByTagName("uses-feature");
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var el = nodes[i] as System.Xml.XmlElement;
                if (el != null && el.GetAttribute("name", androidNs) == "com.google.ar.core.depth")
                {
                    el.ParentNode.RemoveChild(el);
                    removed++;
                }
            }
            if (removed > 0) doc.Save(manifestPath);
            Debug.Log("[MgSO4] Manifest fixer: removed " + removed + " com.google.ar.core.depth uses-feature tag(s) from " + manifestPath);
        }
        catch (Exception e) { Debug.LogWarning("[MgSO4] Manifest fixer: " + e.Message); }
    }
}
#pragma warning restore 0618
#endif
#endif
