// Version: AquariumVolume_v004_Clean
//
// Kompletter Neuaufbau, passend zu BoidSchool_v100.
// Statt drei Boundary-Subsystemen (Approach-Raycast, Druckzone, Failsafe mit
// eigener Logik) gibt es genau EIN weiches Containment-Feld:
//
//   GetContainment(position, margin) -> Stärke 0..1 + Richtung nach innen
//
// Die Stärke wächst linear über die Randzone (0 am Zonenanfang, 1 an der
// Wand), Ecken ergeben automatisch diagonale Innenrichtungen. Was der Schwarm
// daraus macht, entscheidet allein BoidSchool.
//
// WICHTIG beim Ersetzen: Dateiinhalt ersetzen, Datei/.meta NICHT löschen,
// sonst verliert die Szene die Referenz.

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class AquariumVolume : MonoBehaviour
{
    [SerializeField] private BoxCollider volumeCollider;

    public BoxCollider VolumeCollider => volumeCollider;
    public Bounds WorldBounds => volumeCollider.bounds;

    private void Reset()
    {
        volumeCollider = GetComponent<BoxCollider>();
        volumeCollider.isTrigger = true;
    }

    private void Awake()
    {
        if (volumeCollider == null)
            volumeCollider = GetComponent<BoxCollider>();
    }

    /// <summary>
    /// Zufälliger Punkt im Volumen, mit Wandabstand (World Units).
    /// </summary>
    public Vector3 GetRandomPoint(System.Random random, float worldPadding)
    {
        Vector3 center = volumeCollider.center;
        Vector3 halfSize = volumeCollider.size * 0.5f;
        Vector3 padding = WorldToLocalPadding(worldPadding);

        // Padding pro Achse deckeln, falls es größer als die Box ist.
        padding.x = Mathf.Min(padding.x, halfSize.x * 0.95f);
        padding.y = Mathf.Min(padding.y, halfSize.y * 0.95f);
        padding.z = Mathf.Min(padding.z, halfSize.z * 0.95f);

        Vector3 min = center - halfSize + padding;
        Vector3 max = center + halfSize - padding;

        Vector3 local = new Vector3(
            Mathf.Lerp(min.x, max.x, (float)random.NextDouble()),
            Mathf.Lerp(min.y, max.y, (float)random.NextDouble()),
            Mathf.Lerp(min.z, max.z, (float)random.NextDouble()));

        return transform.TransformPoint(local);
    }

    /// <summary>
    /// Weiches Containment-Feld.
    /// Rückgabe: Stärke 0..1 (0 = außerhalb der Randzone, 1 = an/hinter der
    /// Wand). inwardDirection zeigt normalisiert ins Beckeninnere; in Ecken
    /// diagonal. Bei Stärke 0 ist die Richtung Vector3.zero.
    /// </summary>
    public float GetContainment(
        Vector3 worldPosition,
        float worldMargin,
        out Vector3 inwardDirection)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);

        Vector3 center = volumeCollider.center;
        Vector3 halfSize = volumeCollider.size * 0.5f;

        Vector3 min = center - halfSize;
        Vector3 max = center + halfSize;

        Vector3 margin = WorldToLocalPadding(worldMargin);
        margin.x = Mathf.Min(margin.x, halfSize.x * 0.95f);
        margin.y = Mathf.Min(margin.y, halfSize.y * 0.95f);
        margin.z = Mathf.Min(margin.z, halfSize.z * 0.95f);

        Vector3 push = new Vector3(
            AxisPush(local.x, min.x, max.x, margin.x),
            AxisPush(local.y, min.y, max.y, margin.y),
            AxisPush(local.z, min.z, max.z, margin.z));

        float magnitude = push.magnitude;

        if (magnitude < 0.0001f)
        {
            inwardDirection = Vector3.zero;
            return 0f;
        }

        inwardDirection =
            transform.TransformDirection(push / magnitude).normalized;

        return Mathf.Clamp01(magnitude);
    }

    /// <summary>
    /// Harter Failsafe: Position zurück in die (gepaddete) Box holen.
    /// Sollte im Normalbetrieb praktisch nie feuern.
    /// </summary>
    public bool ClampInside(
        ref Vector3 worldPosition,
        float worldPadding,
        out Vector3 inwardNormal)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);

        Vector3 center = volumeCollider.center;
        Vector3 halfSize = volumeCollider.size * 0.5f;
        Vector3 padding = WorldToLocalPadding(worldPadding);

        Vector3 min = center - halfSize + padding;
        Vector3 max = center + halfSize - padding;

        Vector3 clamped = new Vector3(
            Mathf.Clamp(local.x, min.x, max.x),
            Mathf.Clamp(local.y, min.y, max.y),
            Mathf.Clamp(local.z, min.z, max.z));

        Vector3 correction = clamped - local;

        if (correction.sqrMagnitude < 0.000001f)
        {
            inwardNormal = Vector3.zero;
            return false;
        }

        inwardNormal =
            transform.TransformDirection(correction.normalized).normalized;

        worldPosition = transform.TransformPoint(clamped);
        return true;
    }

    // 0 außerhalb der Randzone, wächst linear auf 1 an der Wand,
    // darf hinter der Wand leicht über 1 hinaus (wird oben geclampt).
    private static float AxisPush(float value, float min, float max, float margin)
    {
        if (margin <= 0.0001f)
            return 0f;

        float force = 0f;

        float distanceFromMin = value - min;
        if (distanceFromMin < margin)
            force += Mathf.Clamp(1f - distanceFromMin / margin, 0f, 1.5f);

        float distanceFromMax = max - value;
        if (distanceFromMax < margin)
            force -= Mathf.Clamp(1f - distanceFromMax / margin, 0f, 1.5f);

        return force;
    }

    private Vector3 WorldToLocalPadding(float worldPadding)
    {
        Vector3 scale = transform.lossyScale;

        return new Vector3(
            worldPadding / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            worldPadding / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            worldPadding / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
    }

#if UNITY_EDITOR
    [Header("Gizmo")]
    [Tooltip("Zeigt zusätzlich die innere Randzonen-Box für diesen Margin-Wert (rein visuell, muss manuell mit dem BoidSchool-Wert synchron gehalten werden).")]
    [SerializeField, Min(0f)] private float gizmoPreviewMargin = 4f;

    private void OnDrawGizmosSelected()
    {
        if (volumeCollider == null)
            volumeCollider = GetComponent<BoxCollider>();

        if (volumeCollider == null)
            return;

        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireCube(volumeCollider.center, volumeCollider.size);

        if (gizmoPreviewMargin > 0f)
        {
            Vector3 halfSize = volumeCollider.size * 0.5f;
            Vector3 margin = WorldToLocalPadding(gizmoPreviewMargin);

            margin.x = Mathf.Min(margin.x, halfSize.x * 0.95f);
            margin.y = Mathf.Min(margin.y, halfSize.y * 0.95f);
            margin.z = Mathf.Min(margin.z, halfSize.z * 0.95f);

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(
                volumeCollider.center,
                volumeCollider.size - margin * 2f);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
