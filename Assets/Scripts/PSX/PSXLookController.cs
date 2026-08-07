using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// MASTER-Regler für den kompletten PSX-Look. Steuert an EINER Stelle:
///
///   - Screen: Renderauflösung (Render Scale) + Point-Upscaling
///   - Vertex Snapping: globaler Stärke-Multiplikator, Snap-Raster
///     automatisch an die Renderauflösung gekoppelt
///   - Affine Mapping: globaler Stärke-Multiplikator
///   - Farbquantisierung, Dithering, Analog-Rauschen
///
/// Die Materialien behalten ihre individuellen Werte (Boden ohne Affine,
/// Wand mit) - die Multiplikatoren skalieren nur die Gesamtdosis.
///
/// Einmal auf ein GameObject in der Szene legen (z.B. "PSX Look").
/// ERSETZT die alte PSXScreenPixelation-Component - die entfernen,
/// sonst kämpfen zwei Skripte um den Render Scale!
///
/// Ohne diese Component in der Szene: Quantisierung/Rauschen aus,
/// Material-Werte gelten unverändert, Auflösung bleibt Unity-Standard.
/// </summary>
[ExecuteAlways]
public class PSXLookController : MonoBehaviour
{
    [Header("Screen")]
    [Tooltip("Vertikale Renderauflösung in Pixeln. 240 = klassische PS1. " +
             "Die Breite ergibt sich aus dem Fenster-Aspect.")]
    [Range(64, 720)]
    [SerializeField] private int targetVerticalResolution = 240;

    [Tooltip("Point-Upscaling (Nearest-Neighbor) im URP-Asset erzwingen - " +
             "ohne das verschwimmt der Pixel-Look beim Hochskalieren.")]
    [SerializeField] private bool forcePointUpscaling = true;

    [Header("Vertex Snapping (global)")]
    [Tooltip("Multiplikator auf die Snap Strength ALLER Materialien. " +
             "1 = Material-Werte unverändert, 0 = Wackeln global aus, " +
             "0.5 = überall halb so stark.")]
    [Range(0f, 2f)]
    [SerializeField] private float snapStrengthMultiplier = 1f;

    [Tooltip("Snap-Raster der Vertices automatisch an die Renderauflösung oben koppeln " +
             "(überschreibt die Snap Resolution aller Materialien). " +
             "Ein Wert steuert dann Bild UND Wackel-Raster - empfohlen.")]
    [SerializeField] private bool syncSnapToScreenResolution = true;

    [Header("Affine Mapping (global)")]
    [Tooltip("Multiplikator auf die Affine Strength ALLER Materialien. " +
             "1 = Material-Werte unverändert, 0 = Schwimmen global aus.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float affineMultiplier = 1f;

    [Header("Color Quantization")]
    [Tooltip("Farbreduktion an/aus.")]
    [SerializeField] private bool quantizeColors = true;

    [Tooltip("Helligkeitsstufen pro Farbkanal. 32 = echte 15-bit-PS1-Farbtiefe.\n" +
             "Höher = feinere Abstufung = unauffälligeres Dithering (48-64 für dunkle Szenen).")]
    [Range(2, 64)]
    [SerializeField] private int levelsPerChannel = 48;

    [Tooltip("Stärke des Dither-Punktmusters. 1 = volle PS1-Körnung, 0.4-0.6 = dezent.")]
    [Range(0f, 1f)]
    [SerializeField] private float ditherStrength = 0.5f;

    [Header("Analog Noise (Leben im Standbild)")]
    [Tooltip("Dither-Muster pro Tick verschieben - das Raster 'kriecht' subtil, auch im Stillstand.")]
    [SerializeField] private bool animateDither = true;

    [Tooltip("Signalrauschen auf den Farben. 0.05-0.1 = subtil, 0.3+ = kaputter Sender.")]
    [Range(0f, 1f)]
    [SerializeField] private float grainStrength = 0.06f;

    [Tooltip("Tickrate des Rauschens (Frames pro Sekunde). 24 = träges Videorauschen.")]
    [Range(1f, 60f)]
    [SerializeField] private float noiseFps = 24f;

    [Tooltip("Dämpft Dither + Grain in fast-schwarzen Bereichen. Verhindert die " +
             "versprengten Einzelpixel ('Salz-Rauschen') in tiefen Schatten.\n" +
             "0 = ungedämpft (hardware-authentisch), 0.6-0.8 = saubere Schatten.")]
    [Range(0f, 1f)]
    [SerializeField] private float shadowCleanliness = 0.7f;

    // ------------------------------------------------------------------

    private static readonly int MasterActiveId = Shader.PropertyToID("_PSX_MasterActive");
    private static readonly int SnapMulId = Shader.PropertyToID("_PSX_SnapStrengthMul");
    private static readonly int AffineMulId = Shader.PropertyToID("_PSX_AffineMul");
    private static readonly int SnapResOverrideId = Shader.PropertyToID("_PSX_SnapResolutionOverride");
    private static readonly int QuantizeId = Shader.PropertyToID("_PSX_Quantize");
    private static readonly int LevelsId = Shader.PropertyToID("_PSX_ColorLevels");
    private static readonly int DitherId = Shader.PropertyToID("_PSX_DitherStrength");
    private static readonly int AnimateDitherId = Shader.PropertyToID("_PSX_AnimateDither");
    private static readonly int GrainId = Shader.PropertyToID("_PSX_GrainStrength");
    private static readonly int NoiseFpsId = Shader.PropertyToID("_PSX_NoiseFps");
    private static readonly int ShadowCleanId = Shader.PropertyToID("_PSX_ShadowClean");

    private UniversalRenderPipelineAsset cachedAsset;
    private float originalRenderScale = -1f;
    private UpscalingFilterSelection originalUpscalingFilter;
    private bool originalsCaptured;

    // ------------------------------------------------------------------
    // Public API für Laufzeit-Steuerung (Trigger, Timelines, Schock-Momente)
    // ------------------------------------------------------------------

    public int TargetVerticalResolution
    {
        get => targetVerticalResolution;
        set => targetVerticalResolution = Mathf.Clamp(value, 64, 720);
    }

    public float SnapStrengthMultiplier
    {
        get => snapStrengthMultiplier;
        set { snapStrengthMultiplier = Mathf.Clamp(value, 0f, 2f); ApplyGlobals(); }
    }

    public float AffineMultiplier
    {
        get => affineMultiplier;
        set { affineMultiplier = Mathf.Clamp(value, 0f, 1.5f); ApplyGlobals(); }
    }

    public bool QuantizeColors
    {
        get => quantizeColors;
        set { quantizeColors = value; ApplyGlobals(); }
    }

    public int LevelsPerChannel
    {
        get => levelsPerChannel;
        set { levelsPerChannel = Mathf.Clamp(value, 2, 64); ApplyGlobals(); }
    }

    public float DitherStrength
    {
        get => ditherStrength;
        set { ditherStrength = Mathf.Clamp01(value); ApplyGlobals(); }
    }

    public float GrainStrength
    {
        get => grainStrength;
        set { grainStrength = Mathf.Clamp01(value); ApplyGlobals(); }
    }

    public float NoiseFps
    {
        get => noiseFps;
        set { noiseFps = Mathf.Clamp(value, 1f, 60f); ApplyGlobals(); }
    }

    // ------------------------------------------------------------------

    private void OnEnable()
    {
        CaptureOriginals();
        ApplyGlobals();
    }

    private void OnValidate()
    {
        ApplyGlobals();
    }

    private void Update()
    {
        ApplyGlobals();
        ApplyRenderScale();
    }

    private void OnDisable()
    {
        // Neutral stellen: Materialien gelten wieder unverändert,
        // Quantisierung/Rauschen aus, Render Scale zurücksetzen.
        Shader.SetGlobalFloat(MasterActiveId, 0f);
        Shader.SetGlobalFloat(QuantizeId, 0f);
        Shader.SetGlobalFloat(GrainId, 0f);

        RestoreOriginals();
    }

    private void ApplyGlobals()
    {
        Shader.SetGlobalFloat(MasterActiveId, 1f);
        Shader.SetGlobalFloat(SnapMulId, snapStrengthMultiplier);
        Shader.SetGlobalFloat(AffineMulId, affineMultiplier);

        // Snap-Raster an die Renderauflösung koppeln: Breite aus dem
        // aktuellen Fenster-Aspect abgeleitet. x <= 1 deaktiviert den Override.
        if (syncSnapToScreenResolution && Screen.height > 0)
        {
            float aspect = Screen.width / (float)Screen.height;
            Vector4 snapRes = new Vector4(
                Mathf.Round(targetVerticalResolution * aspect),
                targetVerticalResolution, 0f, 0f);
            Shader.SetGlobalVector(SnapResOverrideId, snapRes);
        }
        else
        {
            Shader.SetGlobalVector(SnapResOverrideId, Vector4.zero);
        }

        Shader.SetGlobalFloat(QuantizeId, quantizeColors ? 1f : 0f);
        Shader.SetGlobalFloat(LevelsId, levelsPerChannel);
        Shader.SetGlobalFloat(DitherId, ditherStrength);
        Shader.SetGlobalFloat(AnimateDitherId, animateDither ? 1f : 0f);
        Shader.SetGlobalFloat(GrainId, grainStrength);
        Shader.SetGlobalFloat(NoiseFpsId, noiseFps);
        Shader.SetGlobalFloat(ShadowCleanId, shadowCleanliness);
    }

    // ------------------------------------------------------------------
    // Render Scale (übernommen aus der alten PSXScreenPixelation)
    // ------------------------------------------------------------------

    private UniversalRenderPipelineAsset GetPipelineAsset()
    {
        return GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
    }

    private void ApplyRenderScale()
    {
        UniversalRenderPipelineAsset asset = GetPipelineAsset();
        if (asset == null)
            return;

        if (asset != cachedAsset)
        {
            RestoreOriginals();
            CaptureOriginals();
        }

        float scale = Mathf.Clamp(
            targetVerticalResolution / (float)Mathf.Max(1, Screen.height),
            0.05f, 1f);

        if (!Mathf.Approximately(asset.renderScale, scale))
            asset.renderScale = scale;

        if (forcePointUpscaling && asset.upscalingFilter != UpscalingFilterSelection.Point)
            asset.upscalingFilter = UpscalingFilterSelection.Point;
    }

    private void CaptureOriginals()
    {
        cachedAsset = GetPipelineAsset();

        if (cachedAsset == null)
        {
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
