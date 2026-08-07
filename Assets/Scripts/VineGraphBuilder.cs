using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// M2 des Ranken-Systems: das Houdini-"Connect Adjacent Pieces"-Äquivalent.
/// Verbindet die Sample-Punkte des VineSurfaceSampler zu einem k-Nearest-Neighbor-
/// Graphen - gefiltert nach Max-Kantenlänge und Normalen-Kompatibilität, damit
/// keine Kanten durch das Mesh oder zwischen gegenüberliegenden Flächen "tunneln".
/// Zeigt das Netz als Gizmos, meldet Zusammenhangskomponenten und isolierte
/// Punkte. M3 (Dijkstra) liest den Graphen über die Public API.
/// Läuft komplett im Edit Mode - kein Play nötig.
/// </summary>
public class VineGraphBuilder : MonoBehaviour
{
    /// <summary>Ungerichtete Kante zwischen zwei Sample-Indizes. Die Länge wird
    /// beim Build in Welt-Maß eingefroren (Basis für die Kantenkosten ab M3/M4).</summary>
    public struct Edge
    {
        public int NodeA;
        public int NodeB;
        public float Length;
    }

    [Header("References")]
    [Tooltip("Der Sampler aus M1. Leer = auf diesem GameObject suchen.")]
    [SerializeField] private VineSurfaceSampler sampler;

    [Header("Nachbarschaft")]
    [Tooltip("Wie viele nächste Nachbarn pro Punkt verbunden werden. 4-8 ist der sinnvolle Bereich:\nweniger = löchrig/Inseln, mehr = dichteres Netz.")]
    [Range(1, 12)]
    [SerializeField] private int kNeighbors = 6;

    [Tooltip("Max-Kantenlänge automatisch aus Oberfläche und Punktzahl ableiten:\nsqrt(Fläche / Punktzahl) x Faktor. Skaliert dadurch automatisch mit, wenn sich Mesh oder Point Count ändern.")]
    [SerializeField] private bool autoMaxEdgeLength = true;

    [Tooltip("Faktor für die automatische Max-Kantenlänge. 1.5-2.5 ist der sinnvolle Bereich.")]
    [Range(1f, 4f)]
    [SerializeField] private float autoEdgeLengthFactor = 2f;

    [Tooltip("Feste Max-Kantenlänge in Welteinheiten (nur relevant, wenn Auto aus ist).")]
    [Min(0.001f)]
    [SerializeField] private float manualMaxEdgeLength = 0.5f;

    [Tooltip("Normalen-Filter gegen Tunnel-Kanten: Kante nur, wenn Dot(NormalA, NormalB) >= diesem Wert.\n-0.1 (Default) blockt gegenüberliegende Flächen (Dot ~ -1), erlaubt aber 90°-Knicke -\nwichtig, damit Hard-Edge-Meshes (Graybox-Cubes!) nicht in Insel-Flächen zerfallen.\nAuf weichen Meshes (Sphere, Smooth-Fels) darf man höher gehen (~0.3).")]
    [Range(-1f, 1f)]
    [SerializeField] private float normalDotMin = -0.1f;

    [Header("Luftbrücken-Filter")]
    [Tooltip("Verwirft Kanten, die über Innenecken oder Spalten durch die LUFT abkürzen, statt der\nOberfläche zu folgen (der 'kurze Pfad an der Ecke vorbei').\nKonvexe Übergänge (ums Brett herum) bleiben ausdrücklich erlaubt.\nBRAUCHT einen Collider auf dem Ziel-Mesh - ohne Treffer überspringt sich der Check mit Warnung selbst.")]
    [SerializeField] private bool rejectAirBridges = true;

    [Tooltip("Layer, auf denen der Luftbrücken-Check die Oberfläche sucht.")]
    [SerializeField] private LayerMask surfaceMask = ~0;

    [Tooltip("Wie weit der Kanten-Mittelpunkt über der Oberfläche schweben darf, relativ zur Kantenlänge.\n90°-Abkürzungen schweben bei 0.5 - Default 0.35 wirft genau sie raus, sanfte Biegungen (~135°) bleiben.\nKleiner = Pfade schmiegen sich enger in Ecken.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float airBridgeTolerance = 0.35f;

    [Tooltip("Bei Inspector-Änderungen automatisch neu bauen (Live-Tuning).")]
    [SerializeField] private bool rebuildOnValidate = true;

    [Header("Gizmos")]
    [SerializeField] private bool showEdges = true;

    [SerializeField] private Color edgeColor = new Color(0.3f, 0.9f, 0.6f, 0.35f);

    [Tooltip("Kanten pro Zusammenhangskomponente einfärben - Inseln springen sofort ins Auge.\nGesund = alles eine Farbe.")]
    [SerializeField] private bool colorByComponent = false;

    [Tooltip("Punkte ohne einzige Kante rot hervorheben (Zeichen für zu strenge Filter).")]
    [SerializeField] private bool showIsolatedPoints = true;

    [SerializeField] private Color isolatedColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Range(0.005f, 0.2f)]
    [SerializeField] private float isolatedPointSize = 0.05f;

    [Tooltip("Gizmos nur zeichnen, solange das Objekt selektiert ist (weniger Clutter).")]
    [SerializeField] private bool showGizmoOnlyWhenSelected = false;

    // --- Graph-Daten (Snapshot vom Build-Zeitpunkt) ---
    private Edge[] edges;
    private List<int>[] nodeEdges;    // pro Node: Indizes in 'edges'
    private int[] componentOfNode;    // Komponenten-Id, -2 = isoliert
    private int nodeCount;
    private int componentCount;
    private int isolatedCount;
    private int largestComponentSize;
    private int airBridgeRejectedCount;
    private bool warnedNoSurfaceCollider;
    private float usedMaxEdgeLength;
    private float lastBuildMs;
    private int builtForVersion = -1;

    private Vector3[] gizmoPositions; // wiederverwendeter Buffer fürs Zeichnen

    // ------------------------------------------------------------------
    // Public API - hier dockt M3 (Dijkstra) an.
    // ------------------------------------------------------------------

    public bool HasGraph => edges != null && nodeEdges != null && nodeCount > 0;

    public int NodeCount => nodeCount;

    public int EdgeCount => edges != null ? edges.Length : 0;

    /// <summary>Anzahl Zusammenhangskomponenten (isolierte Punkte nicht mitgezählt).
    /// Mehr als 1 heißt: nicht jedes Ziel ist von jedem Start erreichbar.</summary>
    public int ComponentCount => componentCount;

    public int IsolatedCount => isolatedCount;

    /// <summary>Zählt bei jedem BuildNow() hoch - M3 (Pathfinder) prüft darüber,
    /// ob seine Pfade noch zum aktuellen Graphen passen.</summary>
    public int GraphVersion { get; private set; }

    /// <summary>Tatsächlich benutzte Max-Kantenlänge des letzten Builds (auto oder manuell).</summary>
    public float UsedMaxEdgeLength => usedMaxEdgeLength;

    public VineSurfaceSampler Sampler => ResolveSampler();

    public Edge GetEdge(int edgeIndex) => edges[edgeIndex];

    public IReadOnlyList<int> GetEdgesOfNode(int nodeIndex) => nodeEdges[nodeIndex];

    /// <summary>Für Dijkstra: der gegenüberliegende Endpunkt einer Kante.</summary>
    public int GetOtherNode(int edgeIndex, int nodeIndex)
    {
        Edge edge = edges[edgeIndex];
        return edge.NodeA == nodeIndex ? edge.NodeB : edge.NodeA;
    }

    /// <summary>Komponenten-Id eines Punkts (-2 = isoliert). M3 prüft damit vorab,
    /// ob Start und Ziel überhaupt verbunden sind, statt es per Dijkstra zu merken.</summary>
    public int GetComponentOfNode(int nodeIndex) => componentOfNode[nodeIndex];

    /// <summary>Baut nach, falls der Sampler seit dem letzten Build neu gesampelt hat
    /// (Seed/Punktzahl geändert, Reload, ...). Hält die Kette Sampler -> Graph konsistent.</summary>
    public void EnsureUpToDate()
    {
        VineSurfaceSampler s = ResolveSampler();
        if (s == null)
            return;

        if (builtForVersion == s.SampleVersion)
            return;

        BuildNow();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        ValidateSetup();
        BuildNow();
    }

    private void OnValidate()
    {
        // Damit K, Kantenlänge und Normalen-Filter im Inspector "live" reagieren
        if (rebuildOnValidate)
            BuildNow();
    }

    // ------------------------------------------------------------------
    // Build-Kern: das Houdini-"Connect Adjacent Pieces"-Äquivalent
    // ------------------------------------------------------------------

    [ContextMenu("Rebuild Now")]
    public void BuildNow()
    {
        ClearGraph();

        // Version hochzählen, BEVOR irgendetwas weiter passiert: auch ein
        // fehlgeschlagener Build hat den alten Graphen gelöscht - M3 bekommt
        // das über die Version mit und verwirft seine Pfade entsprechend.
        GraphVersion++;

        VineSurfaceSampler s = ResolveSampler();
        if (s == null)
            return;

        if (!s.HasSamples)
            s.SampleNow();

        // Version JETZT merken - auch bei Fehlschlag, damit nicht jeden Gizmo-Frame
        // erneut versucht wird. Sobald der Sampler wieder liefert, ändert sich seine
        // Version und EnsureUpToDate baut automatisch nach.
        builtForVersion = s.SampleVersion;

        if (!s.HasSamples)
            return;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        nodeCount = s.SampleCount;

        // 1) Positionen & Normalen einmalig in Welt-Space ziehen (Build-Snapshot).
        Vector3[] positions = new Vector3[nodeCount];
        Vector3[] normals = new Vector3[nodeCount];

        for (int i = 0; i < nodeCount; i++)
        {
            positions[i] = s.GetWorldPoint(i);
            normals[i] = s.GetWorldNormal(i);
        }

        // 2) Max-Kantenlänge bestimmen. Auto-Heuristik: charakteristischer
        //    Punktabstand ~ sqrt(Fläche / Punktzahl), mal Faktor -> skaliert
        //    automatisch mit Dichte und Meshgröße.
        usedMaxEdgeLength = autoMaxEdgeLength
            ? Mathf.Sqrt(s.TotalSurfaceArea / Mathf.Max(1, nodeCount)) * autoEdgeLengthFactor
            : manualMaxEdgeLength;

        float maxSqr = usedMaxEdgeLength * usedMaxEdgeLength;

        // Luftbrücken-Check nur aktivieren, wenn tatsächlich ein Collider
        // getroffen wird - sonst würde er den kompletten Graphen leeren.
        bool useAirBridgeCheck = rejectAirBridges && SurfaceProbeAvailable(positions, normals);

        // 3) k nächste Nachbarn pro Punkt (Brute Force reicht bis ~2000 Punkte
        //    locker), gefiltert nach Distanz + Normalen-Dot. Duplikate (i-j vs j-i)
        //    werden über gepackte Keys aussortiert.
        int k = Mathf.Max(1, kNeighbors);
        int[] bestIndex = new int[k];
        float[] bestSqr = new float[k];

        var edgeKeys = new HashSet<long>();
        var edgeList = new List<Edge>(nodeCount * k / 2);

        for (int i = 0; i < nodeCount; i++)
        {
            int found = 0;

            for (int j = 0; j < nodeCount; j++)
            {
                if (j == i)
                    continue;

                float distSqr = (positions[j] - positions[i]).sqrMagnitude;

                if (distSqr > maxSqr)
                    continue;

                // Schneller Ausstieg, bevor der Dot gerechnet wird
                if (found == k && distSqr >= bestSqr[k - 1])
                    continue;

                if (Vector3.Dot(normals[i], normals[j]) < normalDotMin)
                    continue;

                if (useAirBridgeCheck && IsAirBridge(positions[i], positions[j],
                        normals[i], normals[j], Mathf.Sqrt(distSqr)))
                {
                    airBridgeRejectedCount++;
                    continue;
                }

                InsertCandidate(bestIndex, bestSqr, ref found, k, j, distSqr);
            }

            for (int b = 0; b < found; b++)
            {
                int j = bestIndex[b];
                int lo = i < j ? i : j;
                int hi = i < j ? j : i;
                long key = ((long)lo << 32) | (uint)hi;

                if (edgeKeys.Add(key))
                {
                    edgeList.Add(new Edge
                    {
                        NodeA = lo,
                        NodeB = hi,
                        Length = Mathf.Sqrt(bestSqr[b])
                    });
                }
            }
        }

        edges = edgeList.ToArray();

        // 4) Adjazenzlisten: pro Node die Indizes seiner Kanten.
        nodeEdges = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            nodeEdges[i] = new List<int>(k);

        for (int e = 0; e < edges.Length; e++)
        {
            nodeEdges[edges[e].NodeA].Add(e);
            nodeEdges[edges[e].NodeB].Add(e);
        }

        // 5) Zusammenhangskomponenten (Flood Fill) - das Frühwarnsystem für M3:
        //    mehr als eine Komponente heißt, dass nicht jedes Ziel erreichbar ist.
        ComputeComponents();

        stopwatch.Stop();
        lastBuildMs = (float)stopwatch.Elapsed.TotalMilliseconds;
    }

    // Hält bestIndex/bestSqr aufsteigend sortiert. Bei kleinem k (<= 12) ist
    // Insertion die schnellste und einfachste Variante.
    private static void InsertCandidate(int[] bestIndex, float[] bestSqr, ref int found, int k, int candidate, float distSqr)
    {
        int slot;

        if (found < k)
        {
            slot = found;
            found++;
        }
        else
        {
            if (distSqr >= bestSqr[k - 1])
                return;

            slot = k - 1;
        }

        bestIndex[slot] = candidate;
        bestSqr[slot] = distSqr;

        while (slot > 0 && bestSqr[slot] < bestSqr[slot - 1])
        {
            float tmpSqr = bestSqr[slot - 1];
            bestSqr[slot - 1] = bestSqr[slot];
            bestSqr[slot] = tmpSqr;

            int tmpIdx = bestIndex[slot - 1];
            bestIndex[slot - 1] = bestIndex[slot];
            bestIndex[slot] = tmpIdx;

            slot--;
        }
    }

    // Prüft, ob eine Kante als "Luftbrücke" über einer Innenecke oder einem Spalt
    // schwebt. Raycast von weit außen entlang der gemittelten Normalen zurück
    // Richtung Oberfläche:
    //   - Hit VOR dem Mittelpunkt  -> Mittelpunkt leicht im Material = konvexer
    //     Übergang. Erlaubt (sonst zerfiele jeder Cube wieder in Insel-Flächen).
    //   - Hit HINTER dem Mittelpunkt -> Mittelpunkt schwebt. Ab Toleranz x
    //     Kantenlänge ist es eine Abkürzung durch die Luft -> verwerfen.
    //     (90°-Brücken schweben bei 0.5, kurze Querungen nahe der Ecke bleiben.)
    //   - Kein Hit -> Ray-Start steckt selbst im Material (tiefe Durchquerung,
    //     z.B. innenliegende Flächen bei Compound-Meshes) -> verwerfen.
    private bool IsAirBridge(Vector3 a, Vector3 b, Vector3 normalA, Vector3 normalB, float edgeLength)
    {
        Vector3 mid = (a + b) * 0.5f;

        Vector3 n = normalA + normalB;
        n = n.sqrMagnitude > 1e-6f ? n.normalized : normalA;

        float castDistance = usedMaxEdgeLength * 2f;
        Vector3 origin = mid + n * castDistance;

        if (!Physics.Raycast(origin, -n, out RaycastHit hit, castDistance * 2f, surfaceMask))
            return true;

        // > 0: die Oberfläche liegt HINTER dem Mittelpunkt, er schwebt also
        float floatDistance = hit.distance - castDistance;

        return floatDistance > edgeLength * airBridgeTolerance;
    }

    // Einmal pro Build prüfen, ob der Check überhaupt eine Oberfläche findet -
    // ohne Collider würde er sonst jede Kante verwerfen.
    private bool SurfaceProbeAvailable(Vector3[] positions, Vector3[] normals)
    {
        float castDistance = usedMaxEdgeLength * 2f;
        int probes = Mathf.Min(8, positions.Length);

        for (int i = 0; i < probes; i++)
        {
            int index = (positions.Length / probes) * i;
            Vector3 origin = positions[index] + normals[index] * castDistance;

            if (Physics.Raycast(origin, -normals[index], castDistance * 2f, surfaceMask))
            {
                warnedNoSurfaceCollider = false;
                return true;
            }
        }

        if (!warnedNoSurfaceCollider)
        {
            Debug.LogWarning("[VineGraphBuilder] Luftbrücken-Filter aktiv, aber kein Collider getroffen - " +
                "Check wird übersprungen. MeshCollider/BoxCollider aufs Ziel-Mesh legen und Surface Mask prüfen.", this);
            warnedNoSurfaceCollider = true;
        }

        return false;
    }

    private void ComputeComponents()
    {
        componentOfNode = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            componentOfNode[i] = -1;

        componentCount = 0;
        isolatedCount = 0;
        largestComponentSize = 0;

        Queue<int> queue = new Queue<int>();

        for (int start = 0; start < nodeCount; start++)
        {
            if (componentOfNode[start] != -1)
                continue;

            if (nodeEdges[start].Count == 0)
            {
                componentOfNode[start] = -2; // isoliert
                isolatedCount++;
                continue;
            }

            int id = componentCount;
            componentCount++;

            int size = 0;
            queue.Enqueue(start);
            componentOfNode[start] = id;

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                size++;

                List<int> adjacency = nodeEdges[node];
                for (int a = 0; a < adjacency.Count; a++)
                {
                    int other = GetOtherNode(adjacency[a], node);

                    if (componentOfNode[other] == -1)
                    {
                        componentOfNode[other] = id;
                        queue.Enqueue(other);
                    }
                }
            }

            if (size > largestComponentSize)
                largestComponentSize = size;
        }
    }

    private void ClearGraph()
    {
        edges = null;
        nodeEdges = null;
        componentOfNode = null;
        nodeCount = 0;
        componentCount = 0;
        isolatedCount = 0;
        largestComponentSize = 0;
        airBridgeRejectedCount = 0;
    }

    private VineSurfaceSampler ResolveSampler()
    {
        return sampler != null ? sampler : GetComponent<VineSurfaceSampler>();
    }

    private void ValidateSetup()
    {
        if (ResolveSampler() == null)
        {
            Debug.LogWarning("[VineGraphBuilder] Kein VineSurfaceSampler gefunden: entweder 'Sampler' " +
                "zuweisen oder das Script auf dasselbe GameObject wie den Sampler legen.", this);
        }
    }

    // ------------------------------------------------------------------
    // Gizmos - der "Houdini-Viewport-Ersatz" für M2
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!showGizmoOnlyWhenSelected)
            DrawGraphGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (showGizmoOnlyWhenSelected)
            DrawGraphGizmos();
    }

    private void DrawGraphGizmos()
    {
        if (!showEdges && !showIsolatedPoints)
            return;

        // Baut still nach, wenn der Sampler zwischenzeitlich neu gesampelt hat
        EnsureUpToDate();

        if (!HasGraph)
            return;

        VineSurfaceSampler s = ResolveSampler();
        if (s == null || !s.HasSamples || s.SampleCount != nodeCount)
            return;

        // Live-Positionen: das Netz klebt am Objekt, auch wenn es bewegt/rotiert wird
        if (gizmoPositions == null || gizmoPositions.Length != nodeCount)
            gizmoPositions = new Vector3[nodeCount];

        for (int i = 0; i < nodeCount; i++)
            gizmoPositions[i] = s.GetWorldPoint(i);

        if (showEdges)
        {
            Gizmos.color = edgeColor;

            for (int e = 0; e < edges.Length; e++)
            {
                Edge edge = edges[e];

                if (colorByComponent)
                    Gizmos.color = ComponentColor(componentOfNode[edge.NodeA]);

                Gizmos.DrawLine(gizmoPositions[edge.NodeA], gizmoPositions[edge.NodeB]);
            }
        }

        if (showIsolatedPoints && isolatedCount > 0)
        {
            Gizmos.color = isolatedColor;

            for (int i = 0; i < nodeCount; i++)
            {
                if (componentOfNode[i] == -2)
                    Gizmos.DrawSphere(gizmoPositions[i], isolatedPointSize);
            }
        }
    }

    // Goldener-Winkel-Schritt im Farbkreis: aufeinanderfolgende Komponenten-Ids
    // bekommen deutlich unterscheidbare Farben (derselbe Trick wie beim
    // Butterfly-Chill-Orbit gegen sichtbare Reihenbildung).
    private static Color ComponentColor(int componentId)
    {
        if (componentId < 0)
            return Color.red;

        float hue = (0.35f + componentId * 0.618034f) % 1f; // Offset 0.35: Komponente 0 = grün,
                                                            // damit sie sich klar vom Isoliert-Rot abhebt
        return Color.HSVToRGB(hue, 0.75f, 1f);
    }

    // ------------------------------------------------------------------
    // Debug / Kontextmenü
    // ------------------------------------------------------------------

    [ContextMenu("Debug: Rebuild + Stats loggen")]
    private void DebugRebuildWithStats()
    {
        ValidateSetup();
        BuildNow();

        if (!HasGraph)
        {
            Debug.LogWarning("[VineGraphBuilder] Kein Graph gebaut - siehe Warnungen oben.", this);
            return;
        }

        int minDegree = int.MaxValue;
        int maxDegree = 0;
        float degreeSum = 0f;

        for (int i = 0; i < nodeCount; i++)
        {
            int degree = nodeEdges[i].Count;
            if (degree < minDegree) minDegree = degree;
            if (degree > maxDegree) maxDegree = degree;
            degreeSum += degree;
        }

        string lengthMode = autoMaxEdgeLength ? "auto" : "manuell";

        Debug.Log(
            $"[VineGraphBuilder] {nodeCount} Punkte, {EdgeCount} Kanten " +
            $"(Grad min/Ø/max: {minDegree}/{degreeSum / nodeCount:0.0}/{maxDegree}) | " +
            $"MaxEdgeLength: {usedMaxEdgeLength:0.000} ({lengthMode}) | " +
            $"{componentCount} Komponente(n), größte: {largestComponentSize} Punkte, " +
            $"{isolatedCount} isoliert | Luftbrücken verworfen: {airBridgeRejectedCount} | " +
            $"Build: {lastBuildMs:0.0} ms", this);
    }
}
