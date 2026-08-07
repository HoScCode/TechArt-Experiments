using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Erzwingt den pixeligen PSX-Screen-Look über die URP-Renderauflösung:
/// rendert intern auf z.B. 240 Zeilen Höhe und skaliert per Nearest-Neighbor
/// (Point) hoch - unabhängig von Fensterauflösung und Aspect Ratio.
///
/// Einfach auf ein beliebiges GameObject in der Szene legen (z.B. die Kamera).
/// Beim Deaktivieren werden die ursprünglichen URP-Einstellungen wiederhergestellt.
///
/// Hinweis: Das Script ändert Werte im aktiven URP-Asset. Im Editor werden sie
/// beim Deaktivieren zurückgesetzt - trotzdem am besten ein eigenes URP-Asset
/// für den PSX-Look duplizieren, dann bleibt das Original unberührt.
/// </summary>
[ExecuteAlways]
public class PSXScreenPixelation : MonoBehaviour
{
    [Header("Zielauflösung")]
    [Tooltip("Vertikale Zielauflösung in Pixeln. 240 = klassische PS1 (320x240). " +
             "Die Breite ergibt sich automatisch aus dem Aspect Ratio.")]
    [Range(64, 720)]
    [SerializeField] private int targetVerticalResolution = 240;

    [Tooltip("Point-Upscaling (Nearest-Neighbor) im URP-Asset erzwingen. " +
             "Ohne das verschwimmt das Bild beim Hochskalieren und der Pixel-Look geht verloren.")]
    [SerializeField] private bool forcePointUpscaling = true;

    [Header("Debug")]
    [Tooltip("Aktuellen Render Scale im Play-Mode als Log ausgeben, wenn er sich ändert.")]
    [SerializeField] private bool logScaleChanges = false;

    private UniversalRenderPipelineAsset cachedAsset;
    private float originalRenderScale = -1f;
    private UpscalingFilterSelection originalUpscalingFilter;
    private bool originalsCaptured;
    private float lastAppliedScale = -1f;

    private void OnEnable()
    {
        CaptureOriginals();
    }

    private void OnDisable()
    {
        RestoreOriginals();
    }

    private void Update()
    {
        UniversalRenderPipelineAsset asset = GetPipelineAsset();
        if (asset == null)
            return;

        // Falls das Asset gewechselt hat (Quality-Level etc.), Originale neu erfassen
        if (asset != cachedAsset)
        {
            RestoreOriginals();
            CaptureOriginals();
        }

        float scale = Mathf.Clamp(
            targetVerticalResolution / (float)Mathf.Max(1, Screen.height),
            0.05f, 1f);

        if (!Mathf.Approximately(asset.renderScale, scale))
        {
            asset.renderScale = scale;

            if (logScaleChanges && !Mathf.Approximately(lastAppliedScale, scale))
            {
                Debug.Log($"[PSXScreenPixelation] Render Scale = {scale:0.000} " +
                          $"({Mathf.RoundToInt(Screen.width * scale)}x{Mathf.RoundToInt(Screen.height * scale)})", this);
                lastAppliedScale = scale;
            }
        }

        if (forcePointUpscaling && asset.upscalingFilter != UpscalingFilterSelection.Point)
        {
            asset.upscalingFilter = UpscalingFilterSelection.Point;
        }
    }

    private UniversalRenderPipelineAsset GetPipelineAsset()
    {
        return GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
    }

    private void CaptureOriginals()
    {
        cachedAsset = GetPipelineAsset();

        if (cachedAsset == null)
        {
            Debug.LogWarning("[PSXScreenPixelation] Kein URP-Asset aktiv - das Script hat nichts zu tun.", this);
            originalsCaptured = false;
            return;
        }

        originalRenderScale = cachedAsset.renderScale;
        originalUpscalingFilter = cachedAsset.upscalingFilter;
        originalsCaptured = true;
    }

    private void RestoreOriginals()
    {
        if (!originalsCaptured || cachedAsset == null)
            return;

        cachedAsset.renderScale = originalRenderScale;
        cachedAsset.upscalingFilter = originalUpscalingFilter;
        originalsCaptured = false;
    }
}
