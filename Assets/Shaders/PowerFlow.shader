Shader "TowerDefense/PowerFlow"
{
    Properties
    {
        _EnergyColor ("Energy Color", Color) = (1, 1, 1, 0.45)
        _Speed ("Rise Speed", Float) = 1.2
        _Detail ("Streak Detail", Float) = 1.2
        _Spread ("Streak Spread", Range(0, 1)) = 0.5
        _Sway ("Wave Amount", Float) = 1
        _Glow ("Wide Glow", Range(0, 1)) = 0.4
        _ColumnFade ("Column Fade Time", Float) = 0.35
        _Strength ("Strength", Range(0, 1)) = 0
        _Now ("Game Time", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "PowerFlow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                // Per column: x = height in grid cells, y = random seed, z = spawn time.
                float4 column : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 column : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _EnergyColor;
                float _Speed;
                float _Detail;
                float _Spread;
                float _Sway;
                float _Glow;
                float _ColumnFade;
                float _Strength;
                float _Now;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.column = input.column;
                return output;
            }

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 blend = frac(p);
                blend = blend * blend * (3.0 - 2.0 * blend);

                float a = hash21(cell);
                float b = hash21(cell + float2(1.0, 0.0));
                float c = hash21(cell + float2(0.0, 1.0));
                float d = hash21(cell + float2(1.0, 1.0));

                return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
            }

            /// Two octaves of noise scrolling upward. Nothing here quantises the
            /// result, so brightness travels up the column as a wave rather than
            /// as separate specks.
            float flowNoise(float2 p)
            {
                return valueNoise(p) * 0.65 + valueNoise(p * 2.3 + 5.1) * 0.35;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                // Everything is measured in grid cells so a column over three cages
                // moves at the same pace as one over a single cage.
                float cells = max(input.column.x, 0.001);
                float seed = input.column.y;
                float birth = input.column.z;
                float y = uv.y * cells;
                float rise = _Now * _Speed;

                // The script drives _Now, so a column added mid-round eases in
                // instead of popping on at full brightness.
                float age = _ColumnFade > 0.0
                    ? saturate((_Now - birth) / _ColumnFade)
                    : 1.0;

                // Thin wavy lines. Their whole waveform is offset by time, so the
                // curve slides upward and the line reads as climbing.
                float streaks = 0.0;

                [unroll]
                for (int streakIndex = 0; streakIndex < 5; streakIndex++)
                {
                    float streakSeed = (float)streakIndex;
                    float lane = hash11(streakSeed * 3.17 + seed * 11.13);
                    float pace = hash11(streakSeed * 7.71 + seed * 5.37 + 3.1);
                    float phase = hash11(streakSeed * 2.39 + seed * 9.11 + 8.7);

                    float travel = y - rise * lerp(0.8, 1.35, pace) - phase * 13.0;

                    // Two waves of different frequency keep the line from looking
                    // like a plain sine.
                    float centre = lerp(0.5 - _Spread * 0.5, 0.5 + _Spread * 0.5, lane);
                    float wobble = (sin(travel * 2.6 + phase * 6.2831) * 0.6
                        + sin(travel * 4.7 + lane * 6.2831) * 0.25) * 0.09 * _Sway;
                    float thickness = lerp(0.045, 0.075, lane);
                    float acrossLine = (uv.x - centre - wobble) / thickness;
                    // Not "line" - HLSL keeps that as a geometry primitive type.
                    float lineMask = exp(-acrossLine * acrossLine);

                    // Long, softly ended segments so each line reads as a streak
                    // rather than a stripe running the full height.
                    float segment = smoothstep(0.25, 0.75,
                        flowNoise(float2(streakSeed * 9.1 + seed * 6.0, travel * _Detail)));

                    streaks = max(streaks, lineMask * segment * lerp(0.7, 1.0, pace));
                }

                // A broad, faint haze the streaks travel through, so the column
                // still reads as a glow between them.
                float fromCentre = (uv.x - 0.5) / 0.34;
                float2 hazeUV = float2(
                    uv.x * 1.1 + seed * 5.0,
                    y * _Detail * 0.55 - rise * 0.7);
                float wide = exp(-fromCentre * fromCentre)
                    * lerp(0.55, 1.0, smoothstep(0.2, 0.85, flowNoise(hazeUV)));

                float energy = saturate(max(wide * _Glow, streaks));

                // Gathers just above the cage and dims again as it slips into the
                // tower, so neither end of the quad shows a cut edge. It brightens
                // on the way up, which reads as the energy arriving at the tower.
                float baseFade = smoothstep(0.0, 0.22, y);
                float topFade = 1.0 - smoothstep(cells - 0.28, cells - 0.02, y);
                float sideFade = smoothstep(0.0, 0.12, uv.x)
                    * smoothstep(0.0, 0.12, 1.0 - uv.x);
                float gain = lerp(0.85, 1.1, uv.y);

                half alpha = _EnergyColor.a
                    * saturate(energy * gain)
                    * baseFade
                    * topFade
                    * sideFade
                    * age
                    * _Strength;

                return half4(_EnergyColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
