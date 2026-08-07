using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Erzeugt die dicht unterteilte Kreisscheibe für den Fluffy-Clouds-Shader
/// (ersetzt das komplette Blender-Kapitel aus dem Papush-Tutorial).
/// Topologie wie im Video: Hexagon-Basis mit gleichmäßig triangulierten Ringen,
/// damit das Vertex-Displacement überall gleich fein aufgelöst ist.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CloudDiskMeshBuilder : MonoBehaviour
{
    [Header("Disk")]
    [Tooltip("Radius der Scheibe in Welteinheiten. Video: 1000 (= 2 km Durchmesser).\nKleinere Szene = kleinerer Radius, dann die Distanzwerte im Shader mitskalieren.")]
    [Min(0.1f)]
    [SerializeField] private float radius = 1000f;

    [Tooltip("Ringe von der Mitte bis zum Rand. Dreiecke = 6 x Ringe².\n128 = ca. 98.000 Dreiecke (entspricht den ~100k aus dem Video).")]
    [Range(4, 400)]
    [SerializeField] private int rings = 128;

    [Header("Culling")]
    [Tooltip("Das Displacement passiert rein auf der GPU - die Bounds des flachen Meshes wissen nichts davon. Damit die Scheibe am Bildrand nicht weggecullt wird, werden die Bounds um diesen Wert in Y aufgeblasen.\nFaustregel: mindestens Noise Height + Anhebung durch die Krümmung.")]
    [Min(0f)]
    [SerializeField] private float boundsPaddingY = 300f;

    [Header("Editor")]
    [Tooltip("Mesh bei Inspector-Änderungen automatisch neu bauen.")]
    [SerializeField] private bool rebuildOnValidate = true;

    private Mesh mesh;

    private void OnEnable()
    {
        Build();
    }

    private void OnValidate()
    {
        if (!rebuildOnValidate)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Direkt in OnValidate am Mesh/Renderer herumzubauen gibt Editor-Warnungen
            // -> einen Editor-Tick verzögern.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    Build();
            };
            return;
        }
#endif
        Build();
    }

    private void OnDestroy()
    {
        if (mesh == null)
            return;

        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);
    }

    [ContextMenu("Mesh neu bauen")]
    private void Build()
    {
        if (rings < 1)
            return;

        if (mesh == null)
        {
            // DontSave: das Mesh wird nicht in die Szene serialisiert (kein Szenen-Bloat),
            // sondern beim Laden über OnEnable einfach neu gebaut.
            mesh = new Mesh
            {
                name = "Cloud Disk",
                hideFlags = HideFlags.DontSave
            };
        }

        int vertexCount = 1 + 3 * rings * (rings + 1);

        mesh.Clear();
        mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];

        // Zentrum
        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;
        uvs[0] = new Vector2(0.5f, 0.5f);

        int write = 1;

        for (int r = 1; r <= rings; r++)
        {
            int count = 6 * r;
            float ringRadius = radius * r / rings;

            for (int j = 0; j < count; j++)
            {
                float angle = j / (float)count * Mathf.PI * 2f;

                Vector3 p = new Vector3(
                    Mathf.Cos(angle) * ringRadius,
                    0f,
                    Mathf.Sin(angle) * ringRadius
                );

                vertices[write] = p;
                normals[write] = Vector3.up;
                uvs[write] = new Vector2(p.x, p.z) / (radius * 2f) + new Vector2(0.5f, 0.5f);
                write++;
            }
        }

        int[] triangles = new int[6 * rings * rings * 3];
        int t = 0;

        for (int r = 1; r <= rings; r++)
        {
            int outerStart = StartIndex(r);
            int innerStart = StartIndex(r - 1);
            int outerCount = 6 * r;
            int innerCount = Mathf.Max(1, 6 * (r - 1));

            // 6 Sektoren (Hexagon), pro Sektor r "Auf"- und r-1 "Ab"-Dreiecke
            for (int s = 0; s < 6; s++)
            {
                for (int k = 0; k < r; k++)
                {
                    int o0 = outerStart + (s * r + k) % outerCount;
                    int o1 = outerStart + (s * r + k + 1) % outerCount;
                    int i0 = innerStart + (s * (r - 1) + k) % innerCount;

                    // "Auf"-Dreieck (Spitze zeigt zur Mitte)
                    triangles[t++] = o1;
                    triangles[t++] = o0;
                    triangles[t++] = i0;

                    // "Ab"-Dreieck - eins weniger pro Sektor
                    if (k < r - 1)
                    {
                        int i1 = innerStart + (s * (r - 1) + k + 1) % innerCount;

                        triangles[t++] = o1;
                        triangles[t++] = i0;
                        triangles[t++] = i1;
                    }
                }
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        // Bounds von Hand setzen: die flache Scheibe + GPU-Displacement würde sonst
        // zu früh aus dem Bild gecullt, sobald das Ur-Mesh außerhalb des Frustums liegt.
        mesh.bounds = new Bounds(
            Vector3.zero,
            new Vector3(radius * 2f, boundsPaddingY * 2f, radius * 2f)
        );

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    // Erster Vertex-Index eines Rings: 1 + 6*1 + 6*2 + ... + 6*(ring-1)
    private static int StartIndex(int ring)
    {
        return ring <= 0 ? 0 : 1 + 3 * ring * (ring - 1);
    }

    [ContextMenu("Debug: Mesh-Statistik loggen")]
    private void LogStats()
    {
        Debug.Log(
            $"[CloudDiskMeshBuilder] Ringe: {rings} | Vertices: {1 + 3 * rings * (rings + 1):N0} | " +
            $"Dreiecke: {6 * rings * rings:N0} | Radius: {radius}",
            this
        );
    }

#if UNITY_EDITOR
    [ContextMenu("Mesh als Asset speichern")]
    private void SaveMeshAsset()
    {
        Build();

        string path = UnityEditor.EditorUtility.SaveFilePanelInProject(
            "Cloud Disk speichern",
            "CloudDisk",
            "asset",
            "Speicherort für das Mesh-Asset wählen"
        );

        if (string.IsNullOrEmpty(path))
            return;

        Mesh copy = Instantiate(mesh);
        copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
        copy.hideFlags = HideFlags.None;

        UnityEditor.AssetDatabase.CreateAsset(copy, path);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"[CloudDiskMeshBuilder] Mesh gespeichert: {path}", this);
    }
#endif
}
