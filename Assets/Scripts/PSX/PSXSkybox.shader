// PSX Skybox (Gradient oder Panorama-Textur)
// Himmel im Stil der PS1-Aera. Zwei Modi:
//   A) Gradient: drei Farben (Oben / Horizont / Unten), keine Textur.
//   B) Panorama: equirektangulaere Textur (2:1, Horizont in der Bildmitte),
//      z.B. eine 4096x2048-Skybox.
//
// In BEIDEN Modi kann der Horizont automatisch die Fog-Farbe aus den
// Lighting Settings uebernehmen ("Use Fog Color For Horizon"). Dadurch
// verschmelzen Nebel, Himmel und Geometrie-Luecken zu einer Flaeche -
// Polygon-Gaps vor dem Himmel sind dann praktisch unsichtbar
// (der Silent-Hill-Ansatz). "Fog Horizon Height/Sharpness" steuern, wie
// weit der Nebelstreifen ins Panorama hochzieht.
//
// Dazu optional: Textur-Pixelierung, Farbquantisierung und Dithering,
// damit der Himmel zur Geometrie mit dem PSX/Lit-Shader passt.
//
// Setup: Material mit diesem Shader erstellen ->
// Window > Rendering > Lighting > Environment > Skybox Material zuweisen.
// Textur-Import: Texture Shape = 2D reicht (kein Cubemap noetig),
// sRGB an, Wrap Mode Repeat.

Shader "PSX/Skybox"
{
    Properties
    {
        [Header(Mode)]
        // 0 = reiner Farbverlauf, 1 = Panorama-Textur
        [Toggle] _UseTexture ("Use Panorama Texture", Float) = 1

        [Header(Panorama Texture)]
        [NoScaleOffset] _MainTex ("Panorama (equirekt. 2:1)", 2D) = "grey" {}
        // Dreht das Panorama horizontal (Grad) - Sonne/Wolken ausrichten.
        _Rotation ("Rotation", Range(0, 360)) = 0
        // Helligkeit der Textur. <1 = abdunkeln (empfohlen fuer den PSX-Look,
        // damit der Foto-Himmel nicht "zu gut" neben der Geometrie aussieht).
        _Exposure ("Exposure", Range(0, 2)) = 1
        // Faerbt die Textur ein - entsaettigen Richtung Fog-Farbe hilft ebenfalls.
        _Tint ("Tint", Color) = (1, 1, 1, 1)

        [Header(Gradient Colors (nur ohne Textur))]
        _TopColor ("Top Color (Zenit)", Color) = (0.18, 0.22, 0.32, 1)
        _HorizonColor ("Horizon Color", Color) = (0.45, 0.43, 0.48, 1)
        _BottomColor ("Bottom Color (unter Horizont)", Color) = (0.25, 0.24, 0.27, 1)
        _HorizonFalloff ("Horizon Falloff", Range(0.5, 16)) = 4
        _BottomFalloff ("Bottom Falloff", Range(0.5, 16)) = 6

        [Header(Fog Horizon)]
        // Uebernimmt die Fog-Farbe aus den Lighting Settings fuer den
        // Horizontbereich. Fog-Farbe aendern -> Himmel zieht automatisch mit.
        [Toggle] _UseFogColor ("Use Fog Color For Horizon", Float) = 1
        // Wie weit der Nebelstreifen vom Horizont nach oben/unten reicht.
        // 0 = aus. 0.25 = das untere Viertel des Blicks nach oben ist vernebelt.
        _FogHorizonHeight ("Fog Horizon Height", Range(0, 1)) = 0.25
        // Wie hart die Kante des Nebelstreifens ist. Hoch = weicher Uebergang.
        _FogHorizonSharpness ("Fog Horizon Softness", Range(1, 16)) = 4

        [Header(PSX Look)]
        // Erzwingt eine niedrige Aufloesung beim Sampling der Panorama-Textur.
        [Toggle] _TexPixelate ("Pixelate Texture", Float) = 0
        _TexPixelateRes ("Forced Texture Resolution", Vector) = (512, 256, 0, 0)
        // HINWEIS: Quantisierung, Dithering und Grain kommen global von der
        // PSXLookController-Component - Himmel und Geometrie sind damit
        // automatisch immer synchron.
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _UseTexture;
                float _Rotation;
                float _Exposure;
                half4 _Tint;
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                float _HorizonFalloff;
                float _BottomFalloff;
                float _UseFogColor;
                float _FogHorizonHeight;
                float _FogHorizonSharpness;
                float _TexPixelate;
                float4 _TexPixelateRes;
            CBUFFER_END

            // Globale Werte, gesetzt von der PSXLookController-Component
            float _PSX_Quantize;
            float _PSX_ColorLevels;
            float _PSX_DitherStrength;
            float _PSX_AnimateDither;
            float _PSX_GrainStrength;
            float _PSX_NoiseFps;
            float _PSX_ShadowClean;
            float _PSX_MasterActive;
            float _PSX_TexPixelateOff;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction  : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Bei Skyboxen entspricht die Objektposition der Blickrichtung
                output.direction = input.positionOS.xyz;
                return output;
            }

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

            // Blickrichtung -> equirektangulaere UV (Panorama 2:1,
            // Horizont in der Bildmitte)
            float2 DirectionToEquirectUV(float3 dir)
            {
                float longitude = atan2(dir.x, dir.z);
                float latitude = asin(clamp(dir.y, -1.0, 1.0));

                float u = longitude / (2.0 * PI) + 0.5 + _Rotation / 360.0;
                float v = latitude / PI + 0.5;

                return float2(u, v);
            }

            half3 SampleGradient(float y, half3 horizonColor)
            {
                if (y >= 0.0)
                {
                    float t = saturate(1.0 - pow(saturate(1.0 - y), _HorizonFalloff));
                    return lerp(horizonColor, _TopColor.rgb, t);
                }

                float tb = saturate(1.0 - pow(saturate(1.0 + y), _BottomFalloff));
                return lerp(horizonColor, _BottomColor.rgb, tb);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float y = dir.y;

                half3 fogColor = _UseFogColor > 0.5
                    ? unity_FogColor.rgb
                    : _HorizonColor.rgb;

                half3 color;

                if (_UseTexture > 0.5)
                {
                    float2 uv = DirectionToEquirectUV(dir);

                    // Optionale Pixelierung der Panorama-Textur
                    // (vom Showcase in der Modern-Etappe deaktivierbar)
                    if (_TexPixelate > 0.5 &&
                        !(_PSX_MasterActive > 0.5 && _PSX_TexPixelateOff > 0.5))
                    {
                        float2 res = max(float2(2, 2), _TexPixelateRes.xy);
                        uv = (floor(uv * res) + 0.5) / res;
                    }

                    color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                    color *= _Tint.rgb * _Exposure;
                }
                else
                {
                    color = SampleGradient(y, fogColor);
                }

                // Fog-Streifen am Horizont: blendet Himmel (Textur ODER
                // Gradient) nahe y=0 in die Fog-Farbe. Genau dort sitzen
                // Gebaeudesilhouetten und Polygon-Luecken.
                if (_FogHorizonHeight > 0.001)
                {
                    float band = saturate(1.0 - abs(y) / _FogHorizonHeight);
                    float fogMix = pow(band, max(1.0, _FogHorizonSharpness));
                    color = lerp(color, fogColor, fogMix);
                }

                // Analog-Rauschen + Quantisierung, damit der Himmel dieselbe
                // lebendige Koernung hat wie die Geometrie mit dem PSX/Lit-Shader
                float noiseFrame = floor(_Time.y * max(1.0, _PSX_NoiseFps));

                // Dunkle Bereiche entrauschen (siehe PSX/Lit: Shadow Clean)
                float luma = dot(color, half3(0.299, 0.587, 0.114));
                float darkDamp = lerp(1.0, smoothstep(0.0, 0.12, luma),
                                      saturate(_PSX_ShadowClean));

                if (_PSX_GrainStrength > 0.001)
                {
                    float grain = Hash21(input.positionCS.xy + noiseFrame * 17.13) * 2.0 - 1.0;
                    color += grain * _PSX_GrainStrength * 0.05 * darkDamp;
                }

                if (_PSX_Quantize > 0.5)
                {
                    float levels = max(2.0, _PSX_ColorLevels) - 1.0;

                    float2 ditherPos = input.positionCS.xy;
                    if (_PSX_AnimateDither > 0.5)
                    {
                        ditherPos += floor(float2(
                            frac(noiseFrame * 0.7548777) * 4.0,
                            frac(noiseFrame * 0.5698403) * 4.0));
                    }

                    float dither = DitherOffset(ditherPos) * _PSX_DitherStrength * darkDamp;
                    color = floor(color * levels + 0.5 + dither) / levels;
                }

                color = saturate(color);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
