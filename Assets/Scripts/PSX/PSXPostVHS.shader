// PSX Post VHS - Fullscreen-Effekt fuer URP (Unity 6)
//
// Laeuft als Full Screen Pass ueber das fertige Bild und liefert die
// "Videosignal"-Schicht des Looks:
//   - Vignette (dunkle Ecken)
//   - Chromatic Aberration (RGB-Kanaele leicht versetzt, VHS-Farbsaum)
//   - Tracking-Wobble (traeges horizontales Schwimmen des Bildes)
//   - Scanlines (optional)
//   - Glitch-Streifen: ab und zu wandert ein gestoerter Streifen
//     durchs Bild (Zeilenversatz + Rauschen), wie ein Bandfehler
//
// SETUP:
// 1. Material mit diesem Shader erstellen ("PSX/Post VHS").
// 2. Das aktive URP-Renderer-Asset auswaehlen (im URP-Asset unter
//    "Renderer List" verlinkt) -> "Add Renderer Feature" ->
//    "Full Screen Pass Renderer Feature".
// 3. Im Feature: Pass Material = das neue Material,
//    "Fetch Color Buffer" AN,
//    Injection Point = "After Rendering Post Processing".
//
// Der Pass laeuft auf der niedrigen Renderaufloesung (vor dem
// Point-Upscaling) - die Artefakte werden also automatisch schoen grob.

Shader "PSX/Post VHS"
{
    Properties
    {
        [Header(Vignette)]
        [Space(4)]
        // Wie stark die Ecken abdunkeln. 0 = aus.
        [Range(0,1)] _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.45
        // Ab welchem Abstand von der Mitte die Vignette beginnt (0 = frueh/eng).
        [Range(0,1)] _VignetteStart ("Vignette Start", Range(0, 1)) = 0.35

        [Header(Chromatic Aberration)]
        [Space(4)]
        // Horizontaler Versatz der Rot-/Blau-Kanaele in Pixeln der
        // Renderaufloesung. 0.5-1.5 = subtiler VHS-Farbsaum.
        _AberrationPixels ("Aberration (Pixel)", Range(0, 4)) = 1.0

        [Header(Tracking Wobble)]
        [Space(4)]
        // Traeges horizontales Schwimmen des ganzen Bildes (UV-Anteil).
        // 0.001-0.002 = kaum bewusst sichtbar, aber das Bild "steht" nie still.
        _WobbleStrength ("Wobble Strength", Range(0, 0.01)) = 0.0015
        _WobbleSpeed ("Wobble Speed", Range(0, 10)) = 1.5

        [Header(Scanlines)]
        [Space(4)]
        // Abdunkelung jeder zweiten Renderzeile. 0 = aus, 0.05-0.15 = subtil.
        [Range(0,1)] _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.08

        [Header(Glitch Stripe (Bandfehler))]
        [Space(4)]
        // Wahrscheinlichkeit pro Intervall, dass ein Streifen auftritt.
        // 0.3 = ca. jedes dritte Intervall. 0 = aus.
        [Range(0,1)] _StripeChance ("Stripe Chance", Range(0, 1)) = 0.3
        // Laenge eines Intervalls in Sekunden. Der Streifen wandert waehrend
        // des Intervalls einmal durchs Bild.
        _StripeInterval ("Stripe Interval (s)", Range(0.5, 15)) = 4
        // Hoehe des Streifens (Anteil der Bildhoehe).
        _StripeHeight ("Stripe Height", Range(0.005, 0.2)) = 0.035
        // Wie stark die Zeilen im Streifen horizontal verschoben werden.
        _StripeShift ("Stripe Shift", Range(0, 0.2)) = 0.05
        // Zusaetzliches Rauschen im Streifen.
        [Range(0,1)] _StripeNoise ("Stripe Noise", Range(0, 1)) = 0.35

        [Header(Noise Timing)]
        [Space(4)]
        // Tickrate der Glitch-/Rausch-Animation (unabhaengig von der Framerate).
        _NoiseFps ("Noise FPS", Range(1, 60)) = 24

        [Header(Fluctuation (organisches Atmen))]
        [Space(4)]
        // Bricht die technische Regelmaessigkeit auf: Wobble, Scanlines und
        // Aberration schwanken mit einem langsamen Noise - ruhige Phasen,
        // dann kleine Schuebe. 0 = exakt periodisch (steril), 1 = voll organisch.
        [Range(0,1)] _FluctuationAmount ("Fluctuation Amount", Range(0, 1)) = 0.6
        // Wie schnell die Schwankungen durchlaufen. Niedrig = traeges Atmen.
        _FluctuationSpeed ("Fluctuation Speed", Range(0.05, 5)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PSX VHS"

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Stellt Vert, Varyings (positionCS + texcoord), _BlitTexture
            // und die Standard-Sampler bereit
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _VignetteStrength;
                float _VignetteStart;
                float _AberrationPixels;
                float _WobbleStrength;
                float _WobbleSpeed;
                float _ScanlineStrength;
                float _StripeChance;
                float _StripeInterval;
                float _StripeHeight;
                float _StripeShift;
                float _StripeNoise;
                float _NoiseFps;
                float _FluctuationAmount;
                float _FluctuationSpeed;
            CBUFFER_END

            float Hash11(float p)
            {
                return frac(sin(p * 127.1) * 43758.5453);
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Weiches 1D-Value-Noise: kontinuierlich, aber unvorhersehbar -
            // die Basis fuer alles "organische" Schwanken (statt reiner Sinus)
            float ValueNoise(float t)
            {
                float i = floor(t);
                float f = frac(t);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash11(i), Hash11(i + 1.0), f);
            }

            half3 SampleSource(float2 uv)
            {
                uv = clamp(uv, 0.0, 1.0);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float t = _Time.y;
                float noiseFrame = floor(t * max(1.0, _NoiseFps));

                // ----------------------------------------------------------
                // 1) Tracking-Wobble: das ganze Bild schwimmt traege seitlich,
                //    leicht abhaengig von der Bildzeile (wie schlechtes Tracking)
                // ----------------------------------------------------------
                if (_WobbleStrength > 0.0)
                {
                    // Periodische Grundwelle...
                    float wobble = sin(t * _WobbleSpeed + uv.y * 6.2832)
                                 + 0.5 * sin(t * _WobbleSpeed * 2.7 + uv.y * 17.0);

                    // ...mit organischem Noise-Anteil verschnitten, damit die
                    // Bewegung nicht vorhersehbar pendelt
                    float organic = ValueNoise(t * _WobbleSpeed * 1.9 + uv.y * 4.0) * 2.0 - 1.0;
                    wobble = lerp(wobble, wobble * 0.4 + organic * 1.6, _FluctuationAmount);

                    // Amplitude "atmet": ruhige Phasen, dazwischen kleine
                    // Tracking-Schuebe wie bei echtem Bandmaterial
                    float breathe = ValueNoise(t * _FluctuationSpeed);
                    float surge = smoothstep(0.78, 0.97,
                        ValueNoise(t * _FluctuationSpeed * 0.37 + 11.3)) * 2.5;
                    float amp = lerp(1.0, 0.35 + breathe * 1.3 + surge, _FluctuationAmount);

                    uv.x += wobble * _WobbleStrength * amp;
                }

                // ----------------------------------------------------------
                // 2) Glitch-Streifen: pro Intervall entscheidet ein Wuerfelwurf,
                //    ob ein Stoerstreifen einmal durchs Bild wandert.
                //    Im Streifen: zeilenweiser Versatz + Rauschen.
                // ----------------------------------------------------------
                float stripeMask = 0.0;

                if (_StripeChance > 0.001)
                {
                    float interval = floor(t / _StripeInterval);
                    float phase = frac(t / _StripeInterval);

                    // Wuerfelwurf pro Intervall
                    float active = step(Hash11(interval + 0.37), _StripeChance);

                    // Startzeile zufaellig, wandert waehrend des Intervalls
                    // von oben nach unten durchs Bild
                    float stripeY = frac(Hash11(interval * 1.93 + 7.7) + phase);

                    stripeMask = active * step(abs(uv.y - stripeY), _StripeHeight);

                    if (stripeMask > 0.0)
                    {
                        // Zeilenweiser horizontaler Versatz (pro Renderzeile
                        // eigener Zufallswert, tickt mit Noise FPS)
                        float lineId = floor(uv.y * _ScreenParams.y);
                        float lineShift = Hash21(float2(lineId, noiseFrame)) * 2.0 - 1.0;
                        uv.x += lineShift * _StripeShift * stripeMask;
                    }
                }

                // ----------------------------------------------------------
                // 3) Chromatic Aberration: R und B leicht auseinanderziehen
                // ----------------------------------------------------------
                half3 color;

                // Aberration schwankt leicht mit - der Farbsaum "pumpt" subtil
                float abPixels = _AberrationPixels * lerp(1.0,
                    0.6 + 0.8 * ValueNoise(t * _FluctuationSpeed * 2.3 + 27.0),
                    _FluctuationAmount);

                if (abPixels > 0.001)
                {
                    float2 offset = float2(abPixels / _ScreenParams.x, 0.0);
                    color.r = SampleSource(uv + offset).r;
                    color.g = SampleSource(uv).g;
                    color.b = SampleSource(uv - offset).b;
                }
                else
                {
                    color = SampleSource(uv);
                }

                // ----------------------------------------------------------
                // 4) Rauschen + Aufhellen im Glitch-Streifen
                // ----------------------------------------------------------
                if (stripeMask > 0.0)
                {
                    float n = Hash21(input.texcoord * _ScreenParams.xy + noiseFrame * 31.7);
                    color += (n * 2.0 - 1.0) * _StripeNoise * stripeMask;
                    color += _StripeNoise * 0.15 * stripeMask; // leichter Helligkeits-Lift
                }

                // ----------------------------------------------------------
                // 5) Scanlines: jede zweite Renderzeile abdunkeln
                // ----------------------------------------------------------
                if (_ScanlineStrength > 0.001)
                {
                    // Staerke flackert leicht mit hoeherer Frequenz...
                    float flicker = lerp(1.0,
                        0.7 + 0.6 * ValueNoise(t * 7.3),
                        _FluctuationAmount);

                    // ...und gelegentlich springt die Zeilenphase um eine Zeile
                    // (kurzes Interlace-Zucken, wie ein Sync-Aussetzer)
                    float phaseJump = step(0.93,
                        ValueNoise(t * _FluctuationSpeed * 1.7 + 4.7))
                        * step(0.01, _FluctuationAmount);

                    float scan = fmod(input.positionCS.y + phaseJump, 2.0) < 1.0
                        ? 1.0
                        : 1.0 - _ScanlineStrength * flicker;
                    color *= scan;
                }

                // ----------------------------------------------------------
                // 6) Vignette
                // ----------------------------------------------------------
                if (_VignetteStrength > 0.001)
                {
                    float dist = length((input.texcoord - 0.5) * 2.0);
                    float vig = smoothstep(_VignetteStart, 1.35, dist);
                    color *= 1.0 - vig * _VignetteStrength;
                }

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
