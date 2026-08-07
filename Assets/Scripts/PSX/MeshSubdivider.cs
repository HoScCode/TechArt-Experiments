using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Unterteilt ein Mesh so lange an der jeweils längsten Kante, bis keine Kante
/// mehr über "Max Edge Length" (in Welteinheiten) liegt. Gedacht für dezimierte
/// Photogrammetrie-Meshes mit riesigen, dünnen Dreiecken, auf denen der
/// PSX-Shader sonst extrem verzerrt (Affine Mapping, Vertex-Snapping und
/// Gouraud-Licht arbeiten alle pro Vertex - kleine Dreiecke = kleiner Fehler).
///
/// UVs und Normalen werden an den neuen Vertices korrekt interpoliert.
/// Benachbarte Dreiecke teilen sich die neuen Kanten-Mittelpunkte, es
/// entstehen also keine Risse.
///
/// Nutzung:
/// A) Zur Laufzeit: Component auf das Objekt, "Subdivide On Awake" an. Fertig.
/// B) Im Editor dauerhaft: Rechtsklick auf die Component -> "Subdivide Now (Preview)"
///    zum Testen, dann "Save Subdivided Mesh As Asset" -> erzeugt ein Mesh-Asset,
///    das du normal zuweisen kannst (danach Component entfernen).
///
/// Hinweis NavMesh: Zur Laufzeit unterteilte Meshes ändern nichts an einem
/// bereits gebackenen NavMesh. Wenn der NavMesh vom Render-Mesh baked,
/// Variante B nutzen und vor dem Baken speichern.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class MeshSubdivider : MonoBehaviour
{
    [Header("Subdivision")]
    [Tooltip("Maximale Kantenlänge in WELT-Einheiten (Skalierung des Objekts wird berücksichtigt).\n" +
             "1-2 ist ein guter Start für Straßen/Böden. Kleiner = mehr Dreiecke = weniger Verzerrung.")]
    [Min(0.05f)]
    [SerializeField] private float maxEdgeLength = 1.5f;

    [Tooltip("Mesh beim Start automatisch unterteilen (arbeitet auf einer Instanz, das Original-Asset bleibt unberührt).")]
    [SerializeField] private bool subdivideOnAwake = true;

    [Header("Safety")]
    [Tooltip("Harte Obergrenze für die Vertex-Anzahl, damit ein zu kleiner Schwellwert nicht den Editor einfriert.")]
    [SerializeField] private int vertexLimit = 250000;

    [Tooltip("Ergebnis nach der Unterteilung als Log ausgeben.")]
    [SerializeField] private bool logResult = true;

    private Mesh runtimeMesh;

    private void Awake()
    {
        if (subdivideOnAwake)
            SubdivideNow();
    }

    [ContextMenu("Subdivide Now (Preview)")]
    public void SubdivideNow()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh source = filter.sharedMesh;

        if (source == null)
        {
            Debug.LogWarning("[MeshSubdivider] Kein Mesh im MeshFilter.", this);
            return;
        }

        Mesh result = BuildSubdividedMesh(source);
        if (result == null)
            return;

        // Instanz zuweisen - das Original-Asset bleibt unangetastet
        runtimeMesh = result;
        filter.sharedMesh = result;
    }

    private Mesh BuildSubdividedMesh(Mesh source)
    {
        Vector3[] srcVerts = source.vertices;
        Vector3[] srcNormals = source.normals;
        Vector2[] srcUvs = source.uv;
        int[] srcTris = source.triangles;

        bool hasNormals = srcNormals != null && srcNormals.Length == srcVerts.Length;
        bool hasUvs = srcUvs != null && srcUvs.Length == srcVerts.Length;

        var verts = new List<Vector3>(srcVerts);
        var normals = hasNormals ? new List<Vector3>(srcNormals) : null;
        var uvs = hasUvs ? new List<Vector2>(srcUvs) : null;

        // Skalierung des Objekts einrechnen, damit der Schwellwert in
        // Welteinheiten gilt (Photogrammetrie-Importe sind oft skaliert)
        Vector3 scale = transform.lossyScale;

        // Kanten-Mittelpunkt-Cache: Key = (kleinerer Index, größerer Index).
        // Benachbarte Dreiecke bekommen so denselben neuen Vertex -> keine Risse.
        var midpointCache = new Dictionary<(int, int), int>();

        // Dreiecke als Arbeitsliste; fertige (alle Kanten kurz genug) wandern raus
        var work = new Queue<(int a, int b, int c)>();
        for (int i = 0; i < srcTris.Length; i += 3)
            work.Enqueue((srcTris[i], srcTris[i + 1], srcTris[i + 2]));

        var finished = new List<int>(srcTris.Length * 2);

        float maxSqr = maxEdgeLength * maxEdgeLength;
        bool limitHit = false;

        float ScaledSqrLength(int i0, int i1)
        {
            Vector3 d = Vector3.Scale(verts[i1] - verts[i0], scale);
            return d.sqrMagnitude;
        }

        int GetMidpoint(int i0, int i1)
        {
            var key = i0 < i1 ? (i0, i1) : (i1, i0);
            if (midpointCache.TryGetValue(key, out int cached))
                return cached;

            int index = verts.Count;
            verts.Add((verts[i0] + verts[i1]) * 0.5f);

            if (normals != null)
                normals.Add((normals[i0] + normals[i1]).normalized);

            if (uvs != null)
                uvs.Add((uvs[i0] + uvs[i1]) * 0.5f);

            midpointCache.Add(key, index);
            return index;
        }

        while (work.Count > 0)
        {
            (int a, int b, int c) = work.Dequeue();

            float ab = ScaledSqrLength(a, b);
            float bc = ScaledSqrLength(b, c);
            float ca = ScaledSqrLength(c, a);

            float longest = Mathf.Max(ab, Mathf.Max(bc, ca));

            if (longest <= maxSqr || verts.Count >= vertexLimit)
            {
                if (longest > maxSqr)
                    limitHit = true;

                finished.Add(a);
                finished.Add(b);
                finished.Add(c);
                continue;
            }

            // Längste Kante halbieren, zwei neue Dreiecke zurück in die Warteschlange
            if (longest == ab)
            {
                int m = GetMidpoint(a, b);
                work.Enqueue((a, m, c));
                work.Enqueue((m, b, c));
            }
            else if (longest == bc)
            {
                int m = GetMidpoint(b, c);
                work.Enqueue((a, b, m));
                work.Enqueue((a, m, c));
            }
            else
            {
                int m = GetMidpoint(c, a);
                work.Enqueue((a, b, m));
                work.Enqueue((m, b, c));
            }
        }

        if (limitHit)
        {
            Debug.LogWarning($"[MeshSubdivider] Vertex-Limit ({vertexLimit}) erreicht - " +
                "einige Kanten sind noch länger als der Schwellwert. " +
                "Max Edge Length erhöhen oder Limit anheben.", this);
        }

        Mesh mesh = new Mesh
        {
            name = source.name + "_Subdivided",
            indexFormat = verts.Count > 65534
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };

        mesh.SetVertices(verts);
        if (normals != null) mesh.SetNormals(normals);
        if (uvs != null) mesh.SetUVs(0, uvs);
        mesh.SetTriangles(finished, 0);

        if (normals == null)
            mesh.RecalculateNormals();

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        if (logResult)
        {
            Debug.Log($"[MeshSubdivider] '{source.name}': " +
                      $"{srcVerts.Length} -> {verts.Count} Vertices, " +
                      $"{srcTris.Length / 3} -> {finished.Count / 3} Dreiecke " +
                      $"(Max Edge {maxEdgeLength}).", this);
        }

        return mesh;
    }

    private void OnDestroy()
    {
        // Zur Laufzeit erzeugte Mesh-Instanz aufräumen
        if (runtimeMesh != null && Application.isPlaying)
            Destroy(runtimeMesh);
    }

#if UNITY_EDITOR
    [ContextMenu("Save Subdivided Mesh As Asset")]
    private void SaveAsAsset()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh source = filter.sharedMesh;

        if (source == null)
        {
            Debug.LogWarning("[MeshSubdivider] Kein Mesh im MeshFilter.", this);
            return;
        }

        Mesh result = BuildSubdividedMesh(source);
        if (result == null)
            return;

        string path = EditorUtility.SaveFilePanelInProject(
            "Subdivided Mesh speichern",
            result.name,
            "asset",
            "Speicherort für das unterteilte Mesh wählen");

        if (string.IsNullOrEmpty(path))
            return;

        AssetDatabase.CreateAsset(result, path);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(filter, "Assign Subdivided Mesh");
        filter.sharedMesh = result;

        Debug.Log($"[MeshSubdivider] Mesh gespeichert unter {path} und zugewiesen. " +
                  "Die Component kann jetzt entfernt werden.", this);
    }
#endif
}
