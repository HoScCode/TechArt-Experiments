// Version: BoidFollowCamera_v001
//
// Simple 3rd-Person-Kamera für die Player-Kugel:
//   - folgt dem Ziel weich
//   - Rechte Maustaste + Ziehen = Orbit (Yaw/Pitch)
//   - Mausrad = Zoom
//
// Setup: auf die Main Camera legen, Target = Player-Kugel.
// Kompatibel mit Legacy-Input UND neuem Input System.

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class BoidFollowCamera : MonoBehaviour
{
    [Header("Target")]

    [SerializeField] private Transform target;

    [Tooltip("Blickpunkt-Offset über dem Ziel.")]
    [SerializeField] private Vector3 lookOffset = new Vector3(0f, 0.4f, 0f);

    [Header("Distance / Zoom")]

    [SerializeField, Min(0.5f)] private float distance = 8f;
    [SerializeField, Min(0.5f)] private float minDistance = 2.5f;
    [SerializeField, Min(1f)] private float maxDistance = 25f;
    [SerializeField, Min(0.1f)] private float zoomSpeed = 3f;

    [Header("Orbit")]

    [Tooltip("Grad pro Pixel Mausbewegung.")]
    [SerializeField, Range(0.05f, 1f)] private float orbitSensitivity = 0.25f;

    [SerializeField, Range(-85f, 0f)] private float minPitch = -25f;
    [SerializeField, Range(0f, 85f)] private float maxPitch = 75f;

    [Header("Smoothing")]

    [Tooltip("Wie schnell die Kamera der Zielposition folgt.")]
    [SerializeField, Range(1f, 30f)] private float followResponsiveness = 10f;

    private float yaw;
    private float pitch = 25f;
    private Vector3 smoothedPivot;
    private bool initialized;

    private void Start()
    {
        yaw = transform.eulerAngles.y;

        if (target != null)
        {
            smoothedPivot = target.position + lookOffset;
            initialized = true;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // -----------------------------------------------------------------
        // Orbit (RMB-Drag) und Zoom (Mausrad)
        // -----------------------------------------------------------------
        if (OrbitHeld())
        {
            Vector2 delta = MouseDelta();

            yaw += delta.x * orbitSensitivity;
            pitch -= delta.y * orbitSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        float scroll = ScrollDelta();

        if (Mathf.Abs(scroll) > 0.001f)
        {
            distance = Mathf.Clamp(
                distance - scroll * zoomSpeed,
                minDistance,
                maxDistance);
        }

        // -----------------------------------------------------------------
        // Weich folgen und positionieren
        // -----------------------------------------------------------------
        Vector3 pivot = target.position + lookOffset;

        if (!initialized)
        {
            smoothedPivot = pivot;
            initialized = true;
        }

        float blend =
            1f - Mathf.Exp(-followResponsiveness * Time.deltaTime);

        smoothedPivot = Vector3.Lerp(smoothedPivot, pivot, blend);

        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);

        transform.position =
            smoothedPivot + orbitRotation * new Vector3(0f, 0f, -distance);

        transform.rotation =
            Quaternion.LookRotation(
                smoothedPivot - transform.position,
                Vector3.up);
    }


    // ---------------------------------------------------------------------
    // Input-Abstraktion (Legacy-Input oder neues Input System)
    // ---------------------------------------------------------------------

    private bool OrbitHeld()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#elif ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.rightButton.isPressed;
#else
        return false;
#endif
    }

    private Vector2 MouseDelta()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(
            Input.GetAxis("Mouse X") * 12f,
            Input.GetAxis("Mouse Y") * 12f);
#elif ENABLE_INPUT_SYSTEM
        return Mouse.current != null
            ? Mouse.current.delta.ReadValue()
            : Vector2.zero;
#else
        return Vector2.zero;
#endif
    }

    private float ScrollDelta()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mouseScrollDelta.y;
#elif ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
            return 0f;

        // Das neue Input System liefert Scroll oft in ±120er-Schritten.
        float raw = Mouse.current.scroll.ReadValue().y;
        return Mathf.Abs(raw) > 10f ? raw / 120f : raw;
#else
        return 0f;
#endif
    }
}
