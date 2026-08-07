using UnityEngine;

/// <summary>
/// M1 des Ranken-Systems: verteilt N Punkte flächengewichtet auf der Oberfläche
/// eines Meshes (das Houdini-"Scatter"-Äquivalent) und zeigt sie als Gizmos an.
/// Punkte + Normalen werden im lokalen Space des Ziel-Meshes gecacht und folgen
/// dessen Transform. Spätere Bausteine (M2 Graph, M3 Dijkstra) lesen die Samples
/// über die Public API (SampleCount, GetWorldPoint, GetWorldNormal).
/// Funktioniert komplett im Edit Mode - kein Play nötig.
/// </summary>
public class VineSurfaceSampler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("MeshFilter des Ziel-Meshes (Fels, Säule, Torbogen ...).\nLeer = MeshFilter auf diesem GameObject verwenden.")]
    [SerializeField] private MeshFilter targetMesh;

    [Header("Sampling")]
    [Tooltip("Anzahl Punkte auf der Oberfläche. 500-2000 ist der sinnvolle Prototyp-Bereich.")]
    [Range(10, 5000)]
    [SerializeField] private int pointCount = 800;

    [Tooltip("Seed für deterministisches Sampling: gleicher Seed = exakt gleiche Verteilung.\nFür Regrow-Variationen später einfach den Seed wechseln.")]
    [SerializeField] private int seed = 12345;

    [Tooltip("Alle Sample-Normalen umdrehen. Nötig, wenn die Normalen des Meshes nach INNEN zeigen\n(kommt bei dedizierten Collision-Meshes vor). Erkennbar per 'Show Normals' - die Striche müssen\naus der Oberfläche HERAUS zeigen - oder über den Collider-Check am Vine Grower.")]
    [SerializeField] private bool flipNormals = false;

    [Tooltip("Bei Inspector-Änderungen automatisch neu sampeln (Live-Tuning).")]
    [SerializeField] private bool resampleOnValidate = true;

    [Header("Gizmos")]
    [SerializeField] private bool showPoints = true;

    [Tooltip("Radius der Punkt-Kugeln in Welteinheiten.")]
    [Range(0.002f, 0.2f)]
    [SerializeField] private float pointSize = 0.02f;

    [Tooltip("Punkte nach Welthöhe einfärben (unten = Root Color, oben = Tip Color).\nVorschau für die spätere Start-/Ziel-Wahl per Y-Perzentil (M3).")]
    [SerializeField] private bool colorByHeight = true;

    [SerializeField] private Color rootColor = new Color(0.55f, 0.35f, 0.15f, 1f);
    [SerializeField] private Color tipColor = new Color(0.4f, 1f, 0.45f, 1f);

    [Tooltip("Einheitliche Punktfarbe, wenn 'Color By Height' aus ist.")]
    [SerializeField] private Color pointColor = new Color(0.4f, 1f, 0.45f, 1f);

    [Tooltip("Normalen als kurze Linien zeichnen. Check: Sie müssen aus der Oberfläche heraus zeigen.\nWichtig als Basis für den Tunnel-Filter in M2.")]
    [SerializeField] private bool showNormals = false;

    [Tooltip("Länge der Normalen-Linien in Welteinheiten.")]
    [Range(0.02f, 0.5f)]
    [SerializeField] private float normalLength = 0.12f;

    [SerializeField] private Color normalColor = new Color(0.3f, 0.7f, 1f, 0.8f);

    [Tooltip("Gizmos nur zeichnen, solange das Objekt selektiert ist (weniger Clutter).")]
    [SerializeField] private bool showGizmoOnlyWhenSelected = false;

    // Samples liegen im LOKALEN Space des Ziel-Meshes, damit sie automatisch
    // mitgehen, wenn das Objekt bewegt oder rotiert wird.
    private Vector3[] localPositions;
    private Vector3[] localNormals;
    private float totalWorldArea;
    private Transform cachedMeshTransform;

    // ------------------------------------------------------------------
    // Public API - hier docken M2 (Graph) und M3 (Dijkstra) an.
    // ------------------------------------------------------------------

    public int SampleCount => localPositions != null ? localPositions.Length : 0;

    public bool HasSamples => SampleCount > 0;

    /// <summary>Zählt bei jedem SampleNow() hoch - nachgelagerte Bausteine (M2 Graph, ...)
    /// prüfen darüber, ob ihre Daten noch zum aktuellen Sampling passen.</summary>
    public int SampleVersion { get; private set; }

    /// <summary>Gesamtoberfläche des Meshes in Welt-m² (Stand: letztes Sampling).
    /// Praktisch für M2: mittlerer Punktabstand ~ sqrt(Fläche / Punktzahl).</summary>
    public float TotalSurfaceArea => totalWorldArea;

    /// <summary>Transform, in dessen lokalem Space die Samples liegen.</summary>
    public Transform MeshTransform => cachedMeshTransform != null ? cachedMeshTransform : transform;

    public Vector3 GetWorldPoint(int index)
    {
        return MeshTransform.TransformPoint(localPositions[index]);
    }

    public Vector3 GetWorldNormal(int index)
    {
        // worldToLocal transponiert = korrekte Normalen-Matrix, auch bei
        // non-uniform Scale (gestreckte Graybox-Cubes).
        Matrix4x4 normalMatrix = MeshTransform.worldToLocalMatrix.transpose;
        return normalMatrix.MultiplyVector(localNormals[index]).normalized;
    }

    /// <summary>Sampling von außen anstoßen, z.B. später beim Regrow mit neuem Seed.</summary>
    public void Resample(int newSeed)
    {
        seed = newSeed;
        SampleNow();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        ValidateSetup();
        SampleNow();
    }

    private void OnValidate()
    {
        // Damit Point Count, Seed usw. direkt im Inspector "live" reagieren
        if (resampleOnValidate)
            SampleNow();
    }

    // ------------------------------------------------------------------
    // Sampling-Kern: das Houdini-"Scatter"-Äquivalent
    // ------------------------------------------------------------------

    [ContextMenu("Resample Now")]
    public void SampleNow()
    {
        localPositions = null;
        localNormals = null;
        totalWorldArea = 0f;

        // Version hochzählen, BEVOR irgendetwas weiter passiert: auch ein
        // fehlgeschlagenes Sampling hat den alten Zustand gelöscht - M2+
        // bekommen das über die Version mit und bauen entsprechend nach.
        SampleVersion++;

        MeshFilter mf = ResolveMeshFilter();
        if (mf == null || mf.sharedMesh == null)
            return; // Warnung kommt aus ValidateSetup, sonst spammt OnValidate

        cachedMeshTransform = mf.transform;

        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;

        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
            return;

        bool hasVertexNormals = normals != null && normals.Length == vertices.Length;
        int triangleCount = triangles.Length / 3;

        // 1) Dreiecksflächen in WELT-Maß aufsummieren, damit non-uniform Scale
        //    (z.B. ein 1x3x1 gestreckter Cube) die Dichte nicht verzerrt.
        Matrix4x4 localToWorld = cachedMeshTransform.localToWorldMatrix;

        float[] cumulativeArea = new float[triangleCount];
        float runningArea = 0f;

        for (int i = 0; i < triangleCount; i++)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 0]]);
            Vector3 b = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 1]]);
            Vector3 c = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 2]]);

            runningArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumulativeArea[i] = runningArea;
        }

        if (runningArea <= 1e-6f)
        {
            Debug.LogWarning("[VineSurfaceSampler] Das Mesh hat (fast) keine Fläche - Sampling abgebrochen.", this);
            return;
        }

        totalWorldArea = runningArea;

        // 2) Punkte ziehen: Dreieck proportional zu seiner Fläche wählen (binäre
        //    Suche in der kumulierten Liste), dann gleichverteilter baryzentrischer
        //    Punkt darin. System.Random statt UnityEngine.Random: deterministisch
        //    pro Seed und ohne den globalen Random-State anderer Systeme anzufassen.
        System.Random rng = new System.Random(seed);

        localPositions = new Vector3[pointCount];
        localNormals = new Vector3[pointCount];

        for (int p = 0; p < pointCount; p++)
        {
            float pick = (float)rng.NextDouble() * totalWorldArea;

            int triIndex = System.Array.BinarySearch(cumulativeArea, pick);
            if (triIndex < 0)
                triIndex = ~triIndex;
            triIndex = Mathf.Clamp(triIndex, 0, triangleCount - 1);

            int i0 = triangles[triIndex * 3 + 0];
            int i1 = triangles[triIndex * 3 + 1];
            int i2 = triangles[triIndex * 3 + 2];

            // Reflexions-Trick: (r1, r2) gleichverteilt im Einheitsquadrat,
            // bei r1 + r2 > 1 an der Diagonale spiegeln -> gleichverteilt im Dreieck.
            float r1 = (float)rng.NextDouble();
            float r2 = (float)rng.NextDouble();

            if (r1 + r2 > 1f)
            {
                r1 = 1f - r1;
                r2 = 1f - r2;
            }

            float r0 = 1f - r1 - r2;

            localPositions[p] =
                r0 * vertices[i0] +
                r1 * vertices[i1] +
                r2 * vertices[i2];

            Vector3 normal;
            if (hasVertexNormals)
            {
                normal =
                    r0 * normals[i0] +
                    r1 * normals[i1] +
                    r2 * normals[i2];
            }
            else
            {
                // Fallback: Face-Normale, falls das Mesh keine Vertex-Normalen hat
                normal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
            }

            if (flipNormals)
                normal = -normal;

            localNormals[p] = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
        }
    }

    private MeshFilter ResolveMeshFilter()
    {
        return targetMesh != null ? targetMesh : GetComponent<MeshFilter>();
    }

    private void ValidateSetup()
    {
        MeshFilter mf = ResolveMeshFilter();

        if (mf == null)
        {
            Debug.LogWarning("[VineSurfaceSampler] Kein MeshFilter gefunden: entweder 'Target Mesh' " +
                "zuweisen oder das Script direkt auf das Mesh-Objekt legen.", this);
            return;
        }

        if (mf.sharedMesh == null)
        {
            Debug.LogWarning("[VineSurfaceSampler] Der MeshFilter hat kein Mesh zugewiesen.", this);
            return;
        }

        if (!mf.sharedMesh.isReadable)
        {
            Debug.LogWarning("[VineSurfaceSampler] Das Mesh ist nicht als Read/Write markiert. " +
                "Im Editor funktioniert das Sampling trotzdem, im BUILD nicht - " +
                "in den Import Settings des Meshes 'Read/Write' aktivieren.", this);
        }
    }

    // ------------------------------------------------------------------
    // Gizmos - der "Houdini-Viewport-Ersatz" für M1
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!showGizmoOnlyWhenSelected)
            DrawSampleGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (showGizmoOnlyWhenSelected)
            DrawSampleGizmos();
    }

    private void DrawSampleGizmos()
    {
        if (!showPoints && !showNormals)
            return;

        // Nach Szenen-/Skript-Reload sind die (bewusst nicht serialisierten)
        // Samples weg: still nachsampeln, damit der Viewport-Ersatz immer etwas zeigt.
        if (!HasSamples)
            SampleNow();

        if (!HasSamples)
            return;

        Transform t = MeshTransform;
        Matrix4x4 localToWorld = t.localToWorldMatrix;
        Matrix4x4 normalMatrix = t.worldToLocalMatrix.transpose;

        // Pass 1: Höhenbereich für den Farbverlauf bestimmen
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        if (colorByHeight)
        {
            for (int i = 0; i < localPositions.Length; i++)
            {
                float y = localToWorld.MultiplyPoint3x4(localPositions[i]).y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        float heightRange = Mathf.Max(0.0001f, maxY - minY);

        // Pass 2: zeichnen
        for (int i = 0; i < localPositions.Length; i++)
        {
            Vector3 worldPos = localToWorld.MultiplyPoint3x4(localPositions[i]);

            if (showPoints)
            {
                Gizmos.color = colorByHeight
                    ? Color.Lerp(rootColor, tipColor, (worldPos.y - minY) / heightRange)
                    : pointColor;

                Gizmos.DrawSphere(worldPos, pointSize);
            }

            if (showNormals)
            {
                Gizmos.color = normalColor;
                Vector3 worldNormal = normalMatrix.MultiplyVector(localNormals[i]).normalized;
                Gizmos.DrawLine(worldPos, worldPos + worldNormal * normalLength);
            }
        }
    }

    // ------------------------------------------------------------------
    // Debug / Kontextmenü
    // ------------------------------------------------------------------

    [ContextMenu("Debug: Resample + Stats loggen")]
    private void DebugResampleWithStats()
    {
        ValidateSetup();
        SampleNow();

        if (!HasSamples)
        {
            Debug.LogWarning("[VineSurfaceSampler] Kein Sampling möglich - siehe Warnungen oben.", this);
            return;
        }

        float density = totalWorldArea > 0f ? SampleCount / totalWorldArea : 0f;
        Debug.Log($"[VineSurfaceSampler] {SampleCount} Punkte auf {totalWorldArea:0.00} m² Oberfläche " +
                  $"= {density:0.0} Punkte/m² (Seed {seed}).", this);
    }

    [ContextMenu("Debug: Neuer zufälliger Seed")]
    private void DebugRandomSeed()
    {
        seed = Random.Range(0, 100000);
        SampleNow();
        Debug.Log($"[VineSurfaceSampler] Neuer Seed: {seed}", this);
    }
}
