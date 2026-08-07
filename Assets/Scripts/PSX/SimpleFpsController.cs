using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Klassischer FPS-Controller: WASD + Mouselook, Shift = Sprint, Space = Sprung.
/// Funktioniert mit dem neuen Input System UND dem alten Input Manager
/// (je nachdem, was im Projekt aktiv ist - keine Einstellung nötig).
///
/// Setup:
/// 1. Leeres GameObject "Player" erstellen, dieses Script drauf.
///    Ein CharacterController wird automatisch hinzugefügt.
/// 2. Die Kamera als CHILD unter den Player hängen (Position ca. y = 1.6)
///    und im Feld "Camera Pivot" zuweisen. Leer = wird automatisch gesucht.
/// 3. Play. Escape gibt den Cursor frei, Klick ins Game-Fenster sperrt ihn wieder.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SimpleFpsController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Das Kamera-Child, das hoch/runter schaut (Pitch). Leer = erste Kamera unter diesem Objekt.")]
    [SerializeField] private Transform cameraPivot;

    [Header("Movement")]
    [Tooltip("Gehgeschwindigkeit in Unity-Einheiten pro Sekunde.")]
    [SerializeField] private float walkSpeed = 4f;

    [Tooltip("Geschwindigkeit mit gehaltener Shift-Taste.")]
    [SerializeField] private float sprintSpeed = 7f;

    [Tooltip("Wie schnell die Bewegung auf Zielgeschwindigkeit beschleunigt. Hoch = direkt, niedrig = schwammig.")]
    [SerializeField] private float acceleration = 12f;

    [Header("Jump / Gravity")]
    [SerializeField] private bool allowJump = true;

    [Tooltip("Sprunghöhe in Unity-Einheiten.")]
    [SerializeField] private float jumpHeight = 1.1f;

    [Tooltip("Schwerkraft (negativ). -20 fühlt sich knackiger an als realistische -9.81.")]
    [SerializeField] private float gravity = -20f;

    [Header("Mouse Look")]
    [Tooltip("Maus-Empfindlichkeit (Grad pro Maus-Einheit).")]
    [SerializeField] private float mouseSensitivity = 0.12f;

    [Tooltip("Wie weit man nach oben/unten schauen kann (Grad).")]
    [SerializeField] private float pitchLimit = 85f;

    [Tooltip("Vertikale Mausachse invertieren.")]
    [SerializeField] private bool invertY = false;

    [Header("Cursor")]
    [Tooltip("Cursor beim Start sperren und ausblenden.")]
    [SerializeField] private bool lockCursorOnStart = true;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraPivot == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
                cameraPivot = cam.transform;
        }

        if (cameraPivot == null)
            Debug.LogWarning("[SimpleFpsController] Keine Kamera als Child gefunden - Mouselook (Pitch) bleibt wirkungslos.", this);

        yaw = transform.eulerAngles.y;
        if (cameraPivot != null)
            pitch = cameraPivot.localEulerAngles.x;
    }

    private void Start()
    {
        if (lockCursorOnStart)
            SetCursorLocked(true);
    }

    private void Update()
    {
        HandleCursor();

        // Nur steuern, solange der Cursor gesperrt ist (sonst klickt man in den Editor)
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            HandleLook();
            HandleMovement();
        }
        else
        {
            // Schwerkraft trotzdem anwenden, damit man nicht in der Luft hängt
            ApplyGravityOnly();
        }
    }

    // ----------------------------------------------------------------------
    // Input-Abstraktion: liest neues Input System, wenn aktiv, sonst Legacy.
    // ----------------------------------------------------------------------

    private Vector2 GetMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
            return Vector2.zero;

        Vector2 move = Vector2.zero;
        if (kb.wKey.isPressed) move.y += 1f;
        if (kb.sKey.isPressed) move.y -= 1f;
        if (kb.dKey.isPressed) move.x += 1f;
        if (kb.aKey.isPressed) move.x -= 1f;
        return move;
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    private Vector2 GetLookDelta()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
#else
        // Legacy-Achsen sind kleiner skaliert als Input-System-Deltas
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
    }

    private bool GetSprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        return kb != null && kb.leftShiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    private bool GetJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        return kb != null && kb.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private bool GetEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private bool GetClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    // ----------------------------------------------------------------------
    // Look / Move / Cursor
    // ----------------------------------------------------------------------

    private void HandleLook()
    {
        Vector2 lookDelta = GetLookDelta() * mouseSensitivity;

        yaw += lookDelta.x;
        pitch += invertY ? lookDelta.y : -lookDelta.y;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        Vector2 moveInput = GetMoveInput();
        Vector3 wishDirection = transform.right * moveInput.x + transform.forward * moveInput.y;

        if (wishDirection.sqrMagnitude > 1f)
            wishDirection.Normalize();

        float targetSpeed = GetSprintHeld() ? sprintSpeed : walkSpeed;
        Vector3 targetVelocity = wishDirection * targetSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            targetVelocity,
            acceleration * Time.deltaTime * Mathf.Max(walkSpeed, sprintSpeed));

        if (controller.isGrounded)
        {
            // Leicht negativ halten, damit isGrounded stabil bleibt
            verticalVelocity = -2f;

            if (allowJump && GetJumpPressed())
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }

    private void ApplyGravityOnly()
    {
        if (controller.isGrounded)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
            controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
        }
    }

    private void HandleCursor()
    {
        if (GetEscapePressed())
            SetCursorLocked(false);
        else if (Cursor.lockState != CursorLockMode.Locked && GetClickPressed())
            SetCursorLocked(true);
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
