// Version: BoidTestPanel_v002
//
// v002: Kompatibel mit BEIDEN Input-Backends. Läuft das neue Input System
// exklusiv (ENABLE_INPUT_SYSTEM ohne Legacy), werden Keyboard.current /
// Mouse.current genutzt; sonst das klassische UnityEngine.Input.
// Keine Player-Settings-Änderung nötig.
//
// In-Game-Testpanel für die BoidSchool (v101+).
// Nutzt OnGUI - kein Canvas/EventSystem-Setup nötig: Script auf ein
// beliebiges GameObject in der Szene legen (z.B. das BoidSchool-Objekt),
// Referenz wird notfalls automatisch gefunden.

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class BoidTestPanel : MonoBehaviour
{
    [Header("References")]

    [Tooltip("Leer lassen = wird automatisch in der Szene gesucht.")]
    [SerializeField] private BoidSchool school;

    [Tooltip("Leer lassen = Camera.main.")]
    [SerializeField] private Camera viewCamera;

    [Header("Startle Settings")]

    [Tooltip("Radius der Schreckwelle bei Klick / Mitte-Button.")]
    [SerializeField, Min(0.5f)] private float startleRadius = 8f;

    [Tooltip("Klick-Tiefe, falls der Raycast nichts trifft (Abstand von der Kamera).")]
    [SerializeField, Min(1f)] private float fallbackClickDepth = 15f;

    [Header("Panel")]

    [SerializeField] private bool visible = true;

    [Tooltip("Taste zum Ein-/Ausblenden des Panels.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;

    private bool clickToStartle = true;
    private bool clickSetsLanding;
    private bool slowMotion;
    private Rect panelRect = new Rect(10f, 10f, 240f, 10f);

    private void Awake()
    {
        if (school == null)
            school = FindObjectOfType<BoidSchool>();

        if (school == null)
            Debug.LogWarning("[BoidTestPanel] Keine BoidSchool in der Szene gefunden.", this);
    }

    private void Update()
    {
        if (TogglePressed())
            visible = !visible;

        if (school == null || (!clickToStartle && !clickSetsLanding))
            return;

        if (!LeftClickPressed())
            return;

        Vector3 mousePosition = MousePosition();

        // Klicks auf das Panel nicht als Schreck werten.
        Vector2 guiMouse = new Vector2(
            mousePosition.x,
            Screen.height - mousePosition.y);

        if (visible && panelRect.Contains(guiMouse))
            return;

        Camera cam = viewCamera != null ? viewCamera : Camera.main;
        if (cam == null)
            return;

        Ray ray = cam.ScreenPointToRay(mousePosition);

        Vector3 point = Physics.Raycast(ray, out RaycastHit hit, 500f)
            ? hit.point
            : ray.GetPoint(fallbackClickDepth);

        // Landepunkt-Modus hat Vorrang vor dem Schreck-Modus.
        if (clickSetsLanding)
            school.LandAt(point);
        else
            school.StartleAt(point, startleRadius);
    }

    // ---------------------------------------------------------------------
    // Input-Abstraktion: Legacy-Input, wenn verfügbar, sonst neues
    // Input System (Keyboard.current / Mouse.current).
    // ---------------------------------------------------------------------

    private bool TogglePressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(toggleKey);
#elif ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null)
            return false;

        // KeyCode -> Key: Namen stimmen für F-Tasten und Buchstaben überein.
        // Falls nicht abbildbar, Fallback auf F1.
        if (!System.Enum.TryParse(toggleKey.ToString(), out Key key))
            key = Key.F1;

        return Keyboard.current[key].wasPressedThisFrame;
#else
        return false;
#endif
    }

    private bool LeftClickPressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#elif ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.leftButton.wasPressedThisFrame;
#else
        return false;
#endif
    }

    private Vector3 MousePosition()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
        return Mouse.current != null
            ? (Vector3)Mouse.current.position.ReadValue()
            : Vector3.zero;
#else
        return Vector3.zero;
#endif
    }

    private void OnGUI()
    {
        if (!visible)
            return;

        GUILayout.BeginArea(new Rect(10f, 10f, 240f, Screen.height - 20f));
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.Label("<b>Boid Test</b> (" + toggleKey + " = an/aus)",
            RichLabel());

        if (school == null)
        {
            GUILayout.Label("Keine BoidSchool gefunden!");
            EndPanel();
            return;
        }

        // --- Panik-Anzeige -------------------------------------------------
        float panic = school.AveragePanic;
        GUILayout.Label($"Panik: {panic:P0}");

        Rect barRect = GUILayoutUtility.GetRect(200f, 8f);
        GUI.Box(barRect, GUIContent.none);
        Rect fillRect = new Rect(
            barRect.x, barRect.y,
            barRect.width * panic, barRect.height);
        GUI.color = Color.Lerp(Color.green, Color.red, panic);
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.Space(6f);

        // --- Schreck -------------------------------------------------------
        GUILayout.Label("<b>Aufschrecken</b>", RichLabel());

        clickToStartle = GUILayout.Toggle(
            clickToStartle, " Klick in Szene = Schreck");

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Radius: {startleRadius:0.0}", GUILayout.Width(80f));
        startleRadius = GUILayout.HorizontalSlider(startleRadius, 1f, 30f);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Schreck in der Schwarm-Mitte"))
            school.StartleAt(school.SchoolCenter, startleRadius);

        if (GUILayout.Button("Alle aufschrecken"))
            school.StartleAll();

        GUILayout.Space(6f);

        // --- Sammeln / Beruhigen ------------------------------------------
        GUILayout.Label("<b>Zusammenkommen</b>", RichLabel());

        if (GUILayout.Button("Sammeln (Rally, 4 s)"))
            school.Rally(4f);

        if (GUILayout.Button("Beruhigen (Panik beenden)"))
            school.CalmDown();

        if (GUILayout.Button("Neues Roam-Ziel"))
            school.ForceNewRoamTarget();

        GUILayout.Space(6f);

        // --- Wanderung -----------------------------------------------------
        GUILayout.Label("<b>Wanderung (Leader)</b>", RichLabel());

        string migrationLabel = school.MigrationActive
            ? "Wanderung stoppen"
            : "Wanderung starten";

        if (GUILayout.Button(migrationLabel))
            school.ToggleMigration();

        if (school.MigrationActive)
            GUILayout.Label("Leader führt die Gruppe von Ziel zu Ziel.");

        if (school.HasPlayer)
        {
            string followLabel = school.FollowPlayerActive
                ? "Folgen stoppen"
                : "Folgt mir (Player als Leader)";

            if (GUILayout.Button(followLabel))
                school.ToggleFollowPlayer();

            if (school.FollowPlayerActive)
                GUILayout.Label("Schwarm folgt dem Player (Sprint-Schreck aus).");
        }
        else
        {
            GUILayout.Label("('Folgt mir' braucht ein Player\nTransform in der BoidSchool.)");
        }

        GUILayout.Space(6f);

        // --- Erkunden ------------------------------------------------------
        GUILayout.Label("<b>Erkunden</b>", RichLabel());

        bool circuit =
            school.CurrentRoamPattern == BoidSchool.RoamPattern.Circuit;

        string patternLabel = circuit
            ? "Zu Zufallszielen wechseln"
            : "Große Runde fliegen (Circuit)";

        if (GUILayout.Button(patternLabel))
            school.ToggleRoamPattern();

        if (circuit)
            GUILayout.Label("Rundkurs aktiv (Ellipse durchs Areal).");

        GUILayout.Space(6f);

        // --- Landen --------------------------------------------------------
        GUILayout.Label("<b>Landen</b>", RichLabel());

        clickSetsLanding = GUILayout.Toggle(
            clickSetsLanding, " Klick setzt Landepunkt");

        if (GUILayout.Button("Landen unter Schwarmmitte"))
            school.LandBelowSchoolCenter();

        if (school.LandingActive)
        {
            GUILayout.Label("Landung aktiv - Schwarm sitzt / im Anflug.");

            if (GUILayout.Button("Abheben"))
                school.TakeOff();
        }

        GUILayout.Space(6f);

        // --- Zeit ----------------------------------------------------------
        GUILayout.Label("<b>Zeit</b>", RichLabel());

        bool newSlow = GUILayout.Toggle(slowMotion, " Zeitlupe (0.25x)");
        if (newSlow != slowMotion)
        {
            slowMotion = newSlow;
            Time.timeScale = slowMotion ? 0.25f : 1f;
        }

        EndPanel();
    }

    private void EndPanel()
    {
        GUILayout.EndVertical();

        // Panel-Rect für die Klick-Abschirmung merken.
        if (Event.current.type == EventType.Repaint)
        {
            Rect last = GUILayoutUtility.GetLastRect();
            panelRect = new Rect(10f, 10f, last.width, last.height);
        }

        GUILayout.EndArea();
    }

    private static GUIStyle RichLabel()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.richText = true;
        return style;
    }

    private void OnDisable()
    {
        if (slowMotion)
        {
            Time.timeScale = 1f;
            slowMotion = false;
        }
    }
}
