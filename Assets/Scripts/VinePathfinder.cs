using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// M3 des Ranken-Systems: das Houdini-"Find Shortest Path"-Äquivalent.
/// Wählt Wurzel(n) unten und Ziele oben auf dem Graphen aus M2, rechnet EINEN
/// Multi-Source-Dijkstra (alle Wurzeln starten mit Distanz 0) und verfolgt die
/// kürzesten Wege zu allen Zielen zurück. Weil sich die Pfade nahe der Wurzel
/// zwangsläufig Kanten teilen, entsteht gratis eine Baumstruktur - der
/// Kanten-Nutzungszähler macht sie als "Stamm" sichtbar (Usage-Heat-Gizmo).
/// Ab M4 gilt die Houdini-Kostenformel: Länge x Noise-Mäander x Kletter-Bias.
/// Cost Noise Amp 0 stellt die zielstrebigen M3-Direktpfade wieder her.
/// Läuft komplett im Edit Mode.
/// </summary>
public class VinePathfinder : MonoBehaviour
{
    private enum PathColorMode
    {
        UsageHeat,  // geteilte Kanten heller -> der Stamm wird sichtbar
        PerPath,    // jede Ranke eine eigene Farbe
        Single      // alles in einer Farbe
    }

    [Header("References")]
    [Tooltip("Der Graph aus M2. Leer = auf diesem GameObject suchen.")]
    [SerializeField] private VineGraphBuilder graphBuilder;

    [Header("Start (Wurzel)")]
    [Tooltip("Optionale Anker-Transforms: pro Anker wird der nächstgelegene Samplepunkt zur Wurzel.\nAnker im Scene View verschieben -> die Wurzel folgt live.\nLeer = automatische Wahl aus dem untersten Y-Perzentil.")]
    [SerializeField] private Transform[] startAnchors;

    [Tooltip("Anzahl Wurzeln bei automatischer Wahl. Für den Pflanzen-Look: 1 (oder wenige).")]
    [Range(1, 8)]
    [SerializeField] private int startCount = 1;

    [Tooltip("Unterster Anteil der Punkte (nach Welthöhe), aus dem Wurzeln gewählt werden. 0.1 = unterste 10 %.")]
    [Range(0.02f, 0.5f)]
    [SerializeField] private float startPercentile = 0.1f;

    [Header("Ziele (Krone)")]
    [Tooltip("Wie viele Ranken-Ziele oben angesteuert werden.")]
    [Range(1, 32)]
    [SerializeField] private int targetCount = 8;

    [Tooltip("Oberster Anteil der Punkte (nach Welthöhe), aus dem Ziele gewählt werden. 0.15 = oberste 15 %.")]
    [Range(0.02f, 0.5f)]
    [SerializeField] private float targetPercentile = 0.15f;

    [Tooltip("Mindestabstand zwischen Zielen automatisch aus der Graph-Kantenlänge ableiten\n(Max-Kantenlänge x Faktor). Sorgt dafür, dass sich die Krone auffächert statt zu bündeln.")]
    [SerializeField] private bool autoTargetSpacing = true;

    [Range(1f, 8f)]
    [SerializeField] private float targetSpacingFactor = 2.5f;

    [Tooltip("Fester Mindestabstand in Welteinheiten (nur wenn Auto aus ist).")]
    [Min(0f)]
    [SerializeField] private float manualTargetSpacing = 0.5f;

    [Header("Mäander / Kosten (M4)")]
    [Tooltip("DER Mäander-Regler: verrauschter Kostenaufschlag pro Kante.\n0 = zielstrebige M3-Direktpfade (perfekter A/B-Vergleich), 1-4 = Sweetspot, >5 = chaotische Haken.")]
    [Range(0f, 6f)]
    [SerializeField] private float costNoiseAmp = 2.5f;

    [Tooltip("Größe der 'teuren Zonen', die umwandert werden.\nNiedrig = große, ruhige Bögen. Hoch = feine, nervöse Schlenker.\nStartwert für ein ~5 Einheiten großes Mesh: 1-2.")]
    [Min(0.01f)]
    [SerializeField] private float costNoiseScale = 1.5f;

    [Tooltip("Eigener Seed nur für das Mäander-Muster: gleiche Punkte, andere Wege.\nDas ist der spätere Regrow-Trick - per Kontextmenü neu würfelbar.")]
    [SerializeField] private int noiseSeed = 999;

    [Tooltip("Kletterpflanzen-Bias: abwärts ist teurer als aufwärts (quer = halber Aufschlag).\n0 = aus, 0.2-0.5 = sinnvoll, zu viel = stur senkrechte Wege.")]
    [Range(0f, 2f)]
    [SerializeField] private float upwardBias = 0.25f;

    [Header("Recompute")]
    [Tooltip("Bei Inspector-Änderungen automatisch neu rechnen (Live-Tuning).")]
    [SerializeField] private bool recomputeOnValidate = true;

    [Header("Gizmos")]
    [SerializeField] private bool showPaths = true;

    [Tooltip("Usage Heat = geteilte Kanten leuchten heller (Stamm sichtbar).\nPer Path = jede Ranke eine eigene Farbe. Single = eine Farbe.")]
    [SerializeField] private PathColorMode colorMode = PathColorMode.UsageHeat;

    [Tooltip("Farbe für Kanten, die nur EIN Pfad nutzt (die Spitzen).")]
    [SerializeField] private Color pathBaseColor = new Color(0.35f, 0.85f, 0.4f, 0.9f);

    [Tooltip("Farbe für die meistgeteilte Kante (der Stamm).")]
    [SerializeField] private Color pathHotColor = Color.white;

    [Tooltip("Einheitliche Pfadfarbe im Modus 'Single'.")]
    [SerializeField] private Color singlePathColor = new Color(0.4f, 1f, 0.45f, 0.9f);

    [SerializeField] private bool showEndpoints = true;

    [Range(0.01f, 0.2f)]
    [SerializeField] private float endpointSize = 0.06f;

    [SerializeField] private Color startMarkerColor = new Color(0.8f, 0.5f, 0.2f, 1f);
    [SerializeField] private Color targetMarkerColor = new Color(0.4f, 1f, 0.45f, 1f);

    [Tooltip("Gizmos nur zeichnen, solange das Objekt selektiert ist (weniger Clutter).")]
    [SerializeField] private bool showGizmoOnlyWhenSelected = false;

    // --- Ergebnis-Daten (Snapshot vom Compute-Zeitpunkt) ---
    private readonly List<int[]> paths = new List<int[]>();       // Knotenfolgen Wurzel -> Ziel
    private readonly List<int[]> pathEdges = new List<int[]>();   // Kanten-Indizes; Eintrag i verbindet Knoten i und i+1
    private readonly List<float> pathLengths = new List<float>(); // geometrische Länge in Welteinheiten
    private int[] edgeUsage;                                      // pro Graph-Kante: von wie vielen Pfaden genutzt
    private int maxEdgeUsage;
    private int[] startNodes = System.Array.Empty<int>();
    private int[] targetNodes = System.Array.Empty<int>();
    private int unreachableTargets;
    private float lastComputeMs;
    private int builtForGraphVersion = -1;
    private Vector3[] cachedAnchorPositions;
    private Vector3[] nodePositions; // Welt-Snapshot des letzten Computes (für die Kostenformel)
    private Vector3 noiseOffset;     // aus Noise Seed abgeleitet

    // ------------------------------------------------------------------
    // Public API - hier docken M4 (Noise/Dicke) und M5 (Grower) an.
    // ------------------------------------------------------------------

    public bool HasPaths => paths.Count > 0;

    public int PathCount => paths.Count;

    /// <summary>Zählt bei jedem RecomputeNow() hoch - M5 (Grower) prüft darüber,
    /// ob seine geglätteten Linien noch zu den aktuellen Pfaden passen.</summary>
    public int PathsVersion { get; private set; }

    /// <summary>Knotenfolge eines Pfads, von der Wurzel zum Ziel.</summary>
    public IReadOnlyList<int> GetPathNodes(int pathIndex) => paths[pathIndex];

    /// <summary>Kanten-Indizes eines Pfads; Eintrag i verbindet Knoten i und i+1.
    /// Zusammen mit GetEdgeUsage die Basis für die Dickenlogik in M4/M5.</summary>
    public IReadOnlyList<int> GetPathEdges(int pathIndex) => pathEdges[pathIndex];

    /// <summary>Geometrische Länge eines Pfads in Welteinheiten - Basis für die
    /// Wachstumsdauer in M5 (Dauer = Länge / growthSpeed).</summary>
    public float GetPathLength(int pathIndex) => pathLengths[pathIndex];

    /// <summary>Von wie vielen Pfaden eine Graph-Kante genutzt wird:
    /// die geschenkte Dickenlogik (Stamm dick, Spitzen dünn).</summary>
    public int GetEdgeUsage(int edgeIndex) => edgeUsage != null ? edgeUsage[edgeIndex] : 0;

    public int MaxEdgeUsage => maxEdgeUsage;

    public IReadOnlyList<int> StartNodes => startNodes;
    public IReadOnlyList<int> TargetNodes => targetNodes;

    /// <summary>Hängt die aktuellen Weltpositionen eines Pfads an die Liste an
    /// (live vom Sampler, folgt also dem Objekt-Transform).</summary>
    public void GetPathWorldPositions(int pathIndex, List<Vector3> results)
    {
        VineSurfaceSampler s = ResolveSampler();
        if (s == null)
            return;

        int[] nodes = paths[pathIndex];
        for (int i = 0; i < nodes.Length; i++)
            results.Add(s.GetWorldPoint(nodes[i]));
    }

    /// <summary>Rechnet nach, falls der Graph seit dem letzten Compute neu gebaut
    /// wurde oder ein Start-Anker bewegt wurde. Hält die ganze Kette
    /// Sampler -> Graph -> Pfade konsistent.</summary>
    public void EnsureUpToDate()
    {
        VineGraphBuilder g = ResolveGraph();
        if (g == null)
            return;

        g.EnsureUpToDate();

        if (builtForGraphVersion != g.GraphVersion || AnchorsMoved())
            RecomputeNow();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        ValidateSetup();
        RecomputeNow();
    }

    private void OnValidate()
    {
        // Damit Start-/Ziel-Regler im Inspector "live" reagieren
        if (recomputeOnValidate)
            RecomputeNow();
    }

    // ------------------------------------------------------------------
    // Compute-Kern: das Houdini-"Find Shortest Path"-Äquivalent
    // ------------------------------------------------------------------

    [ContextMenu("Recompute Now")]
    public void RecomputeNow()
    {
        ClearPaths();
        CacheAnchorPositions();

        // Version hochzählen, BEVOR irgendetwas weiter passiert (gleiche Mechanik
        // wie SampleVersion/GraphVersion): auch ein fehlgeschlagenes Compute hat
        // die alten Pfade gelöscht - M5 bekommt das mit und verwirft seine Linien.
        PathsVersion++;

        VineGraphBuilder g = ResolveGraph();
        if (g == null)
            return;

        g.EnsureUpToDate();

        // Version auch bei Fehlschlag merken (gleiche Mechanik wie in M1/M2),
        // damit nicht jeden Gizmo-Frame erneut gerechnet wird.
        builtForGraphVersion = g.GraphVersion;

        if (!g.HasGraph)
            return;

        VineSurfaceSampler s = g.Sampler;
        if (s == null || !s.HasSamples || s.SampleCount != g.NodeCount)
            return;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        int n = g.NodeCount;

        // Snapshot der Weltpositionen (für Perzentile, Anker-Zuordnung, Spacing
        // und die M4-Kostenformel)
        Vector3[] positions = new Vector3[n];
        for (int i = 0; i < n; i++)
            positions[i] = s.GetWorldPoint(i);

        nodePositions = positions;

        // Noise-Offset aus dem Seed: gleicher Seed = gleiches Mäander-Muster.
        // Bewusst kleiner Wertebereich, weil Mathf.PerlinNoise bei sehr großen
        // Koordinaten Präzisions-Artefakte bekommt.
        System.Random noiseRng = new System.Random(noiseSeed);
        noiseOffset = new Vector3(
            (float)noiseRng.NextDouble() * 256f,
            (float)noiseRng.NextDouble() * 256f,
            (float)noiseRng.NextDouble() * 256f);

        // Knoten-Indizes nach Welthöhe sortieren: byHeight[0] = tiefster Punkt
        float[] sortKeys = new float[n];
        int[] byHeight = new int[n];

        for (int i = 0; i < n; i++)
        {
            sortKeys[i] = positions[i].y;
            byHeight[i] = i;
        }

        System.Array.Sort(sortKeys, byHeight);

        float spacing = autoTargetSpacing
            ? g.UsedMaxEdgeLength * targetSpacingFactor
            : manualTargetSpacing;

        // --- Wurzeln wählen ---
        startNodes = PickStartNodes(positions, byHeight, spacing);
        if (startNodes.Length == 0)
        {
            stopwatch.Stop();
            return;
        }

        // --- Multi-Source-Dijkstra: alle Wurzeln starten mit Distanz 0. Ein
        // einziger Lauf liefert die kürzesten Wege zu ALLEN Knoten; jedes Ziel
        // hängt automatisch an der nächstgelegenen Wurzel. Naiv O(n²) - bei
        // <= 2000 Punkten wenige Millisekunden, laut Strategie völlig okay.
        float[] dist = new float[n];
        int[] predNode = new int[n];
        int[] predEdge = new int[n];
        bool[] visited = new bool[n];

        for (int i = 0; i < n; i++)
        {
            dist[i] = float.PositiveInfinity;
            predNode[i] = -1;
            predEdge[i] = -1;
        }

        for (int i = 0; i < startNodes.Length; i++)
            dist[startNodes[i]] = 0f;

        for (int iteration = 0; iteration < n; iteration++)
        {
            // Teuerster Teil der naiven Variante: unbesuchten Knoten mit
            // kleinster Distanz linear suchen.
            int current = -1;
            float best = float.PositiveInfinity;

            for (int i = 0; i < n; i++)
            {
                if (!visited[i] && dist[i] < best)
                {
                    best = dist[i];
                    current = i;
                }
            }

            if (current < 0)
                break; // alles Übrige ist unerreichbar (andere Komponente)

            visited[current] = true;

            IReadOnlyList<int> adjacency = g.GetEdgesOfNode(current);
            for (int a = 0; a < adjacency.Count; a++)
            {
                int edgeIndex = adjacency[a];
                int other = g.GetOtherNode(edgeIndex, current);

                if (visited[other])
                    continue;

                float candidate = dist[current] + GetEdgeCost(g, edgeIndex, current, other);

                if (candidate < dist[other])
                {
                    dist[other] = candidate;
                    predNode[other] = current;
                    predEdge[other] = edgeIndex;
                }
            }
        }

        // --- Ziele wählen (nur erreichbare) und Pfade zurückverfolgen ---
        targetNodes = PickTargetNodes(positions, byHeight, dist, spacing);

        edgeUsage = new int[g.EdgeCount];
        maxEdgeUsage = 0;

        List<int> nodeBuffer = new List<int>(64);
        List<int> edgeBuffer = new List<int>(64);

        for (int t = 0; t < targetNodes.Length; t++)
        {
            nodeBuffer.Clear();
            edgeBuffer.Clear();

            int node = targetNodes[t];
            float length = 0f;

            while (node >= 0)
            {
                nodeBuffer.Add(node);

                int edgeIndex = predEdge[node];
                if (edgeIndex >= 0)
                {
                    edgeBuffer.Add(edgeIndex);
                    length += g.GetEdge(edgeIndex).Length;

                    int usage = ++edgeUsage[edgeIndex];
                    if (usage > maxEdgeUsage)
                        maxEdgeUsage = usage;
                }

                node = predNode[node];
            }

            // Rückverfolgung lief Ziel -> Wurzel; für die Ranke umdrehen.
            nodeBuffer.Reverse();
            edgeBuffer.Reverse();

            paths.Add(nodeBuffer.ToArray());
            pathEdges.Add(edgeBuffer.ToArray());
            pathLengths.Add(length);
        }

        stopwatch.Stop();
        lastComputeMs = (float)stopwatch.Elapsed.TotalMilliseconds;
    }

    // M4: die Houdini-Kostenformel. Der kürzeste Weg umwandert "teure" Zonen
    // und mäandert dadurch organisch - Cost Noise Amp 0 stellt die direkten
    // M3-Pfade wieder her (perfekter A/B-Vergleich).
    //     Kosten = Länge x Mäander(Noise am Kanten-Mittelpunkt) x Kletter-Bias
    // Gerichtet (fromNode -> toNode): abwärts ist teurer als aufwärts.
    private float GetEdgeCost(VineGraphBuilder g, int edgeIndex, int fromNode, int toNode)
    {
        float length = g.GetEdge(edgeIndex).Length;

        if (length < 1e-5f)
            return length; // degenerierte Mini-Kante: Noise/Bias unnötig

        Vector3 from = nodePositions[fromNode];
        Vector3 to = nodePositions[toNode];

        // Mäander: verrauschter Kostenaufschlag, ausgewertet am Kanten-Mittelpunkt
        Vector3 mid = (from + to) * 0.5f;
        float meander = 1f + costNoiseAmp * Noise01(mid * costNoiseScale + noiseOffset);

        // Kletter-Bias: hoch = neutral (x1), quer = +1/2 Bias, runter = +1 Bias
        float upDot = (to.y - from.y) / length;
        float climb = 1f + upwardBias * (1f - upDot) * 0.5f;

        return length * meander * climb;
    }

    // Billiger 3D-Noise aus drei versetzten 2D-Perlin-Ebenen - Unity hat kein
    // natives 3D-Perlin, und für Kantenkosten reicht jede günstige Variante.
    private static float Noise01(Vector3 p)
    {
        float xy = Mathf.PerlinNoise(p.x, p.y);
        float yz = Mathf.PerlinNoise(p.y, p.z);
        float zx = Mathf.PerlinNoise(p.z, p.x);

        return (xy + yz + zx) / 3f;
    }

    private int[] PickStartNodes(Vector3[] positions, int[] byHeight, float spacing)
    {
        List<int> result = new List<int>();

        // Explizite Anker haben Vorrang: pro Anker der nächstgelegene Samplepunkt
        bool hasAnchors = false;

        if (startAnchors != null)
        {
            for (int a = 0; a < startAnchors.Length; a++)
            {
                Transform anchor = startAnchors[a];
                if (anchor == null)
                    continue;

                hasAnchors = true;

                int nearest = -1;
                float nearestSqr = float.MaxValue;

                for (int i = 0; i < positions.Length; i++)
                {
                    float dSqr = (positions[i] - anchor.position).sqrMagnitude;
                    if (dSqr < nearestSqr)
                    {
                        nearestSqr = dSqr;
                        nearest = i;
                    }
                }

                if (nearest >= 0 && !result.Contains(nearest))
                    result.Add(nearest);
            }
        }

        if (hasAnchors)
            return result.ToArray();

        // Auto: aus dem untersten Perzentil, von ganz unten aufwärts, greedy
        // mit Mindestabstand (nur relevant bei mehr als einer Wurzel).
        int candidateCount = Mathf.Clamp(
            Mathf.CeilToInt(positions.Length * startPercentile), 1, positions.Length);

        for (int c = 0; c < candidateCount && result.Count < startCount; c++)
        {
            int node = byHeight[c];

            if (IsFarEnough(positions, result, node, spacing))
                result.Add(node);
        }

        // Fallback: falls das Spacing zu streng war, wenigstens den tiefsten Punkt
        if (result.Count == 0)
            result.Add(byHeight[0]);

        return result.ToArray();
    }

    private int[] PickTargetNodes(Vector3[] positions, int[] byHeight, float[] dist, float spacing)
    {
        int n = positions.Length;
        int candidateCount = Mathf.Clamp(Mathf.CeilToInt(n * targetPercentile), 1, n);

        List<int> result = new List<int>();
        unreachableTargets = 0;

        for (int c = 0; c < candidateCount && result.Count < targetCount; c++)
        {
            int node = byHeight[n - 1 - c]; // von ganz oben abwärts

            if (float.IsPositiveInfinity(dist[node]))
            {
                unreachableTargets++;
                continue; // Graph-Insel: still droppen, im Stats-Log gemeldet
            }

            if (dist[node] <= 0f)
                continue; // das wäre eine Wurzel selbst

            if (IsFarEnough(positions, result, node, spacing))
                result.Add(node);
        }

        return result.ToArray();
    }

    private static bool IsFarEnough(Vector3[] positions, List<int> chosen, int candidate, float spacing)
    {
        float spacingSqr = spacing * spacing;

        for (int i = 0; i < chosen.Count; i++)
        {
            if ((positions[chosen[i]] - positions[candidate]).sqrMagnitude < spacingSqr)
                return false;
        }

        return true;
    }

    private void ClearPaths()
    {
        paths.Clear();
        pathEdges.Clear();
        pathLengths.Clear();
        edgeUsage = null;
        maxEdgeUsage = 0;
        startNodes = System.Array.Empty<int>();
        targetNodes = System.Array.Empty<int>();
        unreachableTargets = 0;
    }

    // ------------------------------------------------------------------
    // Anker-Tracking: Anker im Scene View verschieben -> Wurzel folgt live
    // ------------------------------------------------------------------

    private void CacheAnchorPositions()
    {
        int count = 0;

        if (startAnchors != null)
        {
            for (int i = 0; i < startAnchors.Length; i++)
                if (startAnchors[i] != null)
                    count++;
        }

        if (cachedAnchorPositions == null || cachedAnchorPositions.Length != count)
            cachedAnchorPositions = new Vector3[count];

        int w = 0;

        if (startAnchors != null)
        {
            for (int i = 0; i < startAnchors.Length; i++)
                if (startAnchors[i] != null)
                    cachedAnchorPositions[w++] = startAnchors[i].position;
        }
    }

    private bool AnchorsMoved()
    {
        if (cachedAnchorPositions == null)
            return false; // vor dem ersten Compute regelt das die Versions-Prüfung

        int count = 0;

        if (startAnchors != null)
        {
            for (int i = 0; i < startAnchors.Length; i++)
                if (startAnchors[i] != null)
                    count++;
        }

        if (count != cachedAnchorPositions.Length)
            return true;

        int c = 0;

        if (startAnchors != null)
        {
            for (int i = 0; i < startAnchors.Length; i++)
            {
                if (startAnchors[i] == null)
                    continue;

                if ((startAnchors[i].position - cachedAnchorPositions[c]).sqrMagnitude > 1e-6f)
                    return true;

                c++;
            }
        }

        return false;
    }

    private VineGraphBuilder ResolveGraph()
    {
        return graphBuilder != null ? graphBuilder : GetComponent<VineGraphBuilder>();
    }

    private VineSurfaceSampler ResolveSampler()
    {
        VineGraphBuilder g = ResolveGraph();
        return g != null ? g.Sampler : null;
    }

    private void ValidateSetup()
    {
        if (ResolveGraph() == null)
        {
            Debug.LogWarning("[VinePathfinder] Kein VineGraphBuilder gefunden: entweder 'Graph Builder' " +
                "zuweisen oder das Script auf dasselbe GameObject wie den Graphen legen.", this);
        }
    }

    // ------------------------------------------------------------------
    // Gizmos - der "Houdini-Viewport-Ersatz" für M3
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!showGizmoOnlyWhenSelected)
            DrawPathGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (showGizmoOnlyWhenSelected)
            DrawPathGizmos();
    }

    private void DrawPathGizmos()
    {
        if (!showPaths && !showEndpoints)
            return;

        // Hält die ganze Kette Sampler -> Graph -> Pfade automatisch aktuell
        EnsureUpToDate();

        if (!HasPaths)
            return;

        VineGraphBuilder g = ResolveGraph();
        VineSurfaceSampler s = ResolveSampler();

        if (g == null || s == null || !g.HasGraph || !s.HasSamples || s.SampleCount != g.NodeCount)
            return;

        if (showPaths)
        {
            for (int p = 0; p < paths.Count; p++)
            {
                int[] nodes = paths[p];
                int[] usedEdges = pathEdges[p];

                if (colorMode == PathColorMode.PerPath)
                    Gizmos.color = PathColor(p);
                else if (colorMode == PathColorMode.Single)
                    Gizmos.color = singlePathColor;

                for (int i = 0; i < nodes.Length - 1; i++)
                {
                    if (colorMode == PathColorMode.UsageHeat)
                    {
                        int usage = edgeUsage[usedEdges[i]];
                        float heat = maxEdgeUsage > 1
                            ? (usage - 1) / (float)(maxEdgeUsage - 1)
                            : 0f;

                        Gizmos.color = Color.Lerp(pathBaseColor, pathHotColor, heat);
                    }

                    Gizmos.DrawLine(s.GetWorldPoint(nodes[i]), s.GetWorldPoint(nodes[i + 1]));
                }
            }
        }

        if (showEndpoints)
        {
            Gizmos.color = startMarkerColor;
            for (int i = 0; i < startNodes.Length; i++)
                Gizmos.DrawSphere(s.GetWorldPoint(startNodes[i]), endpointSize);

            Gizmos.color = targetMarkerColor;
            for (int i = 0; i < targetNodes.Length; i++)
                Gizmos.DrawSphere(s.GetWorldPoint(targetNodes[i]), endpointSize * 0.75f);
        }
    }

    // Goldener-Winkel-Schritt im Farbkreis, mit Offset gegen Rot-Kollisionen
    private static Color PathColor(int pathIndex)
    {
        float hue = (0.25f + pathIndex * 0.618034f) % 1f;
        return Color.HSVToRGB(hue, 0.7f, 1f);
    }

    // ------------------------------------------------------------------
    // Debug / Kontextmenü
    // ------------------------------------------------------------------

    [ContextMenu("Debug: Neuer Noise-Seed (Mäander neu würfeln)")]
    private void DebugRandomNoiseSeed()
    {
        noiseSeed = Random.Range(0, 100000);
        RecomputeNow();
        Debug.Log($"[VinePathfinder] Neuer Noise-Seed: {noiseSeed} - gleiche Punkte, neue Wege.", this);
    }

    [ContextMenu("Debug: Recompute + Stats loggen")]
    private void DebugRecomputeWithStats()
    {
        ValidateSetup();
        RecomputeNow();

        if (!HasPaths)
        {
            Debug.LogWarning("[VinePathfinder] Keine Pfade berechnet - Graph vorhanden? " +
                "Ziele erreichbar? Siehe Warnungen weiter oben.", this);
            return;
        }

        float totalLength = 0f;
        float longest = 0f;

        for (int i = 0; i < pathLengths.Count; i++)
        {
            totalLength += pathLengths[i];
            if (pathLengths[i] > longest)
                longest = pathLengths[i];
        }

        string unreachableInfo = unreachableTargets > 0
            ? $" | {unreachableTargets} Ziel-Kandidaten UNERREICHBAR (Graph-Inseln -> M2-Regler)"
            : "";

        Debug.Log(
            $"[VinePathfinder] {paths.Count}/{targetCount} Pfade von {startNodes.Length} Wurzel(n) | " +
            $"Länge gesamt/max: {totalLength:0.00}/{longest:0.00} | " +
            $"max. Kanten-Nutzung (Stamm): {maxEdgeUsage} | " +
            $"Compute: {lastComputeMs:0.0} ms{unreachableInfo}", this);
    }
}
