#if MGSO4_READY
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MgSO4AR
{
    /// <summary>
    /// Measures how much the phone is moving (shake, hand tremor, walking) from the accelerometer
    /// and gyroscope and exposes a smoothed 0..1 "Intensity". UI pulses and decorative heartbeat
    /// lines use it so the animation beats harder and faster when the device is shaken.
    /// (Clinical values shown for the virtual patient are never affected.)
    /// </summary>
    public class MotionSense : MonoBehaviour
    {
        static MotionSense inst;
        public static float Intensity => Ensure().intensity;     // smoothed 0..1
        public static float Raw => Ensure().raw;                 // instantaneous 0..1

        float intensity, raw, envelope;
        /// <summary>Number of on-screen elements that want the phone to "feel" its own movement.</summary>
        public static int HapticSources;
        public static float Envelope => Ensure().envelope;      // fast-following 0..1 (for haptics)
        Vector3 gravity;
        bool enabledSensors;

        static MotionSense Ensure()
        {
            if (inst == null)
            {
                var go = new GameObject("MotionSense");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<MotionSense>();
            }
            return inst;
        }

        float nextEnableTry;
        void EnableSensors()
        {
            if (enabledSensors || Time.time < nextEnableTry) return;
            nextEnableTry = Time.time + 0.5f;
#if ENABLE_INPUT_SYSTEM
            var acc = Accelerometer.current;
            var gyr = UnityEngine.InputSystem.Gyroscope.current;
            if (acc != null && !acc.enabled) InputSystem.EnableDevice(acc);
            if (gyr != null && !gyr.enabled) InputSystem.EnableDevice(gyr);
            if (acc != null) acc.samplingFrequency = 60;
            if (gyr != null) gyr.samplingFrequency = 60;
            enabledSensors = (acc == null || acc.enabled) && (gyr == null || gyr.enabled) && (acc != null || gyr != null);
#else
            enabledSensors = true;
#endif
        }

        void Update()
        {
            EnableSensors();
            Vector3 acc = Vector3.zero, gyro = Vector3.zero;
            bool has = false;
#if ENABLE_INPUT_SYSTEM
            if (Accelerometer.current != null && Accelerometer.current.enabled) { acc = Accelerometer.current.acceleration.ReadValue(); has = true; }
            if (UnityEngine.InputSystem.Gyroscope.current != null && UnityEngine.InputSystem.Gyroscope.current.enabled) { gyro = UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue(); has = true; }
#elif ENABLE_LEGACY_INPUT_MANAGER
            acc = Input.acceleration; has = SystemInfo.supportsAccelerometer;
#endif
            float dt = Mathf.Max(Time.deltaTime, 1e-3f);
            float shake = 0f;
            if (has)
            {
                // high-pass filter: remove gravity, keep the user's movement (in g)
                if (gravity == Vector3.zero) gravity = acc;
                gravity = Vector3.Lerp(gravity, acc, 1f - Mathf.Exp(-dt * 2.5f));
                float lin = (acc - gravity).magnitude;          // ~0.02 still, 0.3 shaking, 1+ hard shake
                float rot = gyro.magnitude;                      // rad/s
                // dead-zone so a phone lying still or held calmly reads 0
                shake = Mathf.Clamp01(Mathf.Max((lin - 0.04f) / 0.6f, (rot - 0.15f) / 4f));
            }
#if (UNITY_EDITOR || UNITY_STANDALONE) && ENABLE_INPUT_SYSTEM
            // desktop preview: fast mouse movement stands in for shaking
            if (Mouse.current != null) shake = Mathf.Max(shake, Mathf.Clamp01(Mouse.current.delta.ReadValue().magnitude / 60f));
#endif
            raw = shake;
            // fast attack, slow release
            float k = shake > intensity ? 1f - Mathf.Exp(-dt * 12f) : 1f - Mathf.Exp(-dt * 1.6f);
            intensity = Mathf.Lerp(intensity, shake, k);
            // fast envelope that tracks the movement itself (~40 ms attack, ~100 ms release)
            float ke = shake > envelope ? 1f - Mathf.Exp(-dt * 25f) : 1f - Mathf.Exp(-dt * 10f);
            envelope = Mathf.Lerp(envelope, shake, ke);
            // continuous vibration whose strength follows how the phone is moving (not beats)
            Haptics.Motion(HapticSources > 0 ? envelope : 0f);
        }
    }
}
#endif
