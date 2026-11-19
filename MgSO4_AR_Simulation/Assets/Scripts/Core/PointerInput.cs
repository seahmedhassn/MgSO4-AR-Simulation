#if MGSO4_READY
using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MgSO4AR
{
    /// <summary>Tiny input abstraction that works with either the legacy Input Manager or the Input System.</summary>
    public static class PointerInput
    {
        public static int TouchCount
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.touchCount;
#elif ENABLE_INPUT_SYSTEM
                if (Touchscreen.current == null) return 0;
                int n = 0;
                foreach (var t in Touchscreen.current.touches) if (t.press.isPressed) n++;
                return n;
#else
                return 0;
#endif
            }
        }

        public static Vector2 TouchPos(int i)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetTouch(i).position;
#elif ENABLE_INPUT_SYSTEM
            int n = 0;
            foreach (var t in Touchscreen.current.touches)
                if (t.press.isPressed) { if (n == i) return t.position.ReadValue(); n++; }
            return Vector2.zero;
#else
            return Vector2.zero;
#endif
        }

        /// <summary>Primary pointer pressed this frame.</summary>
        public static bool Down(out Vector2 pos)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                pos = t.position;
                return t.phase == TouchPhase.Began && Input.touchCount == 1;
            }
            pos = Input.mousePosition;
            return Input.GetMouseButtonDown(0);
#elif ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                pos = Touchscreen.current.primaryTouch.position.ReadValue();
                return Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
            }
            if (Mouse.current != null)
            {
                pos = Mouse.current.position.ReadValue();
                return Mouse.current.leftButton.wasPressedThisFrame;
            }
            pos = Vector2.zero; return false;
#else
            pos = Vector2.zero; return false;
#endif
        }

        /// <summary>Primary pointer currently held.</summary>
        public static bool Held(out Vector2 pos)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0) { pos = Input.GetTouch(0).position; return true; }
            pos = Input.mousePosition;
            return Input.GetMouseButton(0);
#elif ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            { pos = Touchscreen.current.primaryTouch.position.ReadValue(); return true; }
            if (Mouse.current != null) { pos = Mouse.current.position.ReadValue(); return Mouse.current.leftButton.isPressed; }
            pos = Vector2.zero; return false;
#else
            pos = Vector2.zero; return false;
#endif
        }

        /// <summary>Primary pointer released this frame.</summary>
        public static bool Up(out Vector2 pos)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                pos = t.position;
                return t.phase == TouchPhase.Ended;
            }
            pos = Input.mousePosition;
            return Input.GetMouseButtonUp(0);
#elif ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                pos = Touchscreen.current.primaryTouch.position.ReadValue();
                if (Touchscreen.current.primaryTouch.press.wasReleasedThisFrame) return true;
            }
            if (Mouse.current != null) { pos = Mouse.current.position.ReadValue(); return Mouse.current.leftButton.wasReleasedThisFrame; }
            pos = Vector2.zero; return false;
#else
            pos = Vector2.zero; return false;
#endif
        }

        public static float Scroll
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mouseScrollDelta.y;
#elif ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
                return 0f;
#endif
            }
        }

        // ---------- gyroscope ----------
        public static bool EnableGyro()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!SystemInfo.supportsGyroscope) return false;
            Input.gyro.enabled = true;
            return true;
#elif ENABLE_INPUT_SYSTEM
            if (AttitudeSensor.current == null) return false;
            InputSystem.EnableDevice(AttitudeSensor.current);
            return true;
#else
            return false;
#endif
        }

        public static Quaternion GyroAttitude
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.gyro.attitude;
#elif ENABLE_INPUT_SYSTEM
                return AttitudeSensor.current != null ? AttitudeSensor.current.attitude.ReadValue() : Quaternion.identity;
#else
                return Quaternion.identity;
#endif
            }
        }

        public static void DisableGyro()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (SystemInfo.supportsGyroscope) Input.gyro.enabled = false;
#elif ENABLE_INPUT_SYSTEM
            if (AttitudeSensor.current != null) InputSystem.DisableDevice(AttitudeSensor.current);
#endif
        }
    }
}
#endif
