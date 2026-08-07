// Version: BoidPlayerController_v001
//
// Frei fliegende Test-Kugel als Proto-Player für das Boid-Aquarium.
//   WASD  = Bewegung relativ zur Kamera (klassisch)
//   Q / E = absteigen / aufsteigen
//   Shift = Sprint (liegt über der Schreckschwelle der Boids ->
//           Gehen ist "unsichtbar", Sprinten löst die Panikwelle aus)
//
// Bewegung ist rein kinematisch (kein Rigidbody nötig). Mit gesetzter
// Obstacle Mask gleitet die Kugel an Collidern entlang statt durchzufliegen;
// mit gesetztem AquariumVolume bleibt sie im Becken.
//
// Setup: Sphere erstellen, dieses Script drauf, Obstacle Mask + optional
// Aquarium zuweisen. Kompatibel mit Legacy-Input UND neuem Input System.

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class BoidPlayerController : MonoBehaviour
{
    [Header("References")]

    [Tooltip("Kamera für die Bewegungsrichtung (WASD relativ zur Blickrichtung). Leer = Camera.main.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("Optional: hält den Player im Becken.")]
    [SerializeField] private AquariumVolume aquarium;

    [Header("Movement")]

    [Tooltip("Gehtempo. Bewusst UNTER der Schreckschwelle der Boids halten.")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 3.5f;

    [Tooltip("Sprint-Faktor (Shift). moveSpeed * Faktor sollte ÜBER der Schreckschwelle liegen.")]
    [SerializeField, Range(1f, 4f)] private float sprintMultiplier = 2.2f;

    [Tooltip("Wie schnell die Geschwindigkeit dem Input folgt (Units/s²).")]
    [SerializeField, Min(1f)] private float acceleration = 14f;

    [Header("Collision")]

    [Tooltip("Collider, an denen die Kugel entlanggleitet (z.B. die Säulen). Leer = keine Kollision.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("Radius der Kugel für die Kollisionsprüfung.")]
    [SerializeField, Min(0.05f)] private float bodyRadius = 0.5f;

    [SerializeField, Min(0f)] private float collisionMargin = 0.03f;

    private Vector3 velocity;

    /// <summary>Aktuelle Geschwindigkeit (für Sensorik/Debug).</summary>
    public Vector3 Velocity => velocity;

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        // -----------------------------------------------------------------
        // Input -> Wunschrichtung (kamera-relativ, vertikal über Q/E)
        // -----------------------------------------------------------------
        Vector3 input = ReadMoveInput(); // x = seitlich, y = vertikal, z = vor/zurück

        Camera cam = viewCamera != null ? viewCamera : Camera.main;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (cam != null)
        {
            forward = cam.transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            right = cam.transform.right;
            right.y = 0f;

            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();
        }

        Vector3 wishDirection =
            forward * input.z +
            right * input.x +
            Vector3.up * input.y;

        if (wishDirection.sqrMagnitude > 1f)
            wishDirection.Normalize();

        float targetSpeed =
            moveSpeed * (SprintHeld() ? sprintMultiplier : 1f);

        velocity = Vector3.MoveTowards(
            velocity,
            wishDirection * targetSpeed,
            acceleration * deltaTime);

        // -----------------------------------------------------------------
        // Bewegung mit Slide an Hindernissen (bis zu 2 Umlenkungen pro Frame)
        // -----------------------------------------------------------------
        Vector3 position = transform.position;
        Vector3 displacement = velocity * deltaTime;

        if (obstacleMask.value != 0)
        {
            for (int iteration = 0;
                 iteration < 2 && displacement.sqrMagnitude > 0.000001f;
                 iteration++)
            {
                float travel = displacement.magnitude;
                Vector3 direction = displacement / travel;

                if (!Physics.SphereCast(
                        position, bodyRadius, direction,
                        out RaycastHit hit, travel + collisionMargin,
                        obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    break;
                }

                float safeTravel =
                    Mathf.Max(0f, hit.distance - collisionMargin);

                position += direction * safeTravel;

                // Restbewegung auf die Oberfläche projizieren -> Gleiten.
                Vector3 remaining = direction * (travel - safeTravel);
                displacement = Vector3.ProjectOnPlane(remaining, hit.normal);
            }
        }

        position += displacement;

        // -----------------------------------------------------------------
        // Im Becken bleiben (optional)
        // -----------------------------------------------------------------
        if (aquarium != null)
            aquarium.ClampInside(ref position, bodyRadius, out _);

        transform.position = position;
    }


    // ---------------------------------------------------------------------
    // Input-Abstraktion (Legacy-Input oder neues Input System)
    // ---------------------------------------------------------------------

    private Vector3 ReadMoveInput()
    {
        float x = 0f, y = 0f, z = 0f;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.W)) z += 1f;
        if (Input.GetKey(KeyCode.S)) z -= 1f;
        if (Input.GetKey(KeyCode.D)) x += 1f;
        if (Input.GetKey(KeyCode.A)) x -= 1f;
        if (Input.GetKey(KeyCode.E)) y += 1f;
        if (Input.GetKey(KeyCode.Q)) y -= 1f;
#elif ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) z += 1f;
            if (keyboard.sKey.isPressed) z -= 1f;
            if (keyboard.dKey.isPressed) x += 1f;
            if (keyboard.aKey.isPressed) x -= 1f;
            if (keyboard.eKey.isPressed) y += 1f;
            if (keyboard.qKey.isPressed) y -= 1f;
        }
#endif

        return new Vector3(x, y, z);
    }

    private bool SprintHeld()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift) ||
               Input.GetKey(KeyCode.RightShift);
#elif ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;

        return keyboard != null &&
               (keyboard.leftShiftKey.isPressed ||
                keyboard.rightShiftKey.isPressed);
#else
        return false;
#endif
    }
}
