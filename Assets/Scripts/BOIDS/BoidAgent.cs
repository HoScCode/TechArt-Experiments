// Version: BoidAgent_v001_MS2_Base (unchanged)
using UnityEngine;

[DisallowMultipleComponent]
public class BoidAgent : MonoBehaviour
{
    [SerializeField]
    private Renderer visualRenderer;

    public Renderer VisualRenderer => visualRenderer;

    public int Index { get; internal set; } = -1;

    // Bereits vorbereitet für MS2.
    public float SeparationActivity { get; private set; }
    public float AlignmentActivity { get; private set; }
    public float CohesionActivity { get; private set; }
    public float AvoidanceActivity { get; private set; }
    public float BoundaryActivity { get; private set; }

    private void Reset()
    {
        visualRenderer = GetComponentInChildren<Renderer>();
    }

    private void Awake()
    {
        if (visualRenderer == null)
            visualRenderer = GetComponentInChildren<Renderer>();
    }

    internal void SetActivity(
        float separation,
        float alignment,
        float cohesion,
        float avoidance,
        float boundary)
    {
        SeparationActivity = Mathf.Clamp01(separation);
        AlignmentActivity = Mathf.Clamp01(alignment);
        CohesionActivity = Mathf.Clamp01(cohesion);
        AvoidanceActivity = Mathf.Clamp01(avoidance);
        BoundaryActivity = Mathf.Clamp01(boundary);
    }
}