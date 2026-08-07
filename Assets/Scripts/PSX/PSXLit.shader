// PSX Lit Shader für Unity 6 / URP - mit dynamischer Distanz-Tessellation
//
// Emuliert die Kern-Merkmale der PS1-Grafik:
//   1. Vertex-Snapping   -> Geometrie "wackelt" auf einem virtuellen Pixelraster
//   2. Affine Texturen   -> Texturen "schwimmen" (mit Distanz-Fade gegen
//                           extreme Verzerrung bei nahen, grossen Flaechen)
//   3. Farbreduktion     -> Quantisierung auf wenige Farbstufen + Dithering
//   4. Gouraud-Licht     -> Beleuchtung pro Vertex, wie auf der PS1
//
// NEU - Distanz-Tessellation:
// Die GPU unterteilt Dreiecke zur Laufzeit, je naeher die Kamera kommt.
// Dadurch werden vertex-basierte Effekte (Taschenlampen-Kegel, Affine,
// Snapping) in Kameranaehe fein aufgeloest, waehrend die Ferne beim
// Original-Polycount bleibt. Kein Speicher-Overhead, kein Popping
// (fractional partitioning = stufenloser Uebergang).
//
// Wichtig: Tessellation kann nur VERFEINERN, was da ist - ein einzelnes
// 20-Meter-Dreieck bekommt maximal Faktor 64. Fuer extreme Photogrammetrie-
// Dreiecke daher weiterhin den MeshSubdivider grob drueberlaufen lassen
// (z.B. Max Edge 2-3), die Tessellation macht dann den Rest in Kameranaehe.
//
// Benoetigt GPU-Tessellation (Shader Model 4.6+): Desktop ok, WebGL nicht.
//
// Empfohlene Textur-Importe: Filter Mode = Point, Mip Maps aus.

Shader "PSX/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Vertex Snapping)]
        [Space(4)]
        // Virtuelle Zielaufloesung, auf deren Raster die Vertices gerundet werden.
        // 320x240 = klassische PS1. Kleiner = mehr Wackeln.
        _SnapResolution ("Snap Resolution (X Y)", Vector) = (320, 240, 0, 0)
        [Range(0,1)] _SnapStrength ("Snap Strength", Range(0, 1)) = 1

        [Header(Affine Texture Mapping)]
        [Space(4)]
        // 1 = volles PS1-Schwimmen, 0 = moderne perspektivische Korrektur.
        [Range(0,1)] _AffineStrength ("Affine Strength", Range(0, 1)) = 1
        // Distanz-Fade gegen die extreme Kruemmung bei nahen, grossen Flaechen:
        // Unterhalb von Fade Start ist die Textur perspektivisch korrekt,
        // ab Fade End wirkt die volle Affine Strength. Dazwischen wird geblendet.
        // Start = End = 0 -> Fade aus, Affine wirkt ueberall.
        _AffineFadeStart ("Affine Fade Start (Distanz)", Float) = 1.5
        _AffineFadeEnd ("Affine Fade End (Distanz)", Float) = 6

        [Header(Distance Tessellation)]
        [Space(4)]
        // GPU-Unterteilung in Kameranaehe. Macht Taschenlampen-Kegel rund
        // und Affine/Snapping feiner, ohne das Mesh dauerhaft zu vergroessern.
        [Toggle] _TessEnabled ("Distance Tessellation", Float) = 1
        // Ziel-Kantenlaenge (Welteinheiten) direkt vor der Kamera.
        // 0.3-0.5 = schoene runde Lichtkegel.
        _TessTargetEdge ("Target Edge Length (nah)", Range(0.05, 2)) = 0.4
        // Bis zu dieser Distanz wird mit voller Dichte tesselliert...
        _TessFadeStart ("Tess Fade Start (Distanz)", Float) = 4
        // ...ab dieser Distanz gar nicht mehr (Faktor 1 = Original-Mesh).
        _TessFadeEnd ("Tess Fade End (Distanz)", Float) = 15
        // Obergrenze pro Kante (Hardware-Limit 64). Schuetzt vor Explosionen
        // bei sehr langen Kanten.
        _TessMaxFactor ("Max Factor", Range(1, 64)) = 24

        [Header(Texture Pixelation)]
        [Space(4)]
        // Erzwingt eine niedrige Texturaufloesung direkt im Shader.
        [Toggle] _TexPixelate ("Pixelate Texture", Float) = 0
        _TexPixelateRes ("Forced Texture Resolution", Vector) = (64, 64, 0, 0)

        // HINWEIS: Farbquantisierung, Dithering und Analog-Rauschen sind KEINE
        // Material-Properties mehr, sondern globale Werte - zentral gesteuert
        // ueber die PSXLookController-Component (einmal in die Szene legen).
        // Ein Regler wirkt auf alle PSX-Materialien inkl. Skybox gleichzeitig.

        [Header(Lighting)]
        [Space(4)]
        // Per-Vertex-Beleuchtung (Gouraud). 0 = unlit.
        [Toggle] _VertexLighting ("Vertex Lighting", Float) = 1
        // Zusatzlichter (Spot-/Punktlichter) pro PIXEL statt pro Vertex rechnen.
        // An = weicher, runder Taschenlampen-Kegel (modern, weniger authentisch).
        // Aus = PS1-Verhalten: Lichtform folgt der Dreiecks-Aufloesung
        //       (mit Tessellation in Kameranaehe trotzdem schoen rund).
        [Toggle] _PixelAdditionalLights ("Per Pixel Additional Lights", Float) = 0
        // Hebt die Grundhelligkeit an, falls die Szene zu dunkel wird.
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 0

        [Header(Shadows)]
        [Space(4)]
        // Schatten des Main Lights empfangen (per Pixel, wirkt nur auf den
        // direkten Lichtanteil - Ambient bleibt erhalten).
        // 0 = authentisches PS1-Verhalten ohne Echtzeitschatten.
        [Toggle] _ReceiveShadows ("Receive Shadows", Float) = 1

        [Header(Alpha)]
        [Space(4)]
        [Toggle] _AlphaClip ("Alpha Clip (Cutout)", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex TessVert
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _SnapResolution;
                float _SnapStrength;
                float _AffineStrength;
                float _AffineFadeStart;
                float _AffineFadeEnd;
                float _TessEnabled;
                float _TessTargetEdge;
                float _TessFadeStart;
                float _TessFadeEnd;
                float _TessMaxFactor;
                float _TexPixelate;
                float4 _TexPixelateRes;
                float _VertexLighting;
                float _PixelAdditionalLights;
                float _AmbientBoost;
                float _ReceiveShadows;
                float _AlphaClip;
                float _Cutoff;
            CBUFFER_END

            // Globale Werte - fuer ALLE PSX-Materialien identisch, gesetzt
            // von der PSXLookController-Component (Shader.SetGlobalFloat).
            float _PSX_Quantize;
            float _PSX_ColorLevels;
            float _PSX_DitherStrength;
            float _PSX_AnimateDither;
            float _PSX_GrainStrength;
            float _PSX_NoiseFps;
            // Daempft Dither + Grain in fast-schwarzen Bereichen, damit dort
            // keine versprengten Einzelpixel "funkeln" (0 = ungedaempft).
            float _PSX_ShadowClean;

            // Master-Steuerung: 1 = Controller aktiv in der Szene.
            // Bei 0 werden alle Multiplikatoren/Overrides ignoriert und die
            // Material-Werte gelten unveraendert.
            float _PSX_MasterActive;
            // Globaler Multiplikator auf die Snap Strength aller Materialien.
            float _PSX_SnapStrengthMul;
            // Globaler Multiplikator auf die Affine Strength aller Materialien.
            float _PSX_AffineMul;
            // Ersetzt die Snap Resolution aller Materialien (xy).
            // x <= 1 = aus, Material-Wert gilt.
            float4 _PSX_SnapResolutionOverride;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            // Kontrollpunkt zwischen Vertex-, Hull- und Domain-Stage:
            // alles in Weltkoordinaten, damit die Domain-Stage neue Vertices
            // einfach baryzentrisch interpolieren kann.
            struct TessControlPoint
            {
                float3 positionWS : INTERNALTESSPOS;
                float3 normalWS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct TessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // xy = uv * w, z = w  -> ergibt im Fragment die affine Interpolation
                float3 uvAffine   : TEXCOORD0;
                // Normale (perspektivisch korrekte) UV als Blend-Gegenstueck
                float2 uvCorrect  : TEXCOORD1;
                // Ambient (SH) + Additional Lights, per Vertex - bleibt von
                // Schatten unberuehrt, damit Schattenbereiche nicht schwarz werden
                half3 lighting    : TEXCOORD2;
                float fogFactor   : TEXCOORD3;
                // Pro Vertex berechneter Affine-Anteil (0 = korrekt, 1 = affin),
                // faellt mit der Naehe zur Kamera ab
                float affineBlend : TEXCOORD4;
                // Direkter Main-Light-Anteil, wird im Fragment mit dem
                // Schattenwert multipliziert
                half3 mainLighting : TEXCOORD5;
                float3 positionWS  : TEXCOORD6;
                // Fuer den optionalen Per-Pixel-Modus der Zusatzlichter
                half3 normalWS     : TEXCOORD7;
            };

            // ------------------------------------------------------------------
            // Vertex-Snapping: Position in NDC umrechnen, auf das virtuelle
            // Pixelraster runden, zurueckrechnen. Nur XY snappen, Z bleibt.
            // ------------------------------------------------------------------
            float4 SnapToPixelGrid(float4 positionCS)
            {
                // Master-Multiplikator (nur wenn Controller aktiv)
                float strength = _SnapStrength *
                    lerp(1.0, _PSX_SnapStrengthMul, saturate(_PSX_MasterActive));

                if (strength <= 0.0)
                    return positionCS;

                // Resolution-Override vom Controller (z.B. an die
                // Renderaufloesung gekoppelt), sonst Material-Wert
                float2 resolution = _SnapResolution.xy;
                if (_PSX_MasterActive > 0.5 && _PSX_SnapResolutionOverride.x > 1.0)
                    resolution = _PSX_SnapResolutionOverride.xy;

                float3 ndc = positionCS.xyz / positionCS.w;

                // NDC deckt -1..1 ab, also halbe Aufloesung als Rastergroesse
                float2 grid = max(float2(2.0, 2.0), resolution) * 0.5;
                float2 snapped = floor(ndc.xy * grid + 0.5) / grid;

                ndc.xy = lerp(ndc.xy, snapped, saturate(strength));

                positionCS.xyz = ndc * positionCS.w;
                return positionCS;
            }

            // ------------------------------------------------------------------
            // Gouraud-Beleuchtung, aufgeteilt in zwei Anteile:
            //   baseLighting = Ambient (SH) + Additional Lights
            //   mainLighting = direkter Main-Light-Anteil (bekommt die Schatten)
            // ------------------------------------------------------------------
            void ComputeVertexLighting(float3 positionWS, half3 normalWS,
                                       out half3 baseLighting, out half3 mainLighting)
            {
                if (_VertexLighting <= 0.5)
                {
                    baseLighting = half3(1, 1, 1);
                    mainLighting = half3(0, 0, 0);
                    return;
                }

                baseLighting = SampleSH(normalWS) + _AmbientBoost.xxx;

                Light mainLight = GetMainLight();
                mainLighting = mainLight.color * saturate(dot(normalWS, mainLight.direction));

                // Im Per-Pixel-Modus laufen die Zusatzlichter im Fragment-Shader
                if (_PixelAdditionalLights > 0.5)
                    return;

                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light light = GetAdditionalLight(i, positionWS);
                    half ndotl = saturate(dot(normalWS, light.direction));
                    baseLighting += light.color * light.distanceAttenuation * ndotl;
                }
            }

            // ------------------------------------------------------------------
            // Baut die Interpolatoren fuer einen (ggf. von der Tessellation neu
            // erzeugten) Vertex in Weltkoordinaten. Wird von der Domain-Stage
            // fuer jeden finalen Vertex aufgerufen.
            // ------------------------------------------------------------------
            Varyings BuildVaryings(float3 positionWS, half3 normalWS, float2 uvRaw)
            {
                Varyings output;

                float4 positionCS = TransformWorldToHClip(positionWS);
                positionCS = SnapToPixelGrid(positionCS);
                output.positionCS = positionCS;

                float2 uv = TRANSFORM_TEX(uvRaw, _BaseMap);
                output.uvCorrect = uv;
                // Affiner Trick: uv * w interpolieren und im Fragment durch das
                // interpolierte w teilen -> screen-space-lineare Interpolation
                output.uvAffine = float3(uv * positionCS.w, positionCS.w);

                // Distanz-Fade: nahe Vertices bekommen weniger Affine-Verzerrung,
                // damit grosse Flaechen direkt vor der Kamera nicht extrem kruemmen.
                // Der Master-Multiplikator skaliert alle Materialien gemeinsam.
                float affine = saturate(_AffineStrength *
                    lerp(1.0, _PSX_AffineMul, saturate(_PSX_MasterActive)));
                if (_AffineFadeEnd > _AffineFadeStart)
                {
                    float viewDistance = distance(_WorldSpaceCameraPos, positionWS);
                    float fade = saturate((viewDistance - _AffineFadeStart) /
                                          max(_AffineFadeEnd - _AffineFadeStart, 0.001));
                    affine *= fade;
                }
                output.affineBlend = affine;

                half3 baseLighting;
                half3 mainLighting;
                ComputeVertexLighting(positionWS, normalWS, baseLighting, mainLighting);

                output.lighting = baseLighting;
                output.mainLighting = mainLighting;
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.fogFactor = ComputeFogFactor(positionCS.z);

                return output;
            }

            // ------------------------------------------------------------------
            // Tessellation-Pipeline
            // ------------------------------------------------------------------

            // Vertex-Stage: nur nach Weltkoordinaten transformieren und an die
            // Hull-Stage durchreichen. Snapping/Licht passieren erst NACH der
            // Unterteilung in der Domain-Stage.
            TessControlPoint TessVert(Attributes input)
            {
                TessControlPoint output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            // Unterteilungsfaktor pro Kante: Kantenlaenge / Ziel-Kantenlaenge,
            // gedaempft ueber die Distanz zur Kamera. Beide Nachbar-Dreiecke
            // einer Kante berechnen denselben Wert -> keine Risse.
            float EdgeTessFactor(float3 a, float3 b)
            {
                if (_TessEnabled < 0.5)
                    return 1.0;

                float3 mid = (a + b) * 0.5;
                float dist = distance(mid, _WorldSpaceCameraPos);

                float falloff = 1.0 - saturate(
                    (dist - _TessFadeStart) /
                    max(_TessFadeEnd - _TessFadeStart, 0.001));

                if (falloff <= 0.0)
                    return 1.0;

                float factor = (distance(a, b) / max(_TessTargetEdge, 0.01)) * falloff;

                return clamp(factor, 1.0, _TessMaxFactor);
            }

            TessFactors PatchConstants(InputPatch<TessControlPoint, 3> patch)
            {
                TessFactors f;

                // Kante i liegt dem Kontrollpunkt i gegenueber
                f.edge[0] = EdgeTessFactor(patch[1].positionWS, patch[2].positionWS);
                f.edge[1] = EdgeTessFactor(patch[2].positionWS, patch[0].positionWS);
                f.edge[2] = EdgeTessFactor(patch[0].positionWS, patch[1].positionWS);
                // Maximum statt Durchschnitt: lange, duenne Photogrammetrie-
                // Dreiecke bekommen so auch INNEN genug Dichte, statt in
                // schmale Streifen zu zerfallen
                f.inside = max(f.edge[0], max(f.edge[1], f.edge[2]));

                return f;
            }

            [domain("tri")]
            [outputcontrolpoints(3)]
            [outputtopology("triangle_cw")]
            // fractional_odd = stufenlose Uebergaenge beim Annaehern (kein Popping)
            [partitioning("fractional_odd")]
            [patchconstantfunc("PatchConstants")]
            TessControlPoint Hull(InputPatch<TessControlPoint, 3> patch,
                                  uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [domain("tri")]
            Varyings Domain(TessFactors factors,
                            OutputPatch<TessControlPoint, 3> patch,
                            float3 bary : SV_DomainLocation)
            {
                float3 positionWS =
                    patch[0].positionWS * bary.x +
                    patch[1].positionWS * bary.y +
                    patch[2].positionWS * bary.z;

                float3 normalWS = normalize(
                    patch[0].normalWS * bary.x +
                    patch[1].normalWS * bary.y +
                    patch[2].normalWS * bary.z);

                float2 uv =
                    patch[0].uv * bary.x +
                    patch[1].uv * bary.y +
                    patch[2].uv * bary.z;

                return BuildVaryings(positionWS, (half3)normalWS, uv);
            }

            // ------------------------------------------------------------------
            // Fragment
            // ------------------------------------------------------------------

            // 4x4-Bayer-Matrix fuer Ordered Dithering
            static const float BAYER_4x4[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            float DitherOffset(float2 pixelPos)
            {
                int2 p = int2(fmod(pixelPos, 4.0));
                return (BAYER_4x4[p.y * 4 + p.x] + 0.5) / 16.0 - 0.5;
            }

            // Billiges Hash-Rauschen pro Pixel (fuer das Analog-Grain)
            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // --- Affine UVs ---
                float2 uvAffine = input.uvAffine.xy / max(input.uvAffine.z, 0.0001);
                float2 uv = lerp(input.uvCorrect, uvAffine, saturate(input.affineBlend));

                // --- Optionale Textur-Pixelierung im Shader ---
                if (_TexPixelate > 0.5)
                {
                    float2 res = max(float2(1, 1), _TexPixelateRes.xy);
                    uv = (floor(uv * res) + 0.5) / res;
                }

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                half4 color = tex * _BaseColor;

                if (_AlphaClip > 0.5)
                    clip(color.a - _Cutoff);

                // --- Schatten (nur auf den direkten Main-Light-Anteil) ---
                half3 mainContribution = input.mainLighting;

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                if (_ReceiveShadows > 0.5)
                {
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    mainContribution *= MainLightRealtimeShadow(shadowCoord);
                }
                #endif

                color.rgb *= input.lighting + mainContribution;

                // --- Optionale Per-Pixel-Zusatzlichter (weicher Spot-Kegel) ---
                // Nur HIER koennen Punkt-/Spotlichter auch Schatten empfangen -
                // der Vertex-Pfad (Gouraud) kann das prinzipbedingt nicht.
                if (_PixelAdditionalLights > 0.5 && _VertexLighting > 0.5)
                {
                    half3 normalWS = normalize(input.normalWS);
                    half3 additional = half3(0, 0, 0);

                    uint count = GetAdditionalLightsCount();
                    for (uint li = 0u; li < count; li++)
                    {
                        // 3-Argument-Variante fuellt light.shadowAttenuation
                        // aus dem Additional-Light-Schatten-Atlas
                        Light light = GetAdditionalLight(li, input.positionWS, half4(1, 1, 1, 1));
                        half ndotl = saturate(dot(normalWS, light.direction));
                        additional += light.color * light.distanceAttenuation
                                    * light.shadowAttenuation * ndotl;
                    }

                    color.rgb += tex.rgb * _BaseColor.rgb * additional;
                }

                // --- Analog-Rauschen: frame-gestufter Zeittakt (wie Videorauschen) ---
                float noiseFrame = floor(_Time.y * max(1.0, _PSX_NoiseFps));

                // Shadow Clean: in fast-schwarzen Bereichen Dither + Grain
                // zuruecknehmen - sonst kippen dort nur vereinzelte Pixel ueber
                // die naechste Farbstufe und es entsteht "Salz-Rauschen"
                // statt eines Musters.
                float luma = dot(color.rgb, half3(0.299, 0.587, 0.114));
                float darkDamp = lerp(1.0, smoothstep(0.0, 0.12, luma),
                                      saturate(_PSX_ShadowClean));

                // Feines Signalrauschen VOR der Quantisierung: die Koernung
                // interagiert mit den Farbstufen und laesst Banding-Kanten
                // subtil flirren - auch im Standbild.
                if (_PSX_GrainStrength > 0.001)
                {
                    float grain = Hash21(input.positionCS.xy + noiseFrame * 17.13) * 2.0 - 1.0;
                    color.rgb += grain * _PSX_GrainStrength * 0.05 * darkDamp;
                }

                // --- Farbquantisierung + Dithering (15-bit-Look) ---
                if (_PSX_Quantize > 0.5)
                {
                    float levels = max(2.0, _PSX_ColorLevels) - 1.0;

                    // Dither-Muster pro Tick verschieben -> das Punktraster
                    // "kriecht", statt wie eingefroren auf dem Bild zu kleben
                    float2 ditherPos = input.positionCS.xy;
                    if (_PSX_AnimateDither > 0.5)
                    {
                        ditherPos += floor(float2(
                            frac(noiseFrame * 0.7548777) * 4.0,
                            frac(noiseFrame * 0.5698403) * 4.0));
                    }

                    float dither = DitherOffset(ditherPos) * _PSX_DitherStrength * darkDamp;
                    color.rgb = floor(color.rgb * levels + 0.5 + dither) / levels;
                }

                color.rgb = saturate(color.rgb);

                color.rgb = MixFog(color.rgb, input.fogFactor);

                return color;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // ShadowCaster - damit die Objekte Schatten werfen koennen.
        // Bewusst OHNE Tessellation: der Schatten des Grob-Meshes reicht.
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DepthOnly - fuer Depth Texture / Depth Priming der URP.
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
