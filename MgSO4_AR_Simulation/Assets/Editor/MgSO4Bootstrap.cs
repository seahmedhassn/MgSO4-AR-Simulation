// Phase 1 of the command-line build. Compiles with NO extra packages or defines:
// installs the packages the app needs, enables the scripting defines and input handling,
// then quits. Phase 2 (MgSO4Builder.BuildAndroid) runs in a fresh editor process.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class MgSO4Bootstrap
{
    static readonly string[] RequiredPackages = { "com.unity.ugui", "com.unity.inputsystem", "com.unity.xr.arfoundation", "com.unity.xr.arcore" };

    public static void Run()
    {
        int code = 0;
        try
        {
            Prepare();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    [MenuItem("MgSO4 AR/0. Install packages + defines")]
    public static void Prepare()
    {
        var installed = ListPackages();
        foreach (var id in RequiredPackages)
        {
            if (installed.Contains(id)) { Debug.Log("[MgSO4] package present: " + id); continue; }
            Debug.Log("[MgSO4] adding package: " + id);
            AddRequest add = Client.Add(id);
            Wait(add);
            if (add.Status == StatusCode.Success) Debug.Log("[MgSO4] added " + add.Result.packageId);
            else Debug.LogError("[MgSO4] failed to add " + id + ": " + add.Error?.message);
        }

        installed = ListPackages();
        bool vuforia = installed.Contains("com.ptc.vuforia.engine");
        bool arf = installed.Contains("com.unity.xr.arfoundation") && installed.Contains("com.unity.xr.arcore");
        Debug.Log("[MgSO4] AR Foundation + ARCore present: " + arf);
        Debug.Log("[MgSO4] Vuforia Engine present: " + vuforia);

        foreach (var nbt in new[] { NamedBuildTarget.Android, NamedBuildTarget.Standalone })
        {
            var defs = PlayerSettings.GetScriptingDefineSymbols(nbt)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            if (!defs.Contains("MGSO4_READY")) defs.Add("MGSO4_READY");
            defs.Remove("VUFORIA_PRESENT");
            if (vuforia) defs.Add("VUFORIA_PRESENT");
            defs.Remove("ARF_PRESENT");
            if (arf) defs.Add("ARF_PRESENT");
            PlayerSettings.SetScriptingDefineSymbols(nbt, string.Join(";", defs));
            Debug.Log("[MgSO4] defines " + nbt.TargetName + ": " + string.Join(";", defs));
        }

        // Active Input Handling = Input System package only (Unity 6.6 warns that "Both" is unsupported on Android)
        var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (ps != null)
        {
            var so = new SerializedObject(ps);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null) { prop.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); Debug.Log("[MgSO4] activeInputHandler = Input System"); }
            else Debug.LogWarning("[MgSO4] activeInputHandler property not found");
        }
        AssetDatabase.SaveAssets();
    }

    static System.Collections.Generic.HashSet<string> ListPackages()
    {
        ListRequest list = Client.List(true, false);
        Wait(list);
        var set = new System.Collections.Generic.HashSet<string>();
        if (list.Status == StatusCode.Success)
            foreach (var p in list.Result) set.Add(p.name);
        else Debug.LogError("[MgSO4] package list failed: " + list.Error?.message);
        return set;
    }

    static void Wait(Request r)
    {
        var start = DateTime.Now;
        while (!r.IsCompleted)
        {
            Thread.Sleep(100);
            if ((DateTime.Now - start).TotalMinutes > 15) throw new TimeoutException("Package Manager request timed out");
        }
    }
}
