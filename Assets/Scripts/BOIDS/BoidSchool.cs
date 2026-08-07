// Version: BoidSchool_v106_InstancedRendering
//
// v106: Draw-Call-Optimierung.
//   - Render Mode "Instanced": KEINE GameObjects mehr pro Boid. Die Boids
//     werden per Graphics.RenderMeshInstanced gezeichnet - 1 Draw Call pro
//     1023 Boids statt 1-2 pro Boid, und der Main Thread spart sich
//     tausende Transform-Updates. Mesh + Material im Header "Rendering"
//     zuweisen (Material braucht 'Enable GPU Instancing' - wird notfalls
//     automatisch aktiviert). Ohne Mesh wird die Unity-Sphere genutzt;
//     ein Low-Poly-Mesh (Icosphere ~80 Tris) wird DRINGEND empfohlen.
//   - Im Instanced-Modus gibt es keine BoidAgent-Instanzen (SetActivity
//     entfällt); alles andere (Panik, Landen, Folgen, ...) ist identisch.
//   - GameObject-Modus bleibt verfügbar; dort werden Schatten der Boids
//     jetzt automatisch deaktiviert (halbiert die Draw Calls).
//
// Version: BoidSchool_v105_FollowPlayer
//
// v105: "Folgt mir" - der Player wird Leader.
//   - SetFollowPlayer(true): alle Boids nutzen ihre Follow-Plätze relativ
//     zum Player-Transform. Die Formation richtet sich nach der
//     BEWEGUNGSRICHTUNG des Players aus; steht er still, hovern sie fast
//     bewegungslos auf ihren Plätzen (Tempo-Matching statt Dauerkreisen).
//   - Im Folge-Modus ist der Sprint-Schreck deaktiviert (sonst würde man
//     die eigene Eskorte versprengen). Klick-Schreck etc. wirken weiter.
//   - Schließt sich gegenseitig mit der Boid-Leader-Wanderung aus.
//
// Version: BoidSchool_v104_PlayerSensing
//
// v104: Player-Sensing (Sandfisch-Regel).
//   - Boids weichen einem zugewiesenen Player-Transform sanft aus - man
//     kann langsam mitten durch den Schwarm fliegen und er teilt sich.
//   - Bewegungs-Gating wie bei den Sandfischen: erst wenn der Player
//     SCHNELLER als playerStartleSpeed ist UND ein Boid in Reichweite,
//     wird eine Panikwelle am Player ausgelöst (mit Cooldown).
//     Stillstehen/Schleichen = unsichtbar.
//   - Passende Scripts: BoidPlayerController (WASD + Q/E, Shift = Sprint
//     über der Schwelle) und BoidFollowCamera.
//
// Version: BoidSchool_v103_CircuitAndLanding
//
// v103: Rundflug + Landen.
//   - RoamPattern.Circuit: Waypoints auf einer großen Ellipse durchs Areal
//     (an die Becken-Grundfläche angepasst, leichte Höhenvariation), der
//     Reihe nach abgeflogen. Funktioniert auch im Leader-Modus.
//   - LandAt(punkt): persönliche Landeplätze in einer Scheibe um den Punkt,
//     Anflug mit kontinuierlichem Abbremsen (Arrive), dann Idle bei ~0.
//     Containment wird für Gelandete unterdrückt (sonst drückt die Randzone
//     sie vom Boden weg). Schreck sprengt die Landung; danach kehren alle
//     von selbst zu ihren Plätzen zurück. TakeOff() beendet das Idlen.
//
// Version: BoidSchool_v102_LeaderMigration
//
// v102: Wander-/Migrationsmodus mit Leader.
//   - SetMigration(true): ein Boid (nahe Schwerpunkt) wird Leader, wird
//     hochskaliert und zieht von Roam-Ziel zu Roam-Ziel. Alle anderen haben
//     feste persönliche Plätze in einer Tropfenform hinter ihm (seitlich /
//     vertikal / längs gestreut) und folgen, während das normale Flocking
//     weiterläuft. Zurückgefallene beschleunigen, Aufgeschlossene drosseln
//     auf Leader-Tempo. Panik unterbricht die Formation; danach sammeln
//     sie sich emergent wieder hinter dem Leader.
//
// Version: BoidSchool_v101_PanicWave
//
// v101: Panik-System + öffentliche Test-API.
//   - StartleAt(punkt, radius): Panikwelle - nahe Boids reagieren sofort,
//     ferne mit Distanz-Verzögerung (Wave). Während der Panik: Flucht vom
//     Schreckpunkt, Cohesion/Roaming aus, Überspeed, wendiger, nervöser.
//   - Das Wiederzusammenfinden ist EMERGENT: Panik klingt pro Boid
//     individuell ab, dann sammeln Cohesion + Roaming die Gruppe von selbst.
//   - Rally(): aktives Sammeln auf Kommando. CalmDown(): Panik abbrechen.
//   - Passendes UI: BoidTestPanel.cs (OnGUI-Buttons + Klick-Schreck).
//
// Version: BoidSchool_v100_Rebuild
//
// Kompletter Neuaufbau. Kernidee: kein Kraft-Feder-Modell mehr
// (viele "desiredVelocity - velocity"-Springs, die sich gegenseitig
// aufschaukeln und drei Glättungsschichten brauchen), sondern ein
// KINEMATISCHES HEADING-MODELL:
//
//   Jeder Boid besitzt nur Heading (Einheitsrichtung) + Speed.
//   Alle Verhalten geben gewichtete RICHTUNGS-VOTES ab:
//     Separation, Alignment, Cohesion, Roam, Wander, Containment, Avoidance.
//   Die Votes werden summiert und normalisiert -> Zielkurs.
//   Das Heading dreht mit begrenzter Drehrate (Grad/s) dorthin.
//
//   Damit sind alle Bahnen krümmungsbegrenzt und "flugartig".
//   Bounces sind strukturell unmöglich: es gibt keine Impulse,
//   nur Kursänderungen.
//
// Weitere Bausteine:
//   - Ausweichen: Richtungs-Fächer von "fast geradeaus" nach "seitlich",
//     ERSTE freie Richtung gewinnt -> der Boid streift an der Silhouette
//     entlang (Kurve ums Objekt). Hysterese verhindert Links/Rechts-Flackern.
//     Es gibt keine Rückwärts-Kandidaten (die erzeugten den Billard-Look).
//   - Containment: weiches Feld aus AquariumVolume.GetContainment().
//     Dringlichkeit erhöht die Drehrate -> früher, weiter Bogen.
//   - Kollisions-Safety: Slide entlang der Oberfläche, nie Reflektion.
//   - Individualität: pro Boid Speed-/Turn-Skalierung + Noise-Seed
//     (Burst & Glide, Schlängel-Wander).
//   - Banking: Rollwinkel aus der Gierrate.
//
// Benötigt: AquariumVolume_v004_Clean, BoidAgent (unverändert, v001).
// WICHTIG beim Ersetzen: Dateiinhalt ersetzen, Datei/.meta NICHT löschen.

using UnityEngine;

[DisallowMultipleComponent]
public class BoidSchool : MonoBehaviour
{
    // =====================================================================
    // Inspector
    // =====================================================================

    [Header("References")]

    [SerializeField] private AquariumVolume aquarium;
    [SerializeField] private GameObject[] boidPrefabs;
    [SerializeField] private Transform boidRoot;


    [Header("Spawn")]

    [SerializeField, Min(1)] private int spawnCount = 150;
    [SerializeField] private int randomSeed = 12345;

    [Tooltip("Wandabstand der Spawnpunkte (World Units).")]
    [SerializeField, Min(0f)] private float spawnPadding = 2f;

    [Tooltip("Körperradius eines Boids. Basis für Kollisions-Safety.")]
    [SerializeField, Min(0.01f)] private float agentRadius = 0.18f;


    [Header("Movement")]

    [SerializeField, Min(0.01f)] private float minSpeed = 1.2f;
    [SerializeField, Min(0.01f)] private float maxSpeed = 3.5f;

    [Tooltip("Wie schnell die Geschwindigkeit ihrem Zielwert folgt (Units/s²).")]
    [SerializeField, Min(0.1f)] private float speedAcceleration = 4.5f;

    [Tooltip("Basis-Drehrate in Grad pro Sekunde. Bestimmt den minimalen Kurvenradius: Radius ≈ Speed / TurnRate(rad).")]
    [SerializeField, Range(45f, 720f)] private float turnRateDegrees = 200f;

    [Tooltip("Faktor, um den Containment/Avoidance die Drehrate bei voller Dringlichkeit anheben dürfen.")]
    [SerializeField, Range(1f, 4f)] private float urgencyTurnBoost = 2.1f;

    [Tooltip("In scharfen Kurven wird das Zieltempo leicht gesenkt (1 = aus).")]
    [SerializeField, Range(0.5f, 1f)] private float sharpTurnSpeedFactor = 0.8f;

    [Tooltip("Wie weich das Mesh der Bewegungsrichtung folgt.")]
    [SerializeField, Range(1f, 30f)] private float orientationResponsiveness = 10f;


    [Header("Individuality")]

    [Tooltip("Streuung der Wunschgeschwindigkeit pro Boid (±). Wichtigster Regler gegen Gleichschritt.")]
    [SerializeField, Range(0f, 0.5f)] private float speedVariation = 0.15f;

    [Tooltip("Streuung der Drehfreudigkeit pro Boid (±).")]
    [SerializeField, Range(0f, 0.5f)] private float turnVariation = 0.2f;

    [Tooltip("Tempo-'Atmen' um die persönliche Reisegeschwindigkeit (Burst & Glide). 0 = konstantes Tempo.")]
    [SerializeField, Range(0f, 1f)] private float burstGlideAmount = 0.35f;

    [SerializeField, Range(0.02f, 2f)] private float burstGlideFrequency = 0.25f;


    [Header("Flocking")]

    [Tooltip("Nachbar-Suchradius. Bestimmt auch die Grid-Zellgröße.")]
    [SerializeField, Min(0.2f)] private float perceptionRadius = 2.5f;

    [Tooltip("Innerhalb dieses Radius wird aktiv Abstand gehalten.")]
    [SerializeField, Min(0.05f)] private float separationRadius = 0.7f;

    [SerializeField, Min(0f)] private float separationStrength = 2.2f;
    [SerializeField, Min(0f)] private float alignmentStrength = 0.9f;
    [SerializeField, Min(0f)] private float cohesionStrength = 0.7f;


    [Header("Roaming")]

    [Tooltip("Die Schule wandert zu wechselnden Zielpunkten IM Becken. Das erzeugt das gemeinsame Durchqueren des Raums.")]
    [SerializeField] private bool roamEnabled = true;

    [Tooltip("Wandabstand der Roam-Ziele. Deutlich größer als die Containment-Randzone wählen.")]
    [SerializeField, Min(0f)] private float roamPadding = 4.5f;

    [Tooltip("Ziel gilt als erreicht, wenn der Schul-Schwerpunkt so nah ist -> neues Ziel.")]
    [SerializeField, Min(0.5f)] private float roamArrivalRadius = 2f;

    [SerializeField, Range(1f, 30f)] private float roamMinInterval = 6f;
    [SerializeField, Range(1f, 30f)] private float roamMaxInterval = 12f;

    [Tooltip("Gewicht des Roam-Votes. Haupt-Regler für 'wie zielstrebig zieht die Gruppe'.")]
    [SerializeField, Min(0f)] private float roamStrength = 0.8f;

    [Tooltip("Jeder Boid peilt einen fest versetzten Punkt um das Ziel an (World Units) -> Wolke statt Trichter.")]
    [SerializeField, Min(0f)] private float roamTargetJitter = 1.3f;


    [Header("Wander")]

    [Tooltip("Kontinuierliche Schlängelbewegung per Perlin-Noise (seitlich + vertikal).")]
    [SerializeField, Min(0f)] private float wanderStrength = 0.45f;

    [SerializeField, Range(0.02f, 2f)] private float wanderFrequency = 0.35f;


    [Header("Panic / Startle")]

    [Tooltip("Gewicht des Flucht-Votes bei voller Panik. Muss Flocking klar dominieren.")]
    [SerializeField, Min(0f)] private float panicFleeStrength = 2.6f;

    [Tooltip("Wie lange ein Boid in Panik bleibt (Sekunden, ±30 % pro Boid).")]
    [SerializeField, Range(0.5f, 10f)] private float panicDuration = 2.5f;

    [Tooltip("Wie lange die Panik danach abklingt (Sekunden). In dieser Phase kehren Cohesion/Roaming weich zurück -> die Gruppe sammelt sich von selbst.")]
    [SerializeField, Range(0.5f, 10f)] private float panicCalmTime = 2.5f;

    [Tooltip("Ausbreitungsgeschwindigkeit der Schreckwelle (Units/s): ferne Boids reagieren später.")]
    [SerializeField, Range(1f, 40f)] private float panicWaveSpeed = 9f;

    [Tooltip("Tempo-Faktor über der normalen Maximalgeschwindigkeit während der Panik.")]
    [SerializeField, Range(1f, 2f)] private float panicSpeedMultiplier = 1.3f;

    [Tooltip("Drehraten-Faktor während der Panik (hektischere Kurven).")]
    [SerializeField, Range(1f, 3f)] private float panicTurnBoost = 1.5f;

    [Tooltip("Wander-Verstärkung während der Panik (nervöses Zickzack).")]
    [SerializeField, Range(0f, 4f)] private float panicWanderBoost = 1.5f;

    [Tooltip("Gewicht des Sammel-Votes während eines Rally-Rufs.")]
    [SerializeField, Min(0f)] private float rallyStrength = 2f;


    [Header("Migration / Leader")]

    [Tooltip("Größenfaktor des Leaders, solange die Wanderung aktiv ist.")]
    [SerializeField, Range(1f, 3f)] private float leaderScaleMultiplier = 1.6f;

    [Tooltip("Leader-Reisetempo relativ zum Maximum. <1, damit der Schwanz der Gruppe aufschließen kann.")]
    [SerializeField, Range(0.5f, 1f)] private float leaderSpeedFactor = 0.88f;

    [Tooltip("Gewicht, mit dem der Leader zum Roam-Ziel zieht (entschlossener als normales Roaming).")]
    [SerializeField, Min(0f)] private float leaderRoamStrength = 1.6f;

    [Tooltip("Basis-Abstand der Follow-Plätze hinter dem Leader (wird pro Boid mit Faktor 0.6-2.8 gestreut).")]
    [SerializeField, Min(0.2f)] private float followDistance = 1.2f;

    [Tooltip("Seitliche Streuung der Follow-Plätze (vertikal automatisch flacher).")]
    [SerializeField, Min(0f)] private float followSpread = 2.2f;

    [Tooltip("Gewicht des Votes zum persönlichen Follow-Platz.")]
    [SerializeField, Min(0f)] private float followStrength = 1.5f;

    [Tooltip("Ab dieser Distanz zum eigenen Platz wird zum Aufholen voll beschleunigt.")]
    [SerializeField, Min(0.5f)] private float catchUpDistance = 4f;


    [Header("Exploration Circuit")]

    [Tooltip("Zielwahl: Zufallspunkte im Becken oder eine große Runde (Ellipse) durchs Areal.")]
    [SerializeField] private RoamPattern roamPattern = RoamPattern.RandomTargets;

    [Tooltip("Anzahl der Waypoints auf der Runde.")]
    [SerializeField, Range(4, 24)] private int circuitWaypointCount = 10;

    [Tooltip("Höhenvariation der Waypoints als Anteil der halben Beckenhöhe (0 = alle auf Mittelhöhe).")]
    [SerializeField, Range(0f, 0.6f)] private float circuitHeightVariation = 0.25f;


    [Header("Landing")]

    [Tooltip("Gewicht des Anflug-Votes zum persönlichen Landeplatz.")]
    [SerializeField, Min(0f)] private float landStrength = 2.2f;

    [Tooltip("Radius der Scheibe, in der die Landeplätze um den Landepunkt verteilt werden.")]
    [SerializeField, Min(0.3f)] private float landSpreadRadius = 2.5f;

    [Tooltip("Ab dieser Distanz zum Platz beginnt das kontinuierliche Abbremsen (Arrive).")]
    [SerializeField, Min(0.5f)] private float landSlowdownRadius = 3f;

    [Tooltip("Rest-Tempo im Idle (fast 0; minimales Wackeln bleibt lebendig).")]
    [SerializeField, Range(0f, 0.5f)] private float landIdleSpeed = 0.06f;


    [Header("Player Sensing")]

    [Tooltip("Optional: Player (z.B. die Kugel mit BoidPlayerController). Leer = kein Player-Sensing.")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("In diesem Radius weichen Boids dem Player sanft aus (auch ohne Panik).")]
    [SerializeField, Min(0f)] private float playerAvoidRadius = 2.2f;

    [SerializeField, Min(0f)] private float playerAvoidStrength = 1.8f;

    [Tooltip("Sandfisch-Regel: erst ab dieser Player-Geschwindigkeit wird geschreckt. Gehtempo darunter halten, Sprint darüber.")]
    [SerializeField, Min(0.1f)] private float playerStartleSpeed = 4.5f;

    [Tooltip("Ist der schnelle Player näher als das an irgendeinem Boid, löst die Panikwelle aus.")]
    [SerializeField, Min(0.5f)] private float playerStartleRadius = 6f;

    [Tooltip("Mindestabstand zwischen zwei player-ausgelösten Schrecks.")]
    [SerializeField, Range(0.2f, 10f)] private float playerStartleCooldown = 1.5f;


    [Header("Containment")]

    [Tooltip("Breite der weichen Randzone (World Units). Großzügig wählen (~20-30 % der Beckengröße): der Bogen beginnt am ZONENANFANG.")]
    [SerializeField, Min(0.2f)] private float containmentMargin = 4f;

    [Tooltip("Gewicht des Containment-Votes bei voller Stärke. Muss nah an der Wand die Flocking-Votes dominieren.")]
    [SerializeField, Min(0f)] private float containmentStrength = 3f;

    [Tooltip("Verlauf über die Zone: 1 = früh spürbar (weite Bögen), 2 = erst spät stark.")]
    [SerializeField, Range(0.5f, 3f)] private float containmentFalloffPower = 1.15f;

    [Tooltip("Sekunden Flugweg Vorausschau: die Zone wird zusätzlich an der vorhergesagten Position geprüft.")]
    [SerializeField, Range(0f, 3f)] private float containmentPredictionTime = 1f;


    [Header("Obstacle Avoidance")]

    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("Basisreichweite der Vorausschau (World Units).")]
    [SerializeField, Min(0.1f)] private float avoidLookAheadBase = 1.4f;

    [Tooltip("Zusätzliche Vorausschau pro Unit Geschwindigkeit (Sekunden).")]
    [SerializeField, Range(0f, 2f)] private float avoidLookAheadPerSpeed = 0.7f;

    [SerializeField, Min(0.02f)] private float avoidProbeRadius = 0.28f;

    [Tooltip("Gewicht des Ausweich-Votes bei voller Dringlichkeit.")]
    [SerializeField, Min(0f)] private float avoidStrength = 3f;

    [Tooltip("Anzahl Kandidatenrichtungen im Fächer (von fast geradeaus bis seitlich).")]
    [SerializeField, Range(8, 48)] private int avoidDirectionCount = 26;

    [Tooltip("Sensor-Updates pro Sekunde und Boid (SphereCasts werden über die Frames verteilt).")]
    [SerializeField, Range(2f, 60f)] private float avoidProbeFrequency = 18f;

    [Tooltip("Wie schnell die Ausweich-Dringlichkeit abklingt, wenn der Weg frei ist (1/s).")]
    [SerializeField, Range(0.5f, 10f)] private float avoidUrgencyDecay = 3f;

    [Tooltip("Bewegung wird jeden Frame gegen Collider geprüft: bei Kontakt Slide statt Durchtunneln/Bounce.")]
    [SerializeField] private bool continuousSafety = true;

    [SerializeField, Min(0f)] private float safetyMargin = 0.04f;


    [Header("Banking")]

    [Tooltip("Maximaler Rollwinkel in Kurven. 0 = aus.")]
    [SerializeField, Range(0f, 70f)] private float maxBankAngle = 40f;

    [Tooltip("Grad Roll pro Grad/s Gierrate.")]
    [SerializeField, Range(0f, 1f)] private float bankPerTurnRate = 0.2f;

    [SerializeField, Range(0.5f, 20f)] private float bankResponsiveness = 5f;


    [Header("Rendering")]

    [Tooltip("GameObjects = 1 Instanz pro Boid (flexibel, teuer).\nInstanced = Graphics.RenderMeshInstanced, 1 Draw Call pro 1023 Boids, keine Transform-Updates. Empfohlen ab ein paar hundert Boids.")]
    [SerializeField] private BoidRenderMode renderMode = BoidRenderMode.Instanced;

    [Tooltip("Mesh für den Instanced-Modus. Leer = Unity-Sphere (768 Tris - für viele Boids besser ein Low-Poly-Mesh mit ~80 Tris zuweisen!).")]
    [SerializeField] private Mesh instancedMesh;

    [Tooltip("Material für den Instanced-Modus. 'Enable GPU Instancing' wird notfalls automatisch aktiviert.")]
    [SerializeField] private Material instancedMaterial;

    [Tooltip("Grundgröße eines Boids im Instanced-Modus (entspricht der Prefab-Scale im GameObject-Modus).")]
    [SerializeField] private Vector3 instancedScale = new Vector3(0.35f, 0.35f, 0.35f);

    [Tooltip("Boid-Schatten deaktivieren. Halbiert im GameObject-Modus die Draw Calls; für kleine Schwarmkörper praktisch unsichtbar.")]
    [SerializeField] private bool disableBoidShadows = true;


    [Header("Debug")]

    [SerializeField] private bool drawHeadings = false;
    [SerializeField] private bool drawAvoidance = false;
    [SerializeField] private bool drawRoamTarget = false;
    [SerializeField, Min(0.05f)] private float debugLineLength = 0.7f;


    // =====================================================================
    // Runtime
    // =====================================================================

    private BoidAgent[] agents;
    private Transform[] agentTransforms;

    private Vector3[] positions;
    private Vector3[] headings;      // Einheitsvektoren
    private float[] speeds;

    // Individualität
    private float[] speedScales;
    private float[] turnScales;
    private float[] noiseSeeds;
    private Vector3[] roamOffsets;   // fester persönlicher Zielversatz
    private float[] bankAngles;

    // Panik / Rally
    private float[] panicLevels;       // 0..1, weich auf/ab
    private float[] panicStartTimes;   // Wellen-Verzögerung pro Boid
    private float[] panicEndTimes;
    private Vector3[] fleeDirections;  // beim Schreck eingefrorene Fluchtrichtung
    private Vector3 rallyPoint;
    private float rallyUntil = float.NegativeInfinity;

    // Migration / Leader
    private bool migrationActive;
    private int leaderIndex = -1;
    private Vector3 leaderBaseScale = Vector3.one;
    private Vector3[] followSlots;     // fester Platz pro Boid, leader-lokal (x=seitlich, y=vertikal, z=längs, negativ = hinter dem Leader)

    // Erkundungs-Muster
    public enum RoamPattern { RandomTargets, Circuit }
    private Vector3[] circuitPoints;
    private int circuitIndex;
    private int circuitDirection = 1;  // +1 / -1 = Umlaufrichtung

    // Landing
    private bool landingActive;
    private Vector3 landingPoint;
    private Vector3[] landingSlotsWorld; // fertig geclampte Welt-Plätze

    // Player-Sensing
    private bool playerPresent;
    private Vector3 playerWorldPosition;
    private Vector3 playerVelocity;
    private Vector3 lastPlayerPosition;
    private bool hasLastPlayerPosition;
    private float nextPlayerStartleTime;

    // "Folgt mir": Player als Leader
    private bool followPlayerActive;
    private Vector3 playerFollowHeading = Vector3.forward; // letzte gültige Bewegungsrichtung

    // Rendering
    public enum BoidRenderMode { GameObjects, Instanced }
    private const int InstanceBatchSize = 1023;

    private Quaternion[] visualRotations; // geglättete Darstellungsrotation (beide Modi)
    private float[] visualScales;         // pro Boid (Leader wird größer)
    private Matrix4x4[][] instanceBatches;
    private Mesh resolvedInstancedMesh;
    private RenderParams instancedRenderParams;

    // Ausweich-Sensor (gestaffelt aktualisiert)
    private Vector3[] avoidDirections;
    private float[] avoidUrgencies;
    private float[] nextProbeTimes;
    private float[] probeRolls;      // stabile Fächer-Rotation pro Boid

    // Fächer-Kandidaten (lokal, +Z = forward, sortiert nach Nähe zu forward)
    private Vector3[] candidateDirections;

    // Spatial Grid (verkettete Listen in Arrays)
    private int[] cellHeads;
    private int[] nextInCell;
    private int[] usedCells;
    private int usedCellCount;
    private Vector3 gridMin;
    private float cellSize;
    private int gridX, gridY, gridZ;

    // Schule / Roaming
    private Vector3 schoolCenter;
    private Vector3 schoolHeading = Vector3.forward;
    private Vector3 roamPoint;
    private float nextRoamChange;

    private System.Random random;
    private readonly Collider[] overlapBuffer = new Collider[8];


    // =====================================================================
    // Unity
    // =====================================================================

    private void Start()
    {
        Initialize();
    }

    private void Update()
    {
        if (positions == null || positions.Length == 0)
            return;

        float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
        float time = Time.time;

        BuildGrid();
        UpdateSchoolAndRoam(time);
        UpdatePlayerSensing(deltaTime, time);
        UpdateAvoidanceSensors(time);
        Step(deltaTime, time);
        RenderInstancedBoids();
    }


    // =====================================================================
    // Initialization
    // =====================================================================

    private void Initialize()
    {
        if (aquarium == null)
        {
            Debug.LogError("BoidSchool benötigt ein AquariumVolume.", this);
            enabled = false;
            return;
        }

        // Instanced-Setup zuerst: schlägt es fehl (kein Material), fällt
        // der Modus sauber auf GameObjects zurück - dann greift darunter
        // der Prefab-Check.
        if (renderMode == BoidRenderMode.Instanced &&
            !TrySetupInstancedRendering())
        {
            renderMode = BoidRenderMode.GameObjects;
        }

        if (renderMode == BoidRenderMode.GameObjects &&
            (boidPrefabs == null || boidPrefabs.Length == 0))
        {
            Debug.LogError("BoidSchool benötigt mindestens ein Boid-Prefab.", this);
            enabled = false;
            return;
        }

        if (boidRoot == null)
            boidRoot = transform;

        random = new System.Random(randomSeed);

        int count = spawnCount;

        agents = new BoidAgent[count];
        agentTransforms = new Transform[count];
        positions = new Vector3[count];
        headings = new Vector3[count];
        speeds = new float[count];

        speedScales = new float[count];
        turnScales = new float[count];
        noiseSeeds = new float[count];
        roamOffsets = new Vector3[count];
        bankAngles = new float[count];

        avoidDirections = new Vector3[count];
        avoidUrgencies = new float[count];
        nextProbeTimes = new float[count];
        probeRolls = new float[count];

        panicLevels = new float[count];
        panicStartTimes = new float[count];
        panicEndTimes = new float[count];
        fleeDirections = new Vector3[count];
        followSlots = new Vector3[count];
        landingSlotsWorld = new Vector3[count];

        visualRotations = new Quaternion[count];
        visualScales = new float[count];

        if (renderMode == BoidRenderMode.Instanced)
        {
            int batchCount =
                (count + InstanceBatchSize - 1) / InstanceBatchSize;

            instanceBatches = new Matrix4x4[batchCount][];

            for (int b = 0; b < batchCount; b++)
            {
                int size = Mathf.Min(
                    InstanceBatchSize,
                    count - b * InstanceBatchSize);

                instanceBatches[b] = new Matrix4x4[size];
            }
        }

        for (int i = 0; i < count; i++)
            panicStartTimes[i] = float.PositiveInfinity;

        nextInCell = new int[count];
        usedCells = new int[count];

        BuildCandidateFan();
        SetupGrid();

        roamPoint = aquarium.GetRandomPoint(random, roamPadding);
        nextRoamChange = Time.time + RandomRange(roamMinInterval, roamMaxInterval);

        if (roamPattern == RoamPattern.Circuit)
            BuildCircuit();

        Vector3 initialDirection = RandomUnitVector();
        float probeInterval = 1f / Mathf.Max(2f, avoidProbeFrequency);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = FindSpawnPosition();

            Vector3 heading = Vector3.Slerp(
                RandomUnitVector(), initialDirection, 0.6f).normalized;

            speeds[i] = Mathf.Lerp(minSpeed, maxSpeed, (float)random.NextDouble());
            headings[i] = heading;
            positions[i] = position;

            speedScales[i] = 1f + RandomSigned() * speedVariation;
            turnScales[i] = 1f + RandomSigned() * turnVariation;
            noiseSeeds[i] = (float)random.NextDouble() * 512f;

            // Fester persönlicher Zielversatz -> die Schule kommt als Wolke an.
            roamOffsets[i] = new Vector3(
                Mathf.Sin(noiseSeeds[i] * 12.9898f),
                Mathf.Sin(noiseSeeds[i] * 78.2330f) * 0.5f,
                Mathf.Sin(noiseSeeds[i] * 37.7190f)) * roamTargetJitter;

            probeRolls[i] = (float)random.NextDouble() * 360f;

            // Fester Follow-Platz: Tropfenform hinter dem Leader.
            // Längs gestaffelt (0.6-2.8x followDistance), seitlich breit,
            // vertikal flacher - so bildet sich ein Schwarmkörper statt
            // einer Perlenkette.
            float slotDepth = 0.6f + (float)random.NextDouble() * 2.2f;

            followSlots[i] = new Vector3(
                Mathf.Sin(noiseSeeds[i] * 4.7f) * followSpread,
                Mathf.Sin(noiseSeeds[i] * 9.1f) * followSpread * 0.35f,
                -followDistance * slotDepth);

            // Sensor-Updates über das Intervall verteilen.
            nextProbeTimes[i] = Time.time + probeInterval * ((float)i / count);

            visualRotations[i] = Quaternion.LookRotation(heading, Vector3.up);
            visualScales[i] = 1f;

            if (renderMode == BoidRenderMode.GameObjects)
            {
                GameObject prefab = PickPrefab();

                GameObject instance = Instantiate(
                    prefab,
                    position,
                    visualRotations[i],
                    boidRoot);

                BoidAgent agent = instance.GetComponent<BoidAgent>();
                if (agent == null)
                    agent = instance.AddComponent<BoidAgent>();

                agent.Index = i;
                agents[i] = agent;
                agentTransforms[i] = instance.transform;

                // Schatten der Boids kosten einen zweiten Draw-Pass pro
                // Renderer und sind bei kleinen Schwarmkörpern unsichtbar.
                if (disableBoidShadows)
                {
                    foreach (Renderer boidRenderer in
                             instance.GetComponentsInChildren<Renderer>())
                    {
                        boidRenderer.shadowCastingMode =
                            UnityEngine.Rendering.ShadowCastingMode.Off;
                        boidRenderer.receiveShadows = false;
                    }
                }
            }
        }
    }

    private GameObject PickPrefab()
    {
        for (int attempt = 0; attempt < boidPrefabs.Length; attempt++)
        {
            GameObject prefab = boidPrefabs[random.Next(0, boidPrefabs.Length)];
            if (prefab != null)
                return prefab;
        }

        foreach (GameObject prefab in boidPrefabs)
            if (prefab != null)
                return prefab;

        return null;
    }

    private Vector3 FindSpawnPosition()
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            Vector3 candidate = aquarium.GetRandomPoint(random, spawnPadding);

            if (obstacleMask.value == 0)
                return candidate;

            if (!Physics.CheckSphere(
                    candidate, agentRadius, obstacleMask,
                    QueryTriggerInteraction.Ignore))
                return candidate;
        }

        return aquarium.GetRandomPoint(random, spawnPadding);
    }

    // Fächer von "fast geradeaus" (z ≈ 0.97) bis "seitlich" (z ≈ 0.1),
    // sortiert: Index 0 = kleinste Abweichung vom Kurs. Kein Rückwärts -
    // Rückwärts-Kandidaten erzeugten den Billard-Look.
    private void BuildCandidateFan()
    {
        int count = Mathf.Max(8, avoidDirectionCount);
        candidateDirections = new Vector3[count];

        const float goldenAngle = 2.39996323f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float z = Mathf.Lerp(0.97f, 0.1f, t);
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            float angle = goldenAngle * i;

            candidateDirections[i] = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                z).normalized;
        }
    }


    // =====================================================================
    // Spatial Grid
    // =====================================================================

    private void SetupGrid()
    {
        Bounds bounds = aquarium.WorldBounds;

        cellSize = Mathf.Max(perceptionRadius, 0.25f);
        gridMin = bounds.min;

        gridX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize));
        gridY = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cellSize));
        gridZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize));

        long total = (long)gridX * gridY * gridZ;

        // Sicherheitsdeckel gegen absurde Kombinationen.
        if (total > 2_000_000)
        {
            float scale = Mathf.Pow(total / 2_000_000f, 1f / 3f) * 1.05f;
            cellSize *= scale;

            gridX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize));
            gridY = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cellSize));
            gridZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize));
        }

        cellHeads = new int[gridX * gridY * gridZ];
        for (int i = 0; i < cellHeads.Length; i++)
            cellHeads[i] = -1;

        usedCellCount = 0;
    }

    private void BuildGrid()
    {
        for (int i = 0; i < usedCellCount; i++)
            cellHeads[usedCells[i]] = -1;

        usedCellCount = 0;

        for (int i = 0; i < positions.Length; i++)
        {
            int cell = CellIndex(positions[i]);

            if (cellHeads[cell] == -1)
                usedCells[usedCellCount++] = cell;

            nextInCell[i] = cellHeads[cell];
            cellHeads[cell] = i;
        }
    }

    private int CellIndex(Vector3 position)
    {
        Vector3 offset = position - gridMin;

        int x = Mathf.Clamp((int)(offset.x / cellSize), 0, gridX - 1);
        int y = Mathf.Clamp((int)(offset.y / cellSize), 0, gridY - 1);
        int z = Mathf.Clamp((int)(offset.z / cellSize), 0, gridZ - 1);

        return x + gridX * (y + gridY * z);
    }


    // =====================================================================
    // Schule / Roaming
    // =====================================================================

    private void UpdateSchoolAndRoam(float time)
    {
        Vector3 centerSum = Vector3.zero;
        Vector3 headingSum = Vector3.zero;
        float panicSum = 0f;

        for (int i = 0; i < positions.Length; i++)
        {
            centerSum += positions[i];
            headingSum += headings[i];
            panicSum += panicLevels[i];
        }

        schoolCenter = centerSum / positions.Length;
        AveragePanic = panicSum / positions.Length;

        if (headingSum.sqrMagnitude > 0.000001f)
            schoolHeading = headingSum.normalized;

        if (!roamEnabled && !migrationActive)
            return;

        // Im Migrationsmodus entscheidet der LEADER über die Ankunft,
        // nicht der Schwerpunkt - sonst würde ein langer Schwanz das
        // Ziel künstlich hinauszögern.
        Vector3 arrivalReference =
            migrationActive && leaderIndex >= 0
                ? positions[leaderIndex]
                : schoolCenter;

        bool arrived =
            (arrivalReference - roamPoint).sqrMagnitude <
            roamArrivalRadius * roamArrivalRadius;

        if (arrived || time >= nextRoamChange)
        {
            AdvanceRoamTarget();
            nextRoamChange = time + RandomRange(roamMinInterval, roamMaxInterval);
        }

        if (drawRoamTarget)
        {
            Debug.DrawLine(arrivalReference, roamPoint, Color.green);
            Debug.DrawLine(
                roamPoint - Vector3.up * 0.4f,
                roamPoint + Vector3.up * 0.4f,
                Color.green);

            // Rundkurs andeuten.
            if (roamPattern == RoamPattern.Circuit && circuitPoints != null)
            {
                for (int k = 0; k < circuitPoints.Length; k++)
                {
                    Debug.DrawLine(
                        circuitPoints[k],
                        circuitPoints[(k + 1) % circuitPoints.Length],
                        new Color(0.2f, 0.9f, 0.6f, 0.5f));
                }
            }
        }
    }

    // Nächstes Ziel gemäß Muster: Zufallspunkt oder nächster Waypoint
    // der Runde (in gemerkter Umlaufrichtung).
    private void AdvanceRoamTarget()
    {
        if (roamPattern == RoamPattern.Circuit &&
            circuitPoints != null &&
            circuitPoints.Length > 0)
        {
            circuitIndex =
                (circuitIndex + circuitDirection + circuitPoints.Length) %
                circuitPoints.Length;

            roamPoint = circuitPoints[circuitIndex];
        }
        else
        {
            roamPoint = aquarium.GetRandomPoint(random, roamPadding);
        }
    }

    // Ellipse durchs Areal: an die Becken-Grundfläche angepasst (minus
    // Wandabstand), Höhe variiert sanft pro Waypoint. Umlaufrichtung zufällig.
    private void BuildCircuit()
    {
        int count = Mathf.Max(4, circuitWaypointCount);
        circuitPoints = new Vector3[count];

        Bounds bounds = aquarium.WorldBounds;
        Vector3 center = bounds.center;

        float radiusX = Mathf.Max(0.5f, bounds.extents.x - roamPadding);
        float radiusZ = Mathf.Max(0.5f, bounds.extents.z - roamPadding);

        float heightAmplitude =
            Mathf.Max(0f, bounds.extents.y - roamPadding * 0.5f) *
            circuitHeightVariation;

        float phase = (float)random.NextDouble() * Mathf.PI * 2f;
        circuitDirection = random.NextDouble() < 0.5 ? 1 : -1;

        for (int k = 0; k < count; k++)
        {
            float angle = phase + (k / (float)count) * Mathf.PI * 2f;

            circuitPoints[k] = new Vector3(
                center.x + Mathf.Cos(angle) * radiusX,
                center.y + Mathf.Sin(angle * 2.3f + phase) * heightAmplitude,
                center.z + Mathf.Sin(angle) * radiusZ);
        }

        circuitIndex = 0;
        roamPoint = circuitPoints[0];
    }

    // Beim Umschalten in den Circuit: am nächstgelegenen Waypoint einsteigen,
    // damit die Gruppe nicht quer durchs Becken zum Startpunkt zieht.
    private void SnapToNearestWaypoint()
    {
        if (circuitPoints == null || circuitPoints.Length == 0)
            return;

        int best = 0;
        float bestSqr = float.PositiveInfinity;

        for (int k = 0; k < circuitPoints.Length; k++)
        {
            float sqr = (circuitPoints[k] - schoolCenter).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = k;
            }
        }

        circuitIndex = best;
        roamPoint = circuitPoints[best];
        nextRoamChange = Time.time + RandomRange(roamMinInterval, roamMaxInterval);
    }


    // =====================================================================
    // Player-Sensing (Sandfisch-Regel: Bewegung triggert, nicht Nähe)
    // =====================================================================

    private void UpdatePlayerSensing(float deltaTime, float time)
    {
        playerPresent = playerTransform != null;

        if (!playerPresent)
        {
            hasLastPlayerPosition = false;
            return;
        }

        Vector3 currentPosition = playerTransform.position;
        playerWorldPosition = currentPosition;

        // Geschwindigkeit aus der Positionsänderung ableiten und glätten,
        // damit ein einzelner Ruckel-Frame nicht sofort schreckt.
        if (hasLastPlayerPosition && deltaTime > 0.0001f)
        {
            Vector3 rawVelocity =
                (currentPosition - lastPlayerPosition) / deltaTime;

            float blend = 1f - Mathf.Exp(-8f * deltaTime);
            playerVelocity = Vector3.Lerp(playerVelocity, rawVelocity, blend);
        }

        lastPlayerPosition = currentPosition;
        hasLastPlayerPosition = true;

        // Letzte gültige Bewegungsrichtung merken - sie richtet im
        // Folge-Modus die Formation aus (auch wenn der Player stehen bleibt).
        if (playerVelocity.sqrMagnitude > 0.25f)
            playerFollowHeading = playerVelocity.normalized;

        // Im Folge-Modus vertrauen die Boids dem Player: kein Sprint-Schreck
        // (sonst würde man die eigene Eskorte versprengen).
        if (followPlayerActive)
            return;

        // Bewegungs-Gating: langsam = unsichtbar.
        if (playerVelocity.magnitude < playerStartleSpeed)
            return;

        if (time < nextPlayerStartleTime)
            return;

        // Nur schrecken, wenn der schnelle Player wirklich jemandem nah ist.
        float triggerSquared = playerStartleRadius * playerStartleRadius;

        for (int i = 0; i < positions.Length; i++)
        {
            if ((positions[i] - currentPosition).sqrMagnitude < triggerSquared)
            {
                StartleAt(currentPosition, playerStartleRadius * 1.8f);
                nextPlayerStartleTime = time + playerStartleCooldown;
                break;
            }
        }
    }


    // =====================================================================
    // Ausweich-Sensorik (gestaffelte SphereCasts)
    // =====================================================================

    private void UpdateAvoidanceSensors(float time)
    {
        if (obstacleMask.value == 0)
            return;

        float interval = 1f / Mathf.Max(2f, avoidProbeFrequency);

        for (int i = 0; i < positions.Length; i++)
        {
            if (time < nextProbeTimes[i])
                continue;

            nextProbeTimes[i] = time + interval;
            ProbeObstacles(i);
        }
    }

    private void ProbeObstacles(int index)
    {
        Vector3 origin = positions[index];
        Vector3 forward = headings[index];

        // Notfall: Boid steckt bereits in einem Collider.
        int overlaps = Physics.OverlapSphereNonAlloc(
            origin, avoidProbeRadius, overlapBuffer,
            obstacleMask, QueryTriggerInteraction.Ignore);

        if (overlaps > 0)
        {
            avoidDirections[index] = EscapeDirection(origin, overlaps);
            avoidUrgencies[index] = 1f;
            return;
        }

        float lookAhead =
            avoidLookAheadBase + speeds[index] * avoidLookAheadPerSpeed;

        bool blocked = Physics.SphereCast(
            origin, avoidProbeRadius, forward,
            out RaycastHit hit, lookAhead,
            obstacleMask, QueryTriggerInteraction.Ignore);

        if (!blocked)
        {
            // Dringlichkeit klingt im Hauptloop ab; hier nichts zu tun.
            return;
        }

        // Dringlichkeit: früh spürbar (Wurzel), 1 direkt vor dem Hindernis.
        float urgency = Mathf.Sqrt(
            Mathf.Clamp01(1f - hit.distance / Mathf.Max(lookAhead, 0.001f)));

        avoidUrgencies[index] = Mathf.Max(avoidUrgencies[index], urgency);

        float clearDistance = lookAhead * 0.9f;

        // 1) Hysterese: bereits gewählte Richtung behalten, solange sie frei
        //    ist. Verhindert Links/Rechts-Flackern und hält den Bogen zusammen.
        Vector3 previous = avoidDirections[index];

        if (previous.sqrMagnitude > 0.5f &&
            Vector3.Dot(previous, forward) > 0.05f &&
            !Physics.SphereCast(
                origin, avoidProbeRadius, previous,
                out _, clearDistance,
                obstacleMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        // 2) Fächer durchsuchen: sortiert von "fast geradeaus" nach
        //    "seitlich" - die ERSTE freie Richtung ist die mit der kleinsten
        //    Kursabweichung. Genau das erzeugt das Streifen an der Silhouette
        //    (Kurve ums Objekt) statt einer Reflexion.
        Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.96f
            ? Vector3.right
            : Vector3.up;

        Quaternion basis = Quaternion.LookRotation(forward, up);
        Quaternion roll = Quaternion.AngleAxis(probeRolls[index], Vector3.forward);

        for (int c = 0; c < candidateDirections.Length; c++)
        {
            Vector3 candidate =
                (basis * (roll * candidateDirections[c])).normalized;

            if (!Physics.SphereCast(
                    origin, avoidProbeRadius, candidate,
                    out _, clearDistance,
                    obstacleMask, QueryTriggerInteraction.Ignore))
            {
                avoidDirections[index] = candidate;
                return;
            }
        }

        // 3) Fallback: alles blockiert -> an der getroffenen Fläche entlang
        //    sliden (nie zurückprallen).
        Vector3 slide = Vector3.ProjectOnPlane(forward, hit.normal);

        avoidDirections[index] = slide.sqrMagnitude > 0.000001f
            ? (slide.normalized + hit.normal * 0.35f).normalized
            : hit.normal;

        avoidUrgencies[index] = 1f;
    }

    private Vector3 EscapeDirection(Vector3 position, int overlapCount)
    {
        Vector3 escape = Vector3.zero;

        for (int i = 0; i < overlapCount; i++)
        {
            Collider collider = overlapBuffer[i];
            if (collider == null)
                continue;

            Vector3 away = position - collider.ClosestPoint(position);

            if (away.sqrMagnitude < 0.000001f)
                away = position - collider.bounds.center;

            if (away.sqrMagnitude > 0.000001f)
                escape += away.normalized;
        }

        return escape.sqrMagnitude > 0.000001f
            ? escape.normalized
            : RandomUnitVector();
    }


    // =====================================================================
    // Hauptschritt: Votes sammeln, Heading drehen, integrieren
    // =====================================================================

    private void Step(float deltaTime, float time)
    {
        float perceptionSquared = perceptionRadius * perceptionRadius;
        float separationSquared = separationRadius * separationRadius;

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 position = positions[i];
            Vector3 heading = headings[i];
            float speed = speeds[i];

            // ---------------------------------------------------------
            // Panik-Level aktualisieren (Attack schnell, Release weich)
            // ---------------------------------------------------------
            float panic = panicLevels[i];
            {
                bool inPanicWindow =
                    time >= panicStartTimes[i] &&
                    time <= panicEndTimes[i];

                float target = inPanicWindow ? 1f : 0f;

                float rate = inPanicWindow
                    ? 6f                                     // zuschnappen
                    : 1f / Mathf.Max(0.2f, panicCalmTime);   // weich abklingen

                panic = Mathf.MoveTowards(panic, target, rate * deltaTime);
                panicLevels[i] = panic;
            }

            // 1 = ruhig, 0 = volle Panik. Skaliert die "sozialen" Votes.
            float calm = 1f - panic;

            // Migrations-Rollen für diesen Boid.
            bool inMigration = migrationActive && leaderIndex >= 0;
            bool isLeader = inMigration && i == leaderIndex;
            bool followingPlayer = followPlayerActive && playerPresent;
            float followCatchUp = 0f; // 0 = am Platz, 1 = weit zurückgefallen

            // Landing: 1 = weit weg vom Landeplatz (normal fliegen),
            // 0 = angekommen (idlen). Bleibt 1, wenn nicht gelandet wird.
            float landApproach = 1f;

            // ---------------------------------------------------------
            // Nachbarn einsammeln
            // ---------------------------------------------------------
            Vector3 separationSum = Vector3.zero;
            Vector3 alignmentSum = Vector3.zero;
            Vector3 cohesionSum = Vector3.zero;
            float cohesionWeightSum = 0f;
            float separationActivity = 0f;

            Vector3 gridOffset = position - gridMin;
            int cx = Mathf.Clamp((int)(gridOffset.x / cellSize), 0, gridX - 1);
            int cy = Mathf.Clamp((int)(gridOffset.y / cellSize), 0, gridY - 1);
            int cz = Mathf.Clamp((int)(gridOffset.z / cellSize), 0, gridZ - 1);

            for (int z = Mathf.Max(0, cz - 1); z <= Mathf.Min(gridZ - 1, cz + 1); z++)
            for (int y = Mathf.Max(0, cy - 1); y <= Mathf.Min(gridY - 1, cy + 1); y++)
            for (int x = Mathf.Max(0, cx - 1); x <= Mathf.Min(gridX - 1, cx + 1); x++)
            {
                int neighbour = cellHeads[x + gridX * (y + gridY * z)];

                while (neighbour != -1)
                {
                    if (neighbour != i)
                    {
                        Vector3 difference = positions[neighbour] - position;
                        float sqrDistance = difference.sqrMagnitude;

                        if (sqrDistance < perceptionSquared &&
                            sqrDistance > 0.000001f)
                        {
                            float distance = Mathf.Sqrt(sqrDistance);

                            // Separation: quadratisch anwachsend im Nahbereich.
                            if (sqrDistance < separationSquared)
                            {
                                float closeness =
                                    1f - distance / separationRadius;

                                separationSum -=
                                    (difference / distance) *
                                    (closeness * closeness);

                                separationActivity =
                                    Mathf.Max(separationActivity, closeness);
                            }

                            // Alignment: nahe Nachbarn zählen stärker.
                            float alignWeight =
                                1f - distance / perceptionRadius;

                            alignmentSum +=
                                headings[neighbour] *
                                (alignWeight * alignWeight);

                            // Cohesion: erst jenseits der Separationszone.
                            float cohesionT = Mathf.InverseLerp(
                                separationRadius, perceptionRadius, distance);

                            cohesionSum += positions[neighbour] * cohesionT;
                            cohesionWeightSum += cohesionT;
                        }
                    }

                    neighbour = nextInCell[neighbour];
                }
            }

            // ---------------------------------------------------------
            // Richtungs-Votes aufsummieren
            // ---------------------------------------------------------
            // Basis-Vote: aktueller Kurs (Trägheit / Glättung in einem).
            Vector3 vote = heading;

            if (separationSum.sqrMagnitude > 0.000001f)
                vote += separationSum.normalized *
                        (separationStrength * separationActivity);

            // Player-Ausweichen: der Schwarm teilt sich sanft um den Player,
            // wie um einen großen Artgenossen. Wirkt auch auf Sitzende
            // (die rutschen dann zur Seite) und unabhängig von Panik.
            if (playerPresent && playerAvoidRadius > 0.01f)
            {
                Vector3 awayFromPlayer = position - playerWorldPosition;
                float playerSqr = awayFromPlayer.sqrMagnitude;

                if (playerSqr < playerAvoidRadius * playerAvoidRadius &&
                    playerSqr > 0.000001f)
                {
                    float playerDistance = Mathf.Sqrt(playerSqr);
                    float playerCloseness =
                        1f - playerDistance / playerAvoidRadius;

                    vote += (awayFromPlayer / playerDistance) *
                            (playerAvoidStrength *
                             playerCloseness * playerCloseness);
                }
            }

            if (alignmentSum.sqrMagnitude > 0.000001f)
                vote += alignmentSum.normalized *
                        (alignmentStrength *
                         Mathf.Lerp(1f, 0.6f, panic) *
                         (isLeader ? 0.4f : 1f));

            float cohesionActivity = 0f;

            if (cohesionWeightSum > 0.0001f)
            {
                Vector3 toCenter =
                    cohesionSum / cohesionWeightSum - position;

                float centerDistance = toCenter.magnitude;

                if (centerDistance > 0.001f)
                {
                    // Weit weg vom lokalen Zentrum -> stärkerer Zug.
                    // In Panik zählt Zusammenhalt nichts (calm-Faktor).
                    // Der Leader lässt sich kaum zurückziehen.
                    cohesionActivity = Mathf.Clamp01(
                        centerDistance / perceptionRadius);

                    vote += (toCenter / centerDistance) *
                            (cohesionStrength * cohesionActivity * calm *
                             (isLeader ? 0.25f : 1f));
                }
            }

            // Roaming: persönlicher Zielpunkt (gemeinsames Ziel + Versatz).
            // In Panik interessiert das Ziel niemanden (calm-Faktor).
            // Im Migrations-/Folge-Modus entscheidet der Anker über den Weg.
            // Während einer Landung ruht das Roaming komplett.
            if (roamEnabled && roamStrength > 0f && calm > 0.01f &&
                !inMigration && !followingPlayer && !landingActive)
            {
                Vector3 toRoam = (roamPoint + roamOffsets[i]) - position;

                if (toRoam.sqrMagnitude > 0.01f)
                    vote += toRoam.normalized * (roamStrength * calm);
            }

            // Migration / Folgt-mir: Leader zieht zum Ziel, Follower zu
            // ihrem Platz hinter dem Anker (Leader-Boid ODER Player).
            if ((inMigration || followingPlayer) &&
                calm > 0.01f && !landingActive)
            {
                if (isLeader)
                {
                    Vector3 toGoal = roamPoint - position;

                    if (toGoal.sqrMagnitude > 0.04f)
                        vote += toGoal.normalized *
                                (leaderRoamStrength * calm);
                }
                else
                {
                    // Anker bestimmen: Player hat Vorrang.
                    Vector3 anchorPosition;
                    Vector3 anchorHeading;

                    if (followingPlayer)
                    {
                        anchorPosition = playerWorldPosition;
                        anchorHeading = playerFollowHeading;
                    }
                    else
                    {
                        anchorPosition = positions[leaderIndex];
                        anchorHeading = headings[leaderIndex];
                    }

                    // Persönlichen Platz aus dem Anker-Koordinatensystem
                    // in Weltkoordinaten übersetzen.
                    Vector3 anchorSide =
                        Vector3.Cross(Vector3.up, anchorHeading);

                    if (anchorSide.sqrMagnitude < 0.0001f)
                        anchorSide = Vector3.right;
                    anchorSide.Normalize();

                    Vector3 anchorVert =
                        Vector3.Cross(anchorHeading, anchorSide);

                    Vector3 slotOffset = followSlots[i];

                    Vector3 slot =
                        anchorPosition +
                        anchorSide * slotOffset.x +
                        anchorVert * slotOffset.y +
                        anchorHeading * slotOffset.z;

                    Vector3 toSlot = slot - position;
                    float slotDistance = toSlot.magnitude;

                    if (slotDistance > 0.05f)
                        vote += (toSlot / slotDistance) *
                                (followStrength * calm);

                    // Wie weit hinter dem eigenen Platz? -> Aufhol-Faktor
                    // für die Geschwindigkeitsregelung weiter unten.
                    followCatchUp = Mathf.Clamp01(
                        (slotDistance - 1f) /
                        Mathf.Max(0.5f, catchUpDistance)) * calm;
                }
            }

            // Flucht: beim Schreck eingefrorene Richtung weg vom Ursprung.
            if (panic > 0.001f && fleeDirections[i].sqrMagnitude > 0.5f)
                vote += fleeDirections[i] * (panicFleeStrength * panic);

            // Rally: aktiver Sammelruf zieht alle zum Sammelpunkt.
            if (time < rallyUntil)
            {
                Vector3 toRally = rallyPoint - position;

                if (toRally.sqrMagnitude > 0.25f)
                    vote += toRally.normalized * rallyStrength;
            }

            // Landing: Anflug auf den persönlichen Landeplatz.
            // landApproach steuert weiter unten das Abbremsen (Arrive)
            // sowie die Dämpfung von Wander und Containment.
            if (landingActive && calm > 0.01f)
            {
                Vector3 toLandSlot = landingSlotsWorld[i] - position;
                float slotDistance = toLandSlot.magnitude;

                landApproach = Mathf.Clamp01(
                    slotDistance / Mathf.Max(0.2f, landSlowdownRadius));

                if (slotDistance > 0.05f)
                    vote += (toLandSlot / slotDistance) *
                            (landStrength * calm);
            }

            // Wander: kontinuierliche Schlängellinie (seitlich + vertikal).
            // In Panik deutlich nervöser.
            if (wanderStrength > 0f)
            {
                float noiseTime = time * wanderFrequency;

                float lateral = Mathf.PerlinNoise(
                    noiseSeeds[i] * 1.37f, noiseTime) - 0.5f;

                float vertical = Mathf.PerlinNoise(
                    noiseSeeds[i] * 2.71f, noiseTime + 31.7f) - 0.5f;

                Vector3 side = Vector3.Cross(Vector3.up, heading);
                if (side.sqrMagnitude < 0.0001f)
                    side = Vector3.right;
                side.Normalize();

                Vector3 vertAxis = Vector3.Cross(heading, side);

                float effectiveWander =
                    wanderStrength *
                    (1f + panicWanderBoost * panic) *
                    (isLeader ? 0.5f : 1f);

                // Gelandete Boids wackeln nur noch minimal.
                if (landingActive)
                    effectiveWander *= Mathf.Lerp(
                        0.15f, 1f, Mathf.Max(landApproach, panic));

                vote += (side * lateral + vertAxis * vertical * 0.6f) *
                        (2f * effectiveWander);
            }

            // Containment: weiches Randfeld, aktuell + vorhergesagt.
            float containment = aquarium.GetContainment(
                position, containmentMargin, out Vector3 inward);

            if (containmentPredictionTime > 0f)
            {
                Vector3 predicted =
                    position + heading * (speed * containmentPredictionTime);

                float predictedStrength = aquarium.GetContainment(
                    predicted, containmentMargin, out Vector3 predictedInward);

                predictedStrength *= 0.8f;

                if (predictedStrength > containment)
                {
                    containment = predictedStrength;
                    inward = inward.sqrMagnitude > 0.5f
                        ? (inward + predictedInward).normalized
                        : predictedInward;
                }
            }

            float containmentShaped = 0f;

            if (containment > 0.001f && inward.sqrMagnitude > 0.5f)
            {
                containmentShaped = Mathf.Pow(
                    containment, containmentFalloffPower);

                // Gelandete Boids sitzen bewusst in der Randzone (Boden) -
                // das Containment darf sie dort nicht wegdrücken. Im Anflug
                // (landApproach ~1) und in Panik bleibt es voll aktiv.
                if (landingActive)
                    containmentShaped *= Mathf.Max(landApproach, panic);

                vote += inward * (containmentStrength * containmentShaped);
            }

            // Ausweichen: Dringlichkeit klingt ab, solange der Weg frei ist.
            float urgency = avoidUrgencies[i];

            if (urgency > 0.001f)
            {
                avoidUrgencies[i] = Mathf.MoveTowards(
                    urgency, 0f, avoidUrgencyDecay * deltaTime);

                if (avoidDirections[i].sqrMagnitude > 0.5f)
                    vote += avoidDirections[i] * (avoidStrength * urgency);

                if (drawAvoidance)
                    Debug.DrawLine(
                        position,
                        position + avoidDirections[i] * debugLineLength * 1.5f,
                        Color.red);
            }

            // ---------------------------------------------------------
            // Heading drehen (krümmungsbegrenzte Bahn)
            // ---------------------------------------------------------
            Vector3 targetDirection = vote.sqrMagnitude > 0.000001f
                ? vote.normalized
                : heading;

            float urgencyMax = Mathf.Max(containmentShaped, urgency);

            float turnRate =
                turnRateDegrees *
                turnScales[i] *
                Mathf.Lerp(1f, urgencyTurnBoost, urgencyMax) *
                Mathf.Lerp(1f, panicTurnBoost, panic);

            float remainingAngle = Vector3.Angle(heading, targetDirection);

            Vector3 previousHeading = heading;

            heading = Vector3.RotateTowards(
                heading,
                targetDirection,
                turnRate * Mathf.Deg2Rad * deltaTime,
                0f).normalized;

            // ---------------------------------------------------------
            // Geschwindigkeit: persönliche Reisegeschwindigkeit + Burst&Glide
            // ---------------------------------------------------------
            float agentMin = minSpeed * speedScales[i];
            float agentMax = Mathf.Max(agentMin + 0.01f, maxSpeed * speedScales[i]);

            float cruise = Mathf.Lerp(agentMin, agentMax, 0.6f);

            if (burstGlideAmount > 0f)
            {
                float pulse = Mathf.PerlinNoise(
                    noiseSeeds[i], time * burstGlideFrequency) * 2f - 1f;

                cruise *= 1f + burstGlideAmount * 0.5f * pulse;
            }

            // Panik: Vollgas, kurz sogar über das normale Maximum hinaus.
            float speedCap =
                agentMax * Mathf.Lerp(1f, panicSpeedMultiplier, panic);

            cruise = Mathf.Lerp(cruise, speedCap, panic);

            // Migration: Leader reist unter Maximum (damit der Schwanz
            // aufschließen kann), Follower beschleunigen je nach Rückstand.
            if (inMigration)
            {
                if (isLeader)
                    cruise *= leaderSpeedFactor;
                else
                    cruise = Mathf.Lerp(cruise * 0.95f, speedCap, followCatchUp);
            }
            // Folgt-mir: Tempo-Matching mit dem Player. Steht er still,
            // hovern die Boids fast bewegungslos auf ihren Plätzen statt
            // dauerhaft um sie herumzukreisen; ist er unterwegs, fahren
            // sie sein Tempo mit und holen bei Rückstand auf.
            else if (followingPlayer)
            {
                float hover = agentMin * 0.5f;

                float matchSpeed = Mathf.Clamp(
                    playerVelocity.magnitude * 1.05f,
                    hover,
                    speedCap);

                cruise = Mathf.Lerp(matchSpeed, speedCap, followCatchUp);
            }

            // In scharfen Kurven leicht abbremsen.
            float turnSlow = Mathf.Lerp(
                1f, sharpTurnSpeedFactor,
                Mathf.InverseLerp(30f, 110f, remainingAngle));

            // Im Folge-Modus darf das Tempo unter das normale Minimum
            // fallen (Hovern am Platz, wenn der Player steht).
            float speedFloor = followingPlayer ? agentMin * 0.5f : agentMin;

            float targetSpeed = Mathf.Clamp(cruise * turnSlow, speedFloor, speedCap);

            // Landing-Arrive: je näher am Platz, desto stärker Richtung
            // Idle-Tempo. Panik hebt das sofort wieder auf (Flucht geht vor).
            if (landingActive)
            {
                float arrive = Mathf.Max(landApproach, panic);
                targetSpeed = Mathf.Lerp(landIdleSpeed, targetSpeed, arrive);
            }

            speed = Mathf.MoveTowards(
                speed, targetSpeed, speedAcceleration * deltaTime);

            // ---------------------------------------------------------
            // Integration + Safety (Slide, nie Bounce)
            // ---------------------------------------------------------
            Vector3 displacement = heading * (speed * deltaTime);
            Vector3 newPosition = position + displacement;

            if (continuousSafety &&
                obstacleMask.value != 0 &&
                displacement.sqrMagnitude > 0.000001f)
            {
                float travel = displacement.magnitude;
                Vector3 travelDirection = displacement / travel;

                if (Physics.SphereCast(
                        position,
                        Mathf.Max(agentRadius, avoidProbeRadius * 0.9f),
                        travelDirection,
                        out RaycastHit safetyHit,
                        travel + safetyMargin,
                        obstacleMask,
                        QueryTriggerInteraction.Ignore))
                {
                    float safeTravel =
                        Mathf.Max(0f, safetyHit.distance - safetyMargin);

                    newPosition = position + travelDirection * safeTravel;

                    // Slide: Kurs auf die Oberfläche projizieren, kleiner
                    // Anteil von der Fläche weg. Kein Rückprall.
                    Vector3 slide =
                        Vector3.ProjectOnPlane(heading, safetyHit.normal);

                    if (slide.sqrMagnitude > 0.000001f)
                        heading = (slide.normalized +
                                   safetyHit.normal * 0.1f).normalized;
                    else
                        heading = safetyHit.normal;

                    avoidDirections[i] = heading;
                    avoidUrgencies[i] = 1f;
                }
            }

            // Aquarium-Failsafe: zurückholen und an der Wand entlang weiter.
            if (aquarium.ClampInside(
                    ref newPosition,
                    agentRadius,
                    out Vector3 wallInward))
            {
                Vector3 alongWall =
                    Vector3.ProjectOnPlane(heading, wallInward);

                heading = alongWall.sqrMagnitude > 0.000001f
                    ? (alongWall.normalized + wallInward * 0.15f).normalized
                    : wallInward;
            }

            positions[i] = newPosition;
            headings[i] = heading;
            speeds[i] = speed;

            // ---------------------------------------------------------
            // Orientierung + Banking (beide Render-Modi)
            // ---------------------------------------------------------
            Vector3 upAxis = Mathf.Abs(Vector3.Dot(heading, Vector3.up)) > 0.96f
                ? Vector3.forward
                : Vector3.up;

            Quaternion targetRotation =
                Quaternion.LookRotation(heading, upAxis);

            if (maxBankAngle > 0f)
            {
                // Gierrate aus der Heading-Änderung dieses Frames.
                float yawDelta = Vector3.SignedAngle(
                    previousHeading, heading, Vector3.up);

                float yawRate = deltaTime > 0.0001f
                    ? yawDelta / deltaTime
                    : 0f;

                float targetBank = Mathf.Clamp(
                    -yawRate * bankPerTurnRate,
                    -maxBankAngle, maxBankAngle);

                float bankBlend =
                    1f - Mathf.Exp(-bankResponsiveness * deltaTime);

                bankAngles[i] = Mathf.Lerp(bankAngles[i], targetBank, bankBlend);

                targetRotation *= Quaternion.AngleAxis(
                    bankAngles[i], Vector3.forward);
            }

            float rotationBlend =
                Mathf.Clamp01(orientationResponsiveness * deltaTime);

            visualRotations[i] = Quaternion.Slerp(
                visualRotations[i], targetRotation, rotationBlend);

            if (renderMode == BoidRenderMode.GameObjects)
            {
                agentTransforms[i].SetPositionAndRotation(
                    newPosition, visualRotations[i]);

                agents[i].SetActivity(
                    separationActivity,
                    panic,   // Slot "Alignment" transportiert das Panik-Level
                    cohesionActivity,
                    avoidUrgencies[i],
                    containmentShaped);
            }
            else
            {
                // Instanced: Matrix in den Batch schreiben; gezeichnet wird
                // gesammelt in RenderInstancedBoids().
                instanceBatches[i / InstanceBatchSize][i % InstanceBatchSize] =
                    Matrix4x4.TRS(
                        newPosition,
                        visualRotations[i],
                        instancedScale * visualScales[i]);
            }

            if (drawHeadings)
                Debug.DrawLine(
                    newPosition,
                    newPosition + heading * debugLineLength,
                    Color.green);
        }
    }


    // =====================================================================
    // Öffentliche API (für BoidTestPanel / Gameplay)
    // =====================================================================

    public Vector3 SchoolCenter => schoolCenter;

    /// <summary>Mittlere Panik der Schule (0..1), z.B. für UI-Anzeige.</summary>
    public float AveragePanic { get; private set; }

    /// <summary>
    /// Löst eine Panikwelle aus. Boids im Radius fliehen vom Ursprung weg;
    /// ferne Boids reagieren später (Welle läuft mit panicWaveSpeed durch
    /// die Gruppe). Nach panicDuration klingt die Panik individuell ab und
    /// Cohesion + Roaming sammeln die Gruppe von selbst wieder ein.
    /// </summary>
    public void StartleAt(Vector3 origin, float radius)
    {
        if (positions == null)
            return;

        float time = Time.time;
        float radiusSquared = radius * radius;

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 away = positions[i] - origin;
            float sqrDistance = away.sqrMagnitude;

            if (sqrDistance > radiusSquared)
                continue;

            float distance = Mathf.Sqrt(sqrDistance);

            // Welle: Reaktionszeit wächst mit der Distanz, plus etwas
            // individueller Jitter, damit es nicht wie ein Ring aussieht.
            float delay =
                distance / Mathf.Max(1f, panicWaveSpeed) *
                (1f + RandomSigned() * 0.25f);

            panicStartTimes[i] = time + Mathf.Max(0f, delay);
            panicEndTimes[i] =
                panicStartTimes[i] +
                panicDuration * (1f + RandomSigned() * 0.3f);

            fleeDirections[i] = distance > 0.05f
                ? away / distance
                : RandomUnitVector();
        }

        // Ein laufender Rally-Ruf wird vom Schreck unterbrochen.
        rallyUntil = float.NegativeInfinity;
    }

    /// <summary>Schreckt die gesamte Schule auf (Ursprung = Schwerpunkt).</summary>
    public void StartleAll()
    {
        StartleAt(schoolCenter, float.PositiveInfinity);
    }

    /// <summary>
    /// Aktives Sammeln: Alle Boids ziehen für 'duration' Sekunden stark zum
    /// aktuellen Schwerpunkt. Bricht laufende Panik ab.
    /// </summary>
    public void Rally(float duration = 4f)
    {
        if (positions == null)
            return;

        rallyPoint = schoolCenter;
        rallyUntil = Time.time + Mathf.Max(0.5f, duration);
        CloseAllPanicWindows();
    }

    /// <summary>Panik sofort beenden (klingt weich über panicCalmTime ab).</summary>
    public void CalmDown()
    {
        rallyUntil = float.NegativeInfinity;
        CloseAllPanicWindows();
    }

    /// <summary>Erzwingt sofort das nächste Ziel (gemäß aktivem Muster).</summary>
    public void ForceNewRoamTarget()
    {
        if (random == null)
            return;

        AdvanceRoamTarget();
        nextRoamChange = Time.time + RandomRange(roamMinInterval, roamMaxInterval);
    }

    /// <summary>Läuft gerade eine Leader-Wanderung?</summary>
    public bool MigrationActive => migrationActive;

    /// <summary>
    /// Startet/stoppt die Leader-Wanderung. Beim Start wird der Boid am
    /// nächsten zum Schwerpunkt zum Leader (hochskaliert); beim Stopp wird
    /// seine Größe zurückgesetzt und alle roamen wieder gleichberechtigt.
    /// </summary>
    public void SetMigration(bool active)
    {
        if (positions == null || active == migrationActive)
            return;

        if (active)
        {
            // "Folgt mir" und Boid-Leader schließen sich aus.
            followPlayerActive = false;

            // Leader = Boid, der dem Schwerpunkt am nächsten ist. Wirkt
            // natürlicher, als wenn ein Randflieger plötzlich führt.
            int best = 0;
            float bestSqr = float.PositiveInfinity;

            for (int i = 0; i < positions.Length; i++)
            {
                float sqr = (positions[i] - schoolCenter).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            leaderIndex = best;

            if (renderMode == BoidRenderMode.GameObjects)
            {
                leaderBaseScale = agentTransforms[leaderIndex].localScale;
                agentTransforms[leaderIndex].localScale =
                    leaderBaseScale * leaderScaleMultiplier;
            }
            else
            {
                visualScales[leaderIndex] = leaderScaleMultiplier;
            }

            ForceNewRoamTarget();
        }
        else if (leaderIndex >= 0)
        {
            if (renderMode == BoidRenderMode.GameObjects)
                agentTransforms[leaderIndex].localScale = leaderBaseScale;
            else
                visualScales[leaderIndex] = 1f;

            leaderIndex = -1;
        }

        migrationActive = active;
    }

    public void ToggleMigration()
    {
        SetMigration(!migrationActive);
    }

    /// <summary>Ist der Player in der BoidSchool zugewiesen?</summary>
    public bool HasPlayer => playerTransform != null;

    /// <summary>Folgen die Boids gerade dem Player?</summary>
    public bool FollowPlayerActive => followPlayerActive;

    /// <summary>
    /// "Folgt mir": Alle Boids nutzen ihre Follow-Plätze relativ zum Player.
    /// Beendet eine laufende Boid-Leader-Wanderung. Braucht ein gesetztes
    /// Player Transform.
    /// </summary>
    public void SetFollowPlayer(bool active)
    {
        if (active == followPlayerActive)
            return;

        if (active)
        {
            if (playerTransform == null)
            {
                Debug.LogWarning(
                    "[BoidSchool] 'Folgt mir' braucht ein gesetztes " +
                    "Player Transform (Header 'Player Sensing').", this);
                return;
            }

            SetMigration(false);
        }

        followPlayerActive = active;
    }

    public void ToggleFollowPlayer()
    {
        SetFollowPlayer(!followPlayerActive);
    }

    /// <summary>Aktuelles Erkundungsmuster (Zufallsziele oder Rundkurs).</summary>
    public RoamPattern CurrentRoamPattern => roamPattern;

    /// <summary>
    /// Umschalten zwischen Zufallszielen und dem großen Rundkurs. Beim
    /// Wechsel in den Circuit steigt die Gruppe am nächstgelegenen
    /// Waypoint ein.
    /// </summary>
    public void SetRoamPattern(RoamPattern pattern)
    {
        if (pattern == roamPattern)
            return;

        roamPattern = pattern;

        if (positions == null)
            return;

        if (pattern == RoamPattern.Circuit)
        {
            BuildCircuit();
            SnapToNearestWaypoint();
        }
        else
        {
            ForceNewRoamTarget();
        }
    }

    public void ToggleRoamPattern()
    {
        SetRoamPattern(roamPattern == RoamPattern.Circuit
            ? RoamPattern.RandomTargets
            : RoamPattern.Circuit);
    }

    /// <summary>Sitzt die Schule gerade (oder ist im Anflug)?</summary>
    public bool LandingActive => landingActive;

    /// <summary>
    /// Alle Boids fliegen den Punkt an und setzen sich dort auf verteilte
    /// Plätze (Scheibe mit landSpreadRadius), bremsen im Anflug ab und
    /// idlen bei landIdleSpeed. Ein Schreck unterbricht das Sitzen; danach
    /// kehren alle von selbst zu ihren Plätzen zurück.
    /// </summary>
    public void LandAt(Vector3 point)
    {
        if (positions == null)
            return;

        landingPoint = point;

        for (int i = 0; i < landingSlotsWorld.Length; i++)
        {
            // Gleichmäßig gefüllte Scheibe (sqrt-Verteilung) um den Punkt,
            // leicht über dem Boden, dann sicher ins Becken geclampt.
            float radius =
                Mathf.Sqrt((float)random.NextDouble()) * landSpreadRadius;

            float angle = (float)random.NextDouble() * Mathf.PI * 2f;

            Vector3 slot = point + new Vector3(
                Mathf.Cos(angle) * radius,
                agentRadius * 1.2f,
                Mathf.Sin(angle) * radius);

            aquarium.ClampInside(ref slot, agentRadius * 1.5f, out _);
            landingSlotsWorld[i] = slot;
        }

        landingActive = true;
        rallyUntil = float.NegativeInfinity;
    }

    /// <summary>Landet auf dem Beckenboden unter dem aktuellen Schwerpunkt.</summary>
    public void LandBelowSchoolCenter()
    {
        Bounds bounds = aquarium.WorldBounds;

        LandAt(new Vector3(
            schoolCenter.x,
            bounds.min.y,
            schoolCenter.z));
    }

    /// <summary>Beendet das Sitzen; alle kehren zum normalen Verhalten zurück.</summary>
    public void TakeOff()
    {
        landingActive = false;
    }

    private void CloseAllPanicWindows()
    {
        if (panicStartTimes == null)
            return;

        for (int i = 0; i < panicStartTimes.Length; i++)
        {
            panicStartTimes[i] = float.PositiveInfinity;
            panicEndTimes[i] = 0f;
        }
    }


    // =====================================================================
    // Instanced Rendering
    // =====================================================================

    // Bereitet Mesh, Material und RenderParams vor. false = Fallback auf
    // GameObjects (z.B. wenn kein Material zugewiesen ist).
    private bool TrySetupInstancedRendering()
    {
        if (instancedMaterial == null)
        {
            Debug.LogWarning(
                "[BoidSchool] Instanced-Modus braucht ein Material im Header " +
                "'Rendering' - Fallback auf GameObjects.", this);
            return false;
        }

        if (!instancedMaterial.enableInstancing)
        {
            instancedMaterial.enableInstancing = true;
            Debug.Log(
                "[BoidSchool] 'Enable GPU Instancing' wurde auf dem " +
                "Boid-Material automatisch aktiviert.", this);
        }

        resolvedInstancedMesh = instancedMesh;

        // Fallback: Unity-Sphere aus einem Wegwerf-Primitive ziehen.
        if (resolvedInstancedMesh == null)
        {
            GameObject temp =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            resolvedInstancedMesh =
                temp.GetComponent<MeshFilter>().sharedMesh;

            Destroy(temp);

            Debug.Log(
                "[BoidSchool] Kein Instanced-Mesh zugewiesen - nutze die " +
                "Unity-Sphere (768 Tris). Für viele Boids lohnt ein " +
                "Low-Poly-Mesh (~80 Tris) deutlich.", this);
        }

        Bounds bounds = aquarium.WorldBounds;
        bounds.Expand(bounds.size.magnitude * 0.1f);

        instancedRenderParams = new RenderParams(instancedMaterial)
        {
            worldBounds = bounds,
            shadowCastingMode = disableBoidShadows
                ? UnityEngine.Rendering.ShadowCastingMode.Off
                : UnityEngine.Rendering.ShadowCastingMode.On,
            receiveShadows = !disableBoidShadows
        };

        return true;
    }

    private void RenderInstancedBoids()
    {
        if (renderMode != BoidRenderMode.Instanced ||
            instanceBatches == null ||
            resolvedInstancedMesh == null)
        {
            return;
        }

        for (int b = 0; b < instanceBatches.Length; b++)
        {
            Graphics.RenderMeshInstanced(
                instancedRenderParams,
                resolvedInstancedMesh,
                0,
                instanceBatches[b],
                instanceBatches[b].Length);
        }
    }


    // =====================================================================
    // Helpers
    // =====================================================================

    private Vector3 RandomUnitVector()
    {
        Vector3 result;

        do
        {
            result = new Vector3(RandomSigned(), RandomSigned(), RandomSigned());
        }
        while (result.sqrMagnitude < 0.0001f || result.sqrMagnitude > 1f);

        return result.normalized;
    }

    private float RandomSigned()
    {
        return (float)random.NextDouble() * 2f - 1f;
    }

    private float RandomRange(float min, float max)
    {
        return Mathf.Lerp(min, max, (float)random.NextDouble());
    }


    // =====================================================================
    // Validation
    // =====================================================================

    private void OnValidate()
    {
        maxSpeed = Mathf.Max(minSpeed, maxSpeed);
        perceptionRadius = Mathf.Max(separationRadius, perceptionRadius);
        roamMaxInterval = Mathf.Max(roamMinInterval, roamMaxInterval);
    }


#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || positions == null)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(schoolCenter, 0.25f);

        if (roamEnabled)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(roamPoint, roamArrivalRadius);
        }
    }
#endif
}
