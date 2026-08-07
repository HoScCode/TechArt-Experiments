using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// M5 des Ranken-Systems: das Houdini-"Carve + Sweep"-Äquivalent.
/// Glättet die Pfade aus M3/M4 (Chaikin-Eckenschnitt -> Resampling auf
/// gleichmäßige Schrittweite -> Normal-Offset, optional Rückprojektion auf die
/// Oberfläche per Raycast), rendert sie als LineRenderer nach dem
/// Lightning-Aura-Rezept (widthCurve, HDR-Emission für Bloom) und spielt das
/// Wachstum ab: Dauer = Länge / Growth Speed, gestaffelte Starts.
/// Dicke kommt aus dem Kanten-Nutzungszähler: Stamm dick, Spitzen dünn.
/// [ExecuteAlways]: im Edit Mode über den Preview-Time-Slider scrubben.
/// </summary>
[ExecuteAlways]
public class VineGrower : MonoBehaviour
{
    private class Vine
    {
        public LineRenderer LineRenderer;
        public Vector3[] Points;
        public float[] CumulativeLength;
        public float TotalLength;
        public float StartDelay;
        public float Duration;
        public int LastRevealCount = -1;
        public Vector3 LastTip;
    }

    [Header("References")]
    [Tooltip("Der Pathfinder aus M3/M4. Leer = auf diesem GameObject suchen.")]
    [SerializeField] private VinePathfinder pathfinder;

    [Tooltip("Parent für die erzeugten Linien-Objekte. Leer = dieses GameObject.")]
    [SerializeField] private Transform lineParent;

    [Tooltip("Material für die Ranken. Leer = Sprites/Default-Fallback (wie bei der Lightning Aura).\nFür Bloom-Glow: HDR + Bloom auf Kamera/URP-Asset aktivieren.")]
    [SerializeField] private Material lineMaterial;

    [Header("Glättung")]
    [Tooltip("Chaikin-Eckenschnitt: jede Iteration verrundet alle Knicke weiter.\n0 = rohe M4-Pfade (A/B-Vergleich), 2 = Empfehlung, mehr bringt kaum noch was.")]
    [Range(0, 4)]
    [SerializeField] private int smoothIterations = 2;

    [Tooltip("Haarnadel-Entferner: löscht iterativ Punkte, an denen die Linie schärfer als ~115° umkehrt\n(Zickzack-Artefakt der Projektion an Spalten und Innenecken). Legitime 90°-Ecken bleiben erhalten.\n0 = aus.")]
    [Range(0, 8)]
    [SerializeField] private int despikePasses = 4;

    [Tooltip("Schrittweite fürs Resampling automatisch aus der Graph-Kantenlänge ableiten\n(Max-Kantenlänge x Faktor).")]
    [SerializeField] private bool autoResampleStep = true;

    [Range(0.1f, 1f)]
    [SerializeField] private float resampleStepFactor = 0.3f;

    [Tooltip("Feste Schrittweite in Welteinheiten (nur wenn Auto aus ist).")]
    [Min(0.005f)]
    [SerializeField] private float manualResampleStep = 0.08f;

    [Tooltip("Abstand der Linie zur Oberfläche entlang der Normalen (gegen Z-Fighting,\nkaschiert außerdem das minimale Eckenschneiden der Glättung).")]
    [Range(0f, 0.2f)]
    [SerializeField] private float surfaceOffset = 0.03f;

    [Tooltip("Geglättete Punkte per Raycast zurück auf die Oberfläche ziehen.\nBei komplexeren Meshes praktisch Pflicht - sonst tauchen die Linien am Eckenschnitt der Glättung ins Mesh ein.\nBRAUCHT einen Collider auf dem Ziel-Mesh (Cube: BoxCollider ab Werk; Fels/Compound: MeshCollider hinzufügen).")]
    [SerializeField] private bool reprojectToSurface = true;

    [Tooltip("Layer, auf denen die Rückprojektion nach der Oberfläche sucht.")]
    [SerializeField] private LayerMask projectionMask = ~0;

    [Tooltip("MINDEST-Startdistanz des Rückprojektions-Raycasts.\nWird automatisch auf 2x Graph-Kantenlänge angehoben, wenn das größer ist -\nversunkene Punkte brauchen einen Ray-Start AUSSERHALB des Meshes (Raycasts treffen keine Backfaces).")]
    [Range(0.05f, 2f)]
    [SerializeField] private float projectionCastDistance = 0.5f;

    [Header("Dicke (Sweep)")]
    [Tooltip("Grundbreite der Ranke in Welteinheiten (an der Wurzel, ohne Usage-Boost).")]
    [Range(0.005f, 0.3f)]
    [SerializeField] private float baseWidth = 0.05f;

    [Tooltip("Breite der Spitze relativ zur Basis. 0.3 = Spitze läuft auf 30 % aus.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float tipWidthScale = 0.35f;

    [Tooltip("Dicken-Boost für geteilte Kanten: die meistgeteilte Kante (der Stamm)\nwird um diesen Faktor breiter. 0 = aus, 0.6 = Stamm 60 % dicker.")]
    [Range(0f, 2f)]
    [SerializeField] private float usageWidthBoost = 0.6f;

    [Header("Farbe / Glow")]
    [Tooltip("Farbe an der Wurzel.")]
    [SerializeField] private Color rootColor = new Color(0.25f, 0.7f, 0.3f, 1f);

    [Tooltip("Farbe an der Spitze.")]
    [SerializeField] private Color tipColor = new Color(0.6f, 1f, 0.5f, 1f);

    [Tooltip("Farbe in den HDR-Bereich schieben, damit URP Bloom die Ranke glühen lässt\n(gleiches Rezept wie bei der Lightning Aura).")]
    [SerializeField] private bool useEmission = true;

    [Range(1f, 16f)]
    [SerializeField] private float emissionIntensity = 4f;

    [Header("Wachstum (Carve)")]
    [Tooltip("Wachstumsgeschwindigkeit in Welteinheiten pro Sekunde.\nAlle Ranken wachsen gleich SCHNELL, nicht gleich lang (Dauer = Länge / Speed).")]
    [Min(0.05f)]
    [SerializeField] private float growthSpeed = 1.5f;

    [Tooltip("Zeitversatz zwischen den Starts der einzelnen Ranken (Sekunden).")]
    [Min(0f)]
    [SerializeField] private float startStagger = 0.25f;

    [Tooltip("Im Play-Modus automatisch mit dem Wachstum starten.")]
    [SerializeField] private bool playOnStart = true;

    [Tooltip("Wenn sich die Pfade ändern (z.B. neuer Noise-Seed am Pathfinder),\nwächst alles von vorn - DER Regrow-Loop für den Clip.")]
    [SerializeField] private bool restartOnRebuild = true;

    [Header("Edit-Mode Preview")]
    [Tooltip("Wachstum im Edit Mode über den Preview-Time-Slider scrubben (kein Play nötig).")]
    [SerializeField] private bool previewInEditMode = true;

    [Tooltip("0 = nichts gewachsen, 1 = komplette Sequenz fertig. Zum Tunen auf 1 lassen.")]
    [Range(0f, 1f)]
    [SerializeField] private float previewTime = 1f;

    [Header("Line Renderer")]
    [Tooltip("Rundet die Knicke ZWISCHEN den Segmenten optisch ab - wichtiger Teil des Anti-Ecken-Pakets.")]
    [Range(0, 8)]
    [SerializeField] private int cornerVertices = 4;

    [Range(0, 8)]
    [SerializeField] private int capVertices = 4;

    // --- Laufzeit-Daten ---
    private readonly List<Vine> vines = new List<Vine>();
    private readonly List<GameObject> lineObjects = new List<GameObject>();
    private Material fallbackMaterial;
    private float totalSequenceDuration;
    private float clock;
    private bool playing;
    private bool rebuildRequested = true;
    private int builtForPathsVersion = -1;
    private int conformProjectedCount;
    private int conformMissedCount;
    private bool warnedNoProjectionHits;

    private const string LineNamePrefix = "Vine Line";

    // ------------------------------------------------------------------
    // Public API - hier dockt M6 (Player-Reaktion) an.
    // ------------------------------------------------------------------

    /// <summary>Wachstum von vorn abspielen (Play-Modus).</summary>
    public void Replay()
    {
        clock = 0f;
        playing = true;
    }

    /// <summary>Wachstum sofort auf "fertig" setzen.</summary>
    public void FinishInstantly()
    {
        clock = totalSequenceDuration;
        playing = false;
        ApplyGrowth(clock);
    }

    public bool IsFinished => clock >= totalSequenceDuration;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void OnEnable()
    {
        // Streuner aus früheren Sessions entfernen (Edit-Mode-Objekte sind
        // DontSave, können aber nach Domain-Reloads übrig bleiben).
        DestroyStrayLineObjects();

        rebuildRequested = true;
        clock = 0f;
        playing = Application.isPlaying && playOnStart;
    }

    private void OnDisable()
    {
        CleanupLineObjects();
        vines.Clear();
        builtForPathsVersion = -1;
    }

    private void OnValidate()
    {
        // Kein Objekt-Bau in OnValidate (Unity mag das nicht) - nur vormerken,
        // Update erledigt den Rest beim nächsten Editor-Tick.
        rebuildRequested = true;
    }

    private void Update()
    {
        EnsureUpToDate();

        if (vines.Count == 0)
            return;

        float time;

        if (Application.isPlaying)
        {
            if (playing)
                clock += Time.deltaTime;

            time = clock;
        }
        else
        {
            time = previewInEditMode
                ? previewTime * totalSequenceDuration
                : totalSequenceDuration;
        }

        ApplyGrowth(time);
    }

    // ------------------------------------------------------------------
    // Kette Sampler -> Graph -> Pfade -> Linien konsistent halten
    // ------------------------------------------------------------------

    private void EnsureUpToDate()
    {
        VinePathfinder pf = ResolvePathfinder();
        if (pf == null)
            return;

        pf.EnsureUpToDate();

        if (!rebuildRequested && builtForPathsVersion == pf.PathsVersion)
            return;

        bool pathsChanged = builtForPathsVersion != pf.PathsVersion;

        RebuildVines(pf);
        rebuildRequested = false;

        if (Application.isPlaying && pathsChanged && restartOnRebuild)
            Replay();
    }

    // ------------------------------------------------------------------
    // Build: Glätten + Renderer aufsetzen (das Sweep-Äquivalent)
    // ------------------------------------------------------------------

    private void RebuildVines(VinePathfinder pf)
    {
        builtForPathsVersion = pf.PathsVersion;
        vines.Clear();
        totalSequenceDuration = 0f;
        conformProjectedCount = 0;
        conformMissedCount = 0;

        VineGraphBuilder graph = ResolveGraph();
        VineSurfaceSampler samplerRef = graph != null ? graph.Sampler : null;

        int pathCount = (pf.HasPaths && samplerRef != null && samplerRef.HasSamples)
            ? pf.PathCount
            : 0;

        EnsureLineObjectCount(pathCount);

        if (pathCount == 0)
            return;

        float step = autoResampleStep && graph != null && graph.HasGraph
            ? graph.UsedMaxEdgeLength * resampleStepFactor
            : manualResampleStep;

        // Referenz-Kantenlänge für die Rückprojektion: tiefer als etwa eine
        // Graph-Kante sinkt der Chaikin-Eckenschnitt nicht - daran skalieren
        // Ray-Startdistanz und Snap-Limit.
        float edgeLengthReference = graph != null && graph.HasGraph
            ? graph.UsedMaxEdgeLength
            : step * 3f;

        int maxUsage = Mathf.Max(1, pf.MaxEdgeUsage);

        // Wiederverwendete Arbeitslisten für die Glättungs-Pipeline
        List<Vector3> pos = new List<Vector3>(128);
        List<Vector3> nrm = new List<Vector3>(128);
        List<float> use = new List<float>(128);

        for (int p = 0; p < pathCount; p++)
        {
            Vine vine = new Vine();
            vine.LineRenderer = lineObjects[p].GetComponent<LineRenderer>();
            lineObjects[p].SetActive(true);

            if (!BuildSmoothedPolyline(pf, samplerRef, p, pos, nrm, use, step, edgeLengthReference))
            {
                vine.Points = null;
                vine.LineRenderer.positionCount = 0;
                vines.Add(vine);
                continue;
            }

            // Punkte + Bogenlängen einfrieren (Basis für das Carve-Wachstum)
            vine.Points = pos.ToArray();

            int n = vine.Points.Length;
            vine.CumulativeLength = new float[n];
            for (int i = 1; i < n; i++)
            {
                vine.CumulativeLength[i] = vine.CumulativeLength[i - 1]
                    + Vector3.Distance(vine.Points[i - 1], vine.Points[i]);
            }

            vine.TotalLength = vine.CumulativeLength[n - 1];
            vine.Duration = Mathf.Max(0.01f, vine.TotalLength / growthSpeed);
            vine.StartDelay = p * startStagger;

            float end = vine.StartDelay + vine.Duration;
            if (end > totalSequenceDuration)
                totalSequenceDuration = end;

            ApplyStyle(vine.LineRenderer, BuildWidthCurve(use, maxUsage));
            vines.Add(vine);
        }

        // Die eine Warnung, die den "es passiert einfach nichts"-Fall aufdeckt:
        // Projektion gewollt, aber kein einziger Raycast trifft.
        if (reprojectToSurface && conformProjectedCount == 0 && conformMissedCount > 0)
        {
            if (!warnedNoProjectionHits)
            {
                Debug.LogWarning("[VineGrower] Conform/Projektion: 0 Treffer bei " + conformMissedCount +
                    " Versuchen - die Ranken bleiben auf den rohen Pfaden! Checkliste: " +
                    "1) Collider auf JEDEM Teil des Ziel-Meshes? 2) MeshCollider: 'Convex' AUS? " +
                    "3) Projection Mask enthält den Layer des Meshes? " +
                    "Kontextmenü 'Debug: Collider-Check' zeigt, was die Rays treffen.", this);
                warnedNoProjectionHits = true;
            }
        }
        else if (conformProjectedCount > 0)
        {
            warnedNoProjectionHits = false;
        }
    }

    // Rohpfad einsammeln und durch die Pipeline schicken:
    // Resample -> Conform (Ray) -> Chaikin -> Conform + Offset.
    // Normale und Usage laufen als Begleitdaten identisch mit.
    private bool BuildSmoothedPolyline(VinePathfinder pf, VineSurfaceSampler samplerRef,
        int pathIndex, List<Vector3> pos, List<Vector3> nrm, List<float> use,
        float step, float edgeLengthReference)
    {
        pos.Clear();
        nrm.Clear();
        use.Clear();

        IReadOnlyList<int> nodes = pf.GetPathNodes(pathIndex);
        IReadOnlyList<int> edgesOfPath = pf.GetPathEdges(pathIndex);

        if (nodes.Count < 2 || edgesOfPath.Count == 0)
            return false;

        for (int i = 0; i < nodes.Count; i++)
        {
            pos.Add(samplerRef.GetWorldPoint(nodes[i]));
            nrm.Add(samplerRef.GetWorldNormal(nodes[i]));

            // Usage am Knoten = Usage der wurzelseitigen Kante -> die Dicke an
            // einer Verzweigung entspricht dem ankommenden Stamm.
            int edgeIndex = i == 0 ? edgesOfPath[0] : edgesOfPath[i - 1];
            use.Add(pf.GetEdgeUsage(edgeIndex));
        }

        // NEUE Reihenfolge (das Houdini-"Ray SOP"-Prinzip): erst dicht
        // resampeln, dann JEDEN Punkt nach außen drücken und per Raycast zurück
        // auf die Oberfläche projizieren. Damit landen auch Punkte MITTEN AUF
        // einer Luft-Abkürzung in der Ecke - der Pfad folgt der echten
        // Oberfläche, egal wie die Graph-Kante geflogen ist. Danach glätten und
        // ein zweites Mal konformieren, weil Chaikin wieder kleine Ecken schneidet.

        Resample(pos, nrm, use, step);

        ConformToSurface(pos, nrm, edgeLengthReference, applyOffset: false);
        RemoveSpikes(pos, nrm, use, step * 0.25f, despikePasses);

        for (int iter = 0; iter < smoothIterations; iter++)
            ChaikinStep(pos, nrm, use);

        ConformToSurface(pos, nrm, edgeLengthReference, applyOffset: true);
        RemoveSpikes(pos, nrm, use, step * 0.25f, despikePasses);

        return pos.Count >= 2;
    }

    // Das "Ray SOP"-Äquivalent: jeden Punkt entlang seiner (mitgeführten)
    // Normalen nach außen schieben und per Raycast zurück auf die Oberfläche
    // holen. Trifft der Ray nichts, bleibt der Punkt unverändert (Fallback =
    // altes Verhalten) - die Conform-Zähler machen das im Stats-Log sichtbar.
    private void ConformToSurface(List<Vector3> pos, List<Vector3> nrm,
        float edgeLengthReference, bool applyOffset)
    {
        // Ray-Start muss AUSSERHALB des Meshes liegen (Raycasts treffen keine
        // Backfaces), deshalb skaliert die Distanz mit der Graph-Kantenlänge.
        float castDistance = Mathf.Max(projectionCastDistance, edgeLengthReference * 2f);
        float snapLimit = edgeLengthReference * 2f;

        // Kontinuitäts-Leine: ein Snap darf den Punkt nicht quer über eine
        // Spalte auf die "andere Wand" reißen (sonst faltet sich die Linie zu
        // Zickzack-Haarnadeln). Referenz ist der zuletzt ausgegebene Punkt;
        // verweigerte Punkte bleiben unprojiziert und werden von Despike +
        // Chaikin als kurze, gerade Brücke überspannt.
        float continuityLimit = edgeLengthReference;
        bool hasPrevious = false;
        Vector3 previousOut = Vector3.zero;

        for (int i = 0; i < pos.Count; i++)
        {
            Vector3 n = nrm[i].sqrMagnitude > 1e-8f ? nrm[i].normalized : Vector3.up;
            Vector3 point = pos[i];

            if (reprojectToSurface)
            {
                Vector3 origin = point + n * castDistance;

                if (Physics.Raycast(origin, -n, out RaycastHit hit,
                        castDistance * 2f, projectionMask))
                {
                    // Teleport-Schutz + Leine: nah an der Linie UND nah am
                    // vorherigen Punkt, sonst kein Snap.
                    bool nearEnough = (hit.point - point).sqrMagnitude <= snapLimit * snapLimit;
                    bool continuous = !hasPrevious ||
                        (hit.point - previousOut).sqrMagnitude <= continuityLimit * continuityLimit;

                    if (nearEnough && continuous)
                    {
                        point = hit.point;
                        n = hit.normal;
                        conformProjectedCount++;
                    }
                    else
                    {
                        conformMissedCount++;
                    }
                }
                else
                {
                    conformMissedCount++;
                }
            }

            nrm[i] = n;
            pos[i] = applyOffset ? point + n * surfaceOffset : point;

            previousOut = pos[i];
            hasPrevious = true;
        }
    }

    // Haarnadel-Entferner: löscht iterativ Punkte, an denen die Linie fast
    // umkehrt (typisches Artefakt, wenn die Projektion benachbarte Punkte
    // abwechselnd auf zwei Wände einer Spalte zieht), und legt praktisch
    // deckungsgleiche Punkte zusammen. Endpunkte bleiben unangetastet.
    private static void RemoveSpikes(List<Vector3> pos, List<Vector3> nrm, List<float> use,
        float minSegmentLength, int passes)
    {
        if (passes <= 0 || pos.Count < 3)
            return;

        // Dot < -0.4 entspricht einem Knick schärfer als ~115 Grad -> Haarnadel.
        // Eine legitime 90°-Innenecke (Dot = 0) bleibt damit klar erhalten.
        const float reversalDot = -0.4f;

        float minSqr = minSegmentLength * minSegmentLength;

        for (int pass = 0; pass < passes; pass++)
        {
            bool removedAny = false;

            for (int i = pos.Count - 2; i >= 1; i--)
            {
                Vector3 inDir = pos[i] - pos[i - 1];
                Vector3 outDir = pos[i + 1] - pos[i];

                if (inDir.sqrMagnitude < minSqr || outDir.sqrMagnitude < minSqr)
                {
                    pos.RemoveAt(i); nrm.RemoveAt(i); use.RemoveAt(i);
                    removedAny = true;
                    continue;
                }

                if (Vector3.Dot(inDir.normalized, outDir.normalized) < reversalDot)
                {
                    pos.RemoveAt(i); nrm.RemoveAt(i); use.RemoveAt(i);
                    removedAny = true;
                }
            }

            if (!removedAny)
                break;
        }
    }

    // Chaikin-Eckenschnitt: Endpunkte bleiben fixiert, jede Innenecke wird durch
    // zwei Punkte bei 25 % / 75 % ersetzt. Bewusst begrenzt kontraktiv - die
    // Abweichung pro Ecke bleibt klein, deshalb bleibt die Linie nah an der Fläche.
    private static void ChaikinStep(List<Vector3> pos, List<Vector3> nrm, List<float> use)
    {
        int count = pos.Count;
        if (count < 3)
            return;

        List<Vector3> newPos = new List<Vector3>(count * 2);
        List<Vector3> newNrm = new List<Vector3>(count * 2);
        List<float> newUse = new List<float>(count * 2);

        newPos.Add(pos[0]);
        newNrm.Add(nrm[0]);
        newUse.Add(use[0]);

        for (int i = 0; i < count - 1; i++)
        {
            newPos.Add(Vector3.Lerp(pos[i], pos[i + 1], 0.25f));
            newNrm.Add(Vector3.Lerp(nrm[i], nrm[i + 1], 0.25f));
            newUse.Add(Mathf.Lerp(use[i], use[i + 1], 0.25f));

            newPos.Add(Vector3.Lerp(pos[i], pos[i + 1], 0.75f));
            newNrm.Add(Vector3.Lerp(nrm[i], nrm[i + 1], 0.75f));
            newUse.Add(Mathf.Lerp(use[i], use[i + 1], 0.75f));
        }

        newPos.Add(pos[count - 1]);
        newNrm.Add(nrm[count - 1]);
        newUse.Add(use[count - 1]);

        pos.Clear(); pos.AddRange(newPos);
        nrm.Clear(); nrm.AddRange(newNrm);
        use.Clear(); use.AddRange(newUse);
    }

    // Lineares Resampling auf gleichmäßige Schrittweite. Nach 1-2 Chaikin-
    // Iterationen ist die Polyline dicht genug - Catmull-Rom wäre Overkill
    // und neigt an scharfen Ecken zum Überschwingen.
    private static void Resample(List<Vector3> pos, List<Vector3> nrm, List<float> use, float step)
    {
        int count = pos.Count;
        if (count < 2 || step <= 0.0005f)
            return;

        float[] cum = new float[count];
        for (int i = 1; i < count; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(pos[i - 1], pos[i]);

        float total = cum[count - 1];
        if (total <= step)
            return;

        List<Vector3> newPos = new List<Vector3>(Mathf.CeilToInt(total / step) + 2);
        List<Vector3> newNrm = new List<Vector3>(newPos.Capacity);
        List<float> newUse = new List<float>(newPos.Capacity);

        int seg = 0;
        for (float d = 0f; d < total; d += step)
        {
            while (seg < count - 2 && cum[seg + 1] < d)
                seg++;

            float t = Mathf.InverseLerp(cum[seg], cum[seg + 1], d);

            newPos.Add(Vector3.Lerp(pos[seg], pos[seg + 1], t));
            newNrm.Add(Vector3.Lerp(nrm[seg], nrm[seg + 1], t));
            newUse.Add(Mathf.Lerp(use[seg], use[seg + 1], t));
        }

        newPos.Add(pos[count - 1]);
        newNrm.Add(nrm[count - 1]);
        newUse.Add(use[count - 1]);

        pos.Clear(); pos.AddRange(newPos);
        nrm.Clear(); nrm.AddRange(newNrm);
        use.Clear(); use.AddRange(newUse);
    }

    // Breitenprofil der Ranke: Taper (Basis -> Spitze) x Usage-Boost (Stamm dicker).
    // 24 Stützstellen glätten die Usage-Stufen an Verzweigungen gleich mit.
    // Hinweis: Die widthCurve wird von Unity über die AKTUELL sichtbare Linie
    // normalisiert - beim Wachsen ist die Spitze dadurch automatisch dünn. Gewollt.
    private AnimationCurve BuildWidthCurve(List<float> use, int maxUsage)
    {
        const int stations = 24;
        Keyframe[] keys = new Keyframe[stations];

        for (int k = 0; k < stations; k++)
        {
            float t = k / (float)(stations - 1);

            float fIndex = t * (use.Count - 1);
            int i0 = Mathf.FloorToInt(fIndex);
            int i1 = Mathf.Min(i0 + 1, use.Count - 1);
            float usage = Mathf.Lerp(use[i0], use[i1], fIndex - i0);

            float usageNorm = maxUsage > 1
                ? Mathf.Clamp01((usage - 1f) / (maxUsage - 1f))
                : 0f;

            float width01 = Mathf.Lerp(1f, tipWidthScale, t) * (1f + usageWidthBoost * usageNorm);

            keys[k] = new Keyframe(t, width01);
        }

        return new AnimationCurve(keys);
    }

    private void ApplyStyle(LineRenderer lr, AnimationCurve widthCurve)
    {
        lr.widthCurve = widthCurve;
        lr.widthMultiplier = baseWidth;

        lr.numCornerVertices = cornerVertices;
        lr.numCapVertices = capVertices;

        Color c0 = rootColor;
        Color c1 = tipColor;

        // HDR-Trick aus der Lightning Aura: RGB über 1 pushen -> URP Bloom glüht
        if (useEmission)
        {
            c0.r *= emissionIntensity; c0.g *= emissionIntensity; c0.b *= emissionIntensity;
            c1.r *= emissionIntensity; c1.g *= emissionIntensity; c1.b *= emissionIntensity;
        }

        lr.startColor = c0;
        lr.endColor = c1;

        // sharedMaterial statt material: erzeugt im Edit Mode keine Instanz-Leaks
        lr.sharedMaterial = lineMaterial != null ? lineMaterial : GetFallbackMaterial();
    }

    // ------------------------------------------------------------------
    // Carve: das Wachstum
    // ------------------------------------------------------------------

    private void ApplyGrowth(float time)
    {
        for (int i = 0; i < vines.Count; i++)
        {
            Vine vine = vines[i];

            if (vine.Points == null || vine.LineRenderer == null)
                continue;

            float growth = vine.Duration > 0f
                ? Mathf.Clamp01((time - vine.StartDelay) / vine.Duration)
                : 1f;

            RevealVine(vine, growth);
        }
    }

    // Progressives Aufdecken der Polyline: alle voll enthaltenen Punkte plus
    // eine exakt interpolierte Spitze. Schreibt nur bei Änderungen in den Renderer.
    private static void RevealVine(Vine vine, float growth)
    {
        LineRenderer lr = vine.LineRenderer;

        if (growth <= 0f)
        {
            if (lr.positionCount != 0)
                lr.positionCount = 0;

            vine.LastRevealCount = 0;
            return;
        }

        int pointCount = vine.Points.Length;
        int revealCount;
        Vector3 tip;

        if (growth >= 1f)
        {
            revealCount = pointCount;
            tip = vine.Points[pointCount - 1];
        }
        else
        {
            float shown = growth * vine.TotalLength;

            int k = 0;
            while (k + 1 < pointCount && vine.CumulativeLength[k + 1] <= shown)
                k++;

            if (k >= pointCount - 1)
            {
                revealCount = pointCount;
                tip = vine.Points[pointCount - 1];
            }
            else
            {
                float t = Mathf.InverseLerp(
                    vine.CumulativeLength[k],
                    vine.CumulativeLength[k + 1],
                    shown);

                tip = Vector3.Lerp(vine.Points[k], vine.Points[k + 1], t);
                revealCount = k + 2;
            }
        }

        bool countChanged = revealCount != vine.LastRevealCount;
        bool tipChanged = (tip - vine.LastTip).sqrMagnitude > 1e-10f;

        if (!countChanged && !tipChanged)
            return;

        lr.positionCount = revealCount;

        if (countChanged)
        {
            for (int i = 0; i < revealCount - 1; i++)
                lr.SetPosition(i, vine.Points[i]);
        }

        lr.SetPosition(revealCount - 1, tip);

        vine.LastRevealCount = revealCount;
        vine.LastTip = tip;
    }

    // ------------------------------------------------------------------
    // Linien-Objekte verwalten (Edit-Mode-sicher: DontSave + Aufräumen)
    // ------------------------------------------------------------------

    private void EnsureLineObjectCount(int needed)
    {
        Transform parent = lineParent != null ? lineParent : transform;

        while (lineObjects.Count < needed)
        {
            GameObject go = new GameObject(LineNamePrefix + " " + lineObjects.Count);
            go.transform.SetParent(parent, false);
            go.hideFlags = HideFlags.DontSave; // landet nicht in der Szenen-Datei

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;

            lineObjects.Add(go);
        }

        for (int i = 0; i < lineObjects.Count; i++)
        {
            if (lineObjects[i] == null)
                continue;

            bool active = i < needed;
            if (lineObjects[i].activeSelf != active)
                lineObjects[i].SetActive(active);

            if (!active)
                lineObjects[i].GetComponent<LineRenderer>().positionCount = 0;
        }
    }

    private void CleanupLineObjects()
    {
        for (int i = 0; i < lineObjects.Count; i++)
        {
            if (lineObjects[i] == null)
                continue;

            if (Application.isPlaying)
                Destroy(lineObjects[i]);
            else
                DestroyImmediate(lineObjects[i]);
        }

        lineObjects.Clear();
    }

    // Nach Domain-Reloads können DontSave-Objekte in der Hierarchie übrig bleiben,
    // während unsere Liste leer ist - per Namens-Präfix einsammeln und entsorgen.
    private void DestroyStrayLineObjects()
    {
        Transform parent = lineParent != null ? lineParent : transform;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);

            if (!child.name.StartsWith(LineNamePrefix))
                continue;

            if (lineObjects.Contains(child.gameObject))
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private Material GetFallbackMaterial()
    {
        if (fallbackMaterial != null)
            return fallbackMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
            fallbackMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };

        return fallbackMaterial;
    }

    private VinePathfinder ResolvePathfinder()
    {
        return pathfinder != null ? pathfinder : GetComponent<VinePathfinder>();
    }

    private VineGraphBuilder ResolveGraph()
    {
        return GetComponent<VineGraphBuilder>();
    }

    // ------------------------------------------------------------------
    // Debug / Kontextmenü
    // ------------------------------------------------------------------

    [ContextMenu("Debug: Collider-Check (Projektion testen)")]
    private void DebugColliderCheck()
    {
        VineGraphBuilder graph = ResolveGraph();
        VineSurfaceSampler samplerRef = graph != null ? graph.Sampler : null;

        if (samplerRef == null || !samplerRef.HasSamples)
        {
            Debug.LogWarning("[VineGrower] Collider-Check: keine Samples vorhanden - Kette Sampler/Graph prüfen.", this);
            return;
        }

        float castDistance = Mathf.Max(projectionCastDistance,
            (graph != null && graph.HasGraph ? graph.UsedMaxEdgeLength : 0.3f) * 2f);

        int probes = Mathf.Min(8, samplerRef.SampleCount);

        // --- Probe A: entlang der Sample-Normalen (exakt so arbeitet der Conform) ---
        int hitsAlongNormals = 0;
        Bounds probeBounds = new Bounds(samplerRef.GetWorldPoint(0), Vector3.zero);

        for (int i = 0; i < probes; i++)
        {
            int index = (samplerRef.SampleCount / probes) * i;
            Vector3 p = samplerRef.GetWorldPoint(index);
            Vector3 n = samplerRef.GetWorldNormal(index);
            probeBounds.Encapsulate(p);

            Vector3 origin = p + n * castDistance;

            if (Physics.Raycast(origin, -n, out RaycastHit hit, castDistance * 2f, projectionMask))
            {
                hitsAlongNormals++;
                Debug.Log($"[VineGrower] Probe {i} (Normale): Treffer auf '{hit.collider.name}' " +
                          $"(Abstand zum Samplepunkt: {Vector3.Distance(hit.point, p):0.000}).", this);
            }
        }

        if (hitsAlongNormals > 0)
        {
            Debug.Log($"[VineGrower] Collider-Check: {hitsAlongNormals}/{probes} Normalen-Proben treffen - Setup ok. " +
                "Wenn der Conform trotzdem 0 projiziert meldet: Rebuild anstoßen.", this);
            return;
        }

        // --- Probe A2: mit UMGEDREHTER Normale (Test auf geflippte Mesh-Normalen) ---
        int hitsFlipped = 0;

        for (int i = 0; i < probes; i++)
        {
            int index = (samplerRef.SampleCount / probes) * i;
            Vector3 p = samplerRef.GetWorldPoint(index);
            Vector3 n = samplerRef.GetWorldNormal(index);

            Vector3 origin = p - n * castDistance;

            if (Physics.Raycast(origin, n, out RaycastHit hit, castDistance * 2f, projectionMask) &&
                Vector3.Distance(hit.point, p) < castDistance)
            {
                hitsFlipped++;
            }
        }

        if (hitsFlipped > 0)
        {
            Debug.LogWarning($"[VineGrower] DIAGNOSE: {hitsFlipped}/{probes} Proben treffen mit UMGEDREHTER Normale -> " +
                "die Normalen des gesampelten Meshes zeigen nach INNEN (bei dedizierten _Collision-Meshes üblich). " +
                "Ein-Klick-Fix: am Vine Surface Sampler 'Flip Normals' anhaken. " +
                "Alternativ das Render-Mesh statt des Collision-Meshes als Target Mesh nehmen.", this);
            return;
        }

        // --- Probe B: senkrecht von oben, MIT Projection Mask ---
        Vector3 top = probeBounds.center + Vector3.up * 100f;
        bool hitWithMask = Physics.Raycast(top, Vector3.down, out RaycastHit hitMasked, 500f, projectionMask);

        // --- Probe C: senkrecht von oben, OHNE Mask (alle Layer) ---
        bool hitAnyLayer = Physics.Raycast(top, Vector3.down, out RaycastHit hitAny, 500f);

        if (hitWithMask)
        {
            Debug.LogWarning("[VineGrower] DIAGNOSE: Collider '" + hitMasked.collider.name + "' ist da und die Mask " +
                "stimmt, aber weder normale noch geflippte Proben treffen -> Samples und Collider liegen vermutlich " +
                "NICHT auf derselben Geometrie (z.B. Collision-Objekt an anderer Position/Skalierung als das " +
                "gesampelte Mesh). Transform des Collider-Objekts mit dem Target Mesh vergleichen.", this);
        }
        else if (hitAnyLayer)
        {
            Debug.LogWarning("[VineGrower] DIAGNOSE: Es gibt einen Collider ('" + hitAny.collider.name + "' auf Layer '" +
                LayerMask.LayerToName(hitAny.collider.gameObject.layer) + "'), aber die Projection Mask lässt ihn " +
                "nicht durch. Entweder das OBJEKT auf den Mask-Layer legen (Layer-Dropdown oben im Inspector) " +
                "oder die Mask erweitern.", this);
        }
        else
        {
            Debug.LogWarning("[VineGrower] DIAGNOSE: Gar kein Collider getroffen. Checkliste: " +
                "1) MeshCollider/BoxCollider auf dem Ziel-Objekt (und jedem Teil)? 2) 'Convex' AUS? " +
                "3) 'Is Trigger' AUS (oder Physics-Setting 'Queries Hit Triggers' aktivieren)?", this);
        }
    }

    [ContextMenu("Replay Growth")]
    private void ContextReplay()
    {
        Replay();
    }

    [ContextMenu("Debug: Sofort fertig wachsen")]
    private void ContextFinish()
    {
        FinishInstantly();
    }

    [ContextMenu("Debug: Rebuild + Stats loggen")]
    private void DebugRebuildWithStats()
    {
        rebuildRequested = true;
        EnsureUpToDate();

        if (vines.Count == 0)
        {
            Debug.LogWarning("[VineGrower] Keine Ranken gebaut - hat der Pathfinder Pfade? Siehe Warnungen oben.", this);
            return;
        }

        int totalVertices = 0;
        float totalLength = 0f;

        for (int i = 0; i < vines.Count; i++)
        {
            if (vines[i].Points == null)
                continue;

            totalVertices += vines[i].Points.Length;
            totalLength += vines[i].TotalLength;
        }

        string conformInfo = reprojectToSurface
            ? $"Conform: {conformProjectedCount} projiziert / {conformMissedCount} daneben"
            : "Conform: AUS ('Reproject To Surface' ist nicht angehakt!)";

        Debug.Log(
            $"[VineGrower] {vines.Count} Ranken, {totalVertices} Punkte nach Glättung, " +
            $"Länge gesamt: {totalLength:0.00} | {conformInfo} | " +
            $"Sequenz-Dauer: {totalSequenceDuration:0.00}s " +
            $"(Speed {growthSpeed}, Stagger {startStagger}s)", this);
    }
}
