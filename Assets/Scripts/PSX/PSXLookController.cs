using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// MASTER-Regler für den kompletten PSX-Look. Steuert an EINER Stelle:
///
///   - Screen: Renderauflösung (Render Scale) + Point-Upscaling
///   - Vertex Snapping: globaler Stärke-Multiplikator, Snap-Raster
///     automatisch an die Renderauflösung gekoppelt
///   - Affine Mapping: globaler Stärke-Multiplikator
///   - Farbquantisierung, Dithering, Analog-Rauschen, Shadow Clean
///
/// SHOWCASE-MODUS (für Breakdown-Videos, z.B. LinkedIn):
///   Baut den Look in Etappen auf - von "Modern Rendering" bis zum vollen
///   VHS-Look. Im Play-Mode mit N (weiter) und B (zurück) durchschalten,
///   optional mit eingeblendetem Etappen-Label für die Zuschauer.
///   Die getunten Look-Werte bleiben dabei unangetastet - der Showcase
///   blendet sie nur schrittweise ein.
///
/// Einmal auf ein GameObject in der Szene legen (z.B. "PSX Look").
/// Ersetzt die alte PSXScreenPixelation-Component.
/// </summary>
[ExecuteAlways]
public class PSXLookController : MonoBehaviour
{
    public enum ShowcaseStage
    {
        ModernRendering = 0,
        LowResolution = 1,
        VertexSnapping = 2,
        AffineTextures = 3,
        ColorQuantization = 4,
        AnalogNoise = 5,
        VhsPostFx = 6
    }

    private static readonly string[] StageLabels =
    {
        "1/7  Modern Rendering",
        "2/7  + Low Resolution",  // Aufloesung wird dynamisch angehaengt
        "3/7  + Vertex Snapping (PS1-Wackeln)",
        "4/7  + Affine Texture Mapping (Schwimmen)",
        "5/7  + Color Quantization & Dithering (15 bit)",
        "6/7  + Analog Noise (lebendiges Standbild)",
        "7/7  + VHS Post FX (Vignette, Wobble, Glitches)"
    };

    private string GetStageLabel(int index)
    {
        if (index == (int)ShowcaseStage.LowResolution)
            return $"{StageLabels[index]} ({targetVerticalResolution}p, Point-Upscaling)";

        return StageLabels[index];
    }

    [Header("Showcase (Breakdown-Video)")]
    [Tooltip("Etappen-Modus für Breakdown-Aufnahmen. Baut den Look Schritt " +
             "für Schritt auf, ohne die getunten Werte unten zu verändern.\n" +
             "Play-Mode-Hotkeys: N = nächste Etappe, B = zurück.")]
    [SerializeField] private bool showcaseMode = false;

    [Tooltip("Aktuelle Etappe. Im Play-Mode auch per N/B durchschaltbar.")]
    [SerializeField] private ShowcaseStage stage = ShowcaseStage.VhsPostFx;

    [Tooltip("Etappen-Name unten links einblenden (für die Zuschauer des Videos).")]
    [SerializeField] private bool showStageLabel = true;

    [Tooltip("Optional: das Material des 'PSX/Post VHS'-Fullscreen-Passes. " +
             "Wird für die letzte Etappe an-/abgeschaltet. Leer = VHS-Etappe " +
             "ändert nichts (Feature dann manuell schalten).")]
    [SerializeField] private Material vhsMaterial;

    [Tooltip("Vertikale Auflösung der 'Modern Rendering'-Etappe. " +
             "0 = native Fensterauflösung (empfohlen, maximal modern). " +
             "z.B. 600 = 800x600-Ära.")]
    [Range(0, 2160)]
    [SerializeField] private int modernVerticalResolution = 0;

    [Tooltip("Übertreibt Snapping, Affine, Dither und Grain WÄHREND des Showcase, " +
             "damit jede neue Etappe auf Kamera deutlich lesbar ist. " +
             "1 = exakt die getunten Werte, 1.3-1.5 = kamerafreundlich deutlich. " +
             "Wirkt nur im Showcase-Modus.")]
    [Range(1f, 2f)]
    [SerializeField] private float showcaseEmphasis = 1.35f;

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
             "1 = Material-Werte unverändert, 0 = Wackeln global aus.")]
    [Range(0f, 2f)]
    [SerializeField] private float snapStrengthMultiplier = 1f;

    [Tooltip("Snap-Raster der Vertices automatisch an die Renderauflösung koppeln " +
             "(überschreibt die Snap Resolution aller Materialien) - empfohlen.")]
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
    private static readonly int TexPixelateOffId = Shader.PropertyToID("_PSX_TexPixelateOff");

    // VHS-Material-Properties, die für frühe Etappen genullt werden
    private static readonly int[] VhsPropIds =
    {
        Shader.PropertyToID("_VignetteStrength"),
        Shader.PropertyToID("_AberrationPixels"),
        Shader.PropertyToID("_WobbleStrength"),
        Shader.PropertyToID("_ScanlineStrength"),
        Shader.PropertyToID("_StripeChance")
    };

    private UniversalRenderPipelineAsset cachedAsset;
    private float originalRenderScale = -1f;
    private UpscalingFilterSelection originalUpscalingFilter;
    private bool originalsCaptured;

    private float[] vhsOriginalValues;
    private bool vhsZeroed;

    // ------------------------------------------------------------------
    // Public API (Trigger, Timelines, UI-Buttons, Schock-Momente)
    // ------------------------------------------------------------------

    public bool ShowcaseMode
    {
        get => showcaseMode;
        set { showcaseMode = value; ApplyGlobals(); }
    }

    public ShowcaseStage Stage
    {
        get => stage;
        set { stage = value; ApplyGlobals(); }
    }

    [ContextMenu("Showcase: Nächste Etappe")]
    public void NextStage()
    {
        stage = (ShowcaseStage)Mathf.Min((int)stage + 1, (int)ShowcaseStage.VhsPostFx);
        ApplyGlobals();
    }

    [ContextMenu("Showcase: Etappe zurück")]
    public void PreviousStage()
    {
        stage = (ShowcaseStage)Mathf.Max((int)stage - 1, 0);
        ApplyGlobals();
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

    public int LevelsPerChannel
    {
        get => levelsPerChannel;
        set { levelsPerChannel = Mathf.Clamp(value, 2, 64); ApplyGlobals(); }
    }

    // ------------------------------------------------------------------

    private bool StageActive(ShowcaseStage required)
    {
        return !showcaseMode || stage >= required;
    }

    private void OnEnable()
    {
        CaptureOriginals();
        ApplyGlobals();
        ApplyRenderScale();
    }

    private void OnValidate()
    {
        ApplyGlobals();
        // Wichtig: auch die Aufloesung sofort anwenden - im Edit-Mode laeuft
        // Update() nur sporadisch, Inspector-Aenderungen kaemen sonst nicht an
        ApplyRenderScale();
    }

    private void Update()
    {
        if (Application.isPlaying && showcaseMode)
            HandleShowcaseHotkeys();

        ApplyGlobals();
        ApplyRenderScale();
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(MasterActiveId, 0f);
        Shader.SetGlobalFloat(QuantizeId, 0f);
        Shader.SetGlobalFloat(GrainId, 0f);

        RestoreVhsMaterial();
        RestoreOriginals();
    }

    private void HandleShowcaseHotkeys()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        if (kb.nKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
            NextStage();
        else if (kb.bKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
            PreviousStage();
#else
        if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.RightArrow))
            NextStage();
        else if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.LeftArrow))
            PreviousStage();
#endif
    }

    private void ApplyGlobals()
    {
        Shader.SetGlobalFloat(MasterActiveId, 1f);

        // Etappen-abhängige Effektiv-Werte: der Showcase blendet die
        // getunten Werte schrittweise ein, verändert sie aber nicht.
        float effSnap = StageActive(ShowcaseStage.VertexSnapping) ? snapStrengthMultiplier : 0f;
        float effAffine = StageActive(ShowcaseStage.AffineTextures) ? affineMultiplier : 0f;
        bool effQuantize = StageActive(ShowcaseStage.ColorQuantization) && quantizeColors;
        bool effAnimate = StageActive(ShowcaseStage.AnalogNoise) && animateDither;
        float effGrain = StageActive(ShowcaseStage.AnalogNoise) ? grainStrength : 0f;
        float effDither = ditherStrength;

        // Emphasis: im Showcase alles etwas lauter drehen, damit jede
        // Etappe auf Kamera sofort lesbar ist
        if (showcaseMode)
        {
            effSnap = Mathf.Clamp(effSnap * showcaseEmphasis, 0f, 2f);
            effAffine = Mathf.Clamp(effAffine * showcaseEmphasis, 0f, 1.5f);
            effDither = Mathf.Clamp01(effDither * showcaseEmphasis);
            effGrain = Mathf.Clamp01(effGrain * showcaseEmphasis);
        }

        // Modern-Etappe: erzwungene Textur-Pixelierung aller Materialien aus,
        // damit "Modern Rendering" wirklich clean aussieht
        bool texPixelateOff = showcaseMode && stage < ShowcaseStage.LowResolution;
        Shader.SetGlobalFloat(TexPixelateOffId, texPixelateOff ? 1f : 0f);

        Shader.SetGlobalFloat(SnapMulId, effSnap);
        Shader.SetGlobalFloat(AffineMulId, effAffine);

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

        Shader.SetGlobalFloat(QuantizeId, effQuantize ? 1f : 0f);
        Shader.SetGlobalFloat(LevelsId, levelsPerChannel);
        Shader.SetGlobalFloat(DitherId, effDither);
        Shader.SetGlobalFloat(AnimateDitherId, effAnimate ? 1f : 0f);
        Shader.SetGlobalFloat(GrainId, effGrain);
        Shader.SetGlobalFloat(NoiseFpsId, noiseFps);
        Shader.SetGlobalFloat(ShadowCleanId, shadowCleanliness);

        ApplyVhsStage();
    }

    // ------------------------------------------------------------------
    // VHS-Material für frühe Etappen stummschalten / wiederherstellen
    // ------------------------------------------------------------------

    private void ApplyVhsStage()
    {
        if (vhsMaterial == null)
            return;

        bool vhsActive = StageActive(ShowcaseStage.VhsPostFx);

        if (!vhsActive && !vhsZeroed)
        {
            // Originalwerte einmalig sichern, dann nullen
            vhsOriginalValues = new float[VhsPropIds.Length];
            for (int i = 0; i < VhsPropIds.Length; i++)
            {
                vhsOriginalValues[i] = vhsMaterial.GetFloat(VhsPropIds[i]);
                vhsMaterial.SetFloat(VhsPropIds[i], 0f);
            }
            vhsZeroed = true;
        }
        else if (vhsActive && vhsZeroed)
        {
            RestoreVhsMaterial();
        }
    }

    private void RestoreVhsMaterial()
    {
        if (!vhsZeroed || vhsMaterial == null || vhsOriginalValues == null)
            return;

        for (int i = 0; i < VhsPropIds.Length; i++)
            vhsMaterial.SetFloat(VhsPropIds[i], vhsOriginalValues[i]);

        vhsZeroed = false;
    }

    // ------------------------------------------------------------------
    // Render Scale
    // ------------------------------------------------------------------

    private UniversalRenderPipelineAsset GetPipelineAsset()
    {
        // Quality-Level kann ein eigenes URP-Asset ueberschreiben - DAS ist
        // dann das aktive. Erst danach auf das Default-Asset zurueckfallen.
        var qualityAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (qualityAsset != null)
            return qualityAsset;

        return GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
    }

    [ContextMenu("Debug: Aktives URP-Asset & Render Scale loggen")]
    private void DebugLogPipelineState()
    {
        UniversalRenderPipelineAsset asset = GetPipelineAsset();

        if (asset == null)
        {
            Debug.LogWarning("[PSXLookController] Kein URP-Asset gefunden!", this);
            return;
        }

        Debug.Log($"[PSXLookController] Aktives Asset: '{asset.name}' | " +
                  $"Render Scale: {asset.renderScale:0.000} | " +
                  $"Upscaling: {asset.upscalingFilter} | " +
                  $"Screen: {Screen.width}x{Screen.height} | " +
                  $"Stage: {stage}", this);
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

        // Etappe 1 (Modern Rendering): hohe/native Auflösung + weiches Upscaling
        bool lowResActive = StageActive(ShowcaseStage.LowResolution);

        float scale;
        if (lowResActive)
        {
            scale = Mathf.Clamp(
                targetVerticalResolution / (float)Mathf.Max(1, Screen.height),
                0.05f, 1f);
        }
        else if (modernVerticalResolution > 0)
        {
            scale = Mathf.Clamp(
                modernVerticalResolution / (float)Mathf.Max(1, Screen.height),
                0.05f, 1f);
        }
        else
        {
            scale = 1f; // native Fensterauflösung
        }

        if (!Mathf.Approximately(asset.renderScale, scale))
            asset.renderScale = scale;

        // Point-Upscaling nur im Retro-Betrieb - die Modern-Etappe skaliert
        // weich (Auto/Linear), sonst sieht selbst hohe Auflösung pixelig aus
        UpscalingFilterSelection wantedFilter = lowResActive && forcePointUpscaling
            ? UpscalingFilterSelection.Point
            : UpscalingFilterSelection.Auto;

        if (asset.upscalingFilter != wantedFilter)
            asset.upscalingFilter = wantedFilter;
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

    // ------------------------------------------------------------------
    // On-Screen-Label für die Aufnahme
    // ------------------------------------------------------------------

    private void OnGUI()
    {
        if (!showcaseMode || !showStageLabel)
            return;

        int index = Mathf.Clamp((int)stage, 0, StageLabels.Length - 1);
        string label = GetStageLabel(index);

        float scale = Screen.height / 1080f;
        int fontSize = Mathf.RoundToInt(34 * scale);
        float pad = 16f * scale;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        Vector2 size = style.CalcSize(new GUIContent(label));
        Rect box = new Rect(pad, Screen.height - size.y - pad * 2f,
                            size.x + pad * 2f, size.y + pad);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = prev;

        GUI.Label(new Rect(box.x + pad, box.y + pad * 0.5f, size.x, size.y), label, style);
    }
}
