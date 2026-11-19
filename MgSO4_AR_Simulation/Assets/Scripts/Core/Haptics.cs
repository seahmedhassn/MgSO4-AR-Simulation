#if MGSO4_READY
using UnityEngine;

namespace MgSO4AR
{
    /// <summary>
    /// Vibration feedback through Android's Vibrator service (predefined click/tick effects on
    /// Android 10+, custom waveforms for heartbeat / seizure / alarm). Can be switched off by the user.
    /// </summary>
    public static class Haptics
    {
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("haptics", 1) == 1;
            set { PlayerPrefs.SetInt("haptics", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        static float lastTap;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static AndroidJavaClass effectClass;
        static int sdk = -1;
        static bool init, available, amplitudeControl;

        static void Init()
        {
            if (init) return;
            init = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                available = vibrator != null && vibrator.Call<bool>("hasVibrator");
                if (sdk >= 26) effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                amplitudeControl = available && sdk >= 26 && vibrator.Call<bool>("hasAmplitudeControl");
            }
            catch (System.Exception e) { Debug.LogWarning("[MgSO4] haptics: " + e.Message); available = false; }
        }

        static void Predefined(int effectId, long fallbackMs, int fallbackAmp)
        {
            Init();
            if (!available) return;
            try
            {
                if (sdk >= 29)
                {
                    using (var eff = effectClass.CallStatic<AndroidJavaObject>("createPredefined", effectId)) vibrator.Call("vibrate", eff);
                }
                else OneShot(fallbackMs, fallbackAmp);
            }
            catch (System.Exception e) { Debug.LogWarning("[MgSO4] haptics: " + e.Message); }
        }

        static void OneShot(long ms, int amp)
        {
            Init();
            if (!available) return;
            try
            {
                if (sdk >= 26)
                {
                    using (var eff = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, Mathf.Clamp(amp, 1, 255))) vibrator.Call("vibrate", eff);
                }
                else vibrator.Call("vibrate", ms);
            }
            catch (System.Exception e) { Debug.LogWarning("[MgSO4] haptics: " + e.Message); }
        }

        static void Waveform(long[] timings, int[] amps)
        {
            Init();
            if (!available) return;
            try
            {
                if (sdk >= 26)
                {
                    using (var eff = effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amps, -1)) vibrator.Call("vibrate", eff);
                }
                else vibrator.Call("vibrate", timings, -1);
            }
            catch (System.Exception e) { Debug.LogWarning("[MgSO4] haptics: " + e.Message); }
        }
#endif

        /// <summary>Very light tick for button presses (rate limited).</summary>
        public static void Tap()
        {
            if (!Enabled || Time.unscaledTime - lastTap < 0.06f) return;
            lastTap = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            Predefined(2 /*EFFECT_TICK*/, 10, 60);
#endif
        }

        /// <summary>Crisp click (selecting an option, tapping an object in the ward).</summary>
        public static void Click()
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Predefined(0 /*EFFECT_CLICK*/, 18, 120);
#endif
        }

        public static void Success()
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Waveform(new long[] { 0, 25, 70, 35 }, new[] { 0, 140, 0, 220 });
#endif
        }

        public static void Error()
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Waveform(new long[] { 0, 60, 60, 60 }, new[] { 0, 255, 0, 255 });
#endif
        }

        /// <summary>Gentle "lub-dub" heartbeat (splash). strength 0..1.</summary>
        public static void Heartbeat(float strength = 0.4f)
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Init();
            if (!amplitudeControl) { if (strength > 0.5f) Predefined(2 /*TICK*/, 8, 40); return; }
            int a = Mathf.RoundToInt(Mathf.Lerp(20, 90, Mathf.Clamp01(strength)));
            Waveform(new long[] { 0, 14, 120, 12 }, new[] { 0, a, 0, Mathf.RoundToInt(a * 0.6f) });
#endif
        }

        static float motionNext; static bool motionOn;
        /// <summary>
        /// Continuous vibration that follows the phone's own movement (level 0..1, called every frame):
        /// nothing while still, barely noticeable for a small tremor, clearly noticeable for a hard
        /// shake - never strong. Implemented as overlapping short one-shots re-issued every 40 ms
        /// with the current amplitude, so the feeling rises and falls with the motion instead of
        /// pulsing. Phones without amplitude control are skipped (they only vibrate at full power).
        /// </summary>
        public static void Motion(float level)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const float threshold = 0.06f;
            if (!Enabled || level < threshold)
            {
                if (motionOn) { motionOn = false; Init(); try { if (available) vibrator.Call("cancel"); } catch { } }
                return;
            }
            if (Time.unscaledTime < motionNext) return;
            Init();
            if (!available || !amplitudeControl) return;
            motionNext = Time.unscaledTime + 0.04f;
            motionOn = true;
            float k = Mathf.Clamp01((level - threshold) / (1f - threshold));
            OneShot(60, Mathf.RoundToInt(Mathf.Lerp(3, 60, Mathf.Pow(k, 1.3f))));
#endif
        }

        /// <summary>Irregular rumble for an eclamptic seizure (~1.5 s).</summary>
        public static void Seizure()
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            var t = new long[16]; var a = new int[16];
            for (int i = 0; i < 16; i++) { t[i] = i % 2 == 0 ? Random.Range(40, 90) : Random.Range(30, 70); a[i] = i % 2 == 0 ? Random.Range(150, 256) : 0; }
            Waveform(t, a);
#endif
        }

        /// <summary>Monitor alarm: three short buzzes.</summary>
        public static void Alarm()
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Waveform(new long[] { 0, 80, 90, 80, 90, 80 }, new[] { 0, 200, 0, 200, 0, 200 });
#endif
        }

        // Referenced so Unity adds the VIBRATE permission to the Android manifest.
#if UNITY_ANDROID || UNITY_IOS
        static void EnsurePermission() { if (Time.frameCount < 0) Handheld.Vibrate(); }
#endif
    }
}
#endif
