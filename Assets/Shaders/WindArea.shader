Shader "TowerDefense/WindArea"
{
    Properties
    {
        _WindColor ("Wind Color", Color) = (0.94, 0.98, 1, 0.3)
        _Speed ("Wind Speed", Float) = 2.5
        _Density ("Streak Density", Float) = 9
        _Distortion ("Distortion", Float) = 1.5
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
            Name "Wind"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _WindColor;
                float _Speed;
                float _Density;
                float _Distortion;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
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

            float windNoise(float2 p)
            {
                float noise = 0.0;
                float amplitude = 0.55;

                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    noise += valueNoise(p) * amplitude;
                    p = mul(float2x2(0.8, -0.6, 0.6, 0.8), p) * 2.03 + 4.17;
                    amplitude *= 0.5;
                }

                return noise;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float time = _Time.y * _Speed;

                // Horizontal bands form streaks in the direction the fan faces.
                // Their vertical position bends gently instead of forming a grid.
                float bendA = sin(uv.x * 7.0 - time * 1.6) * 0.025 * _Distortion;
                float bendB = sin(uv.x * 11.0 - time * 2.1 + 2.4) * 0.018 * _Distortion;
                float bandA = abs(sin((uv.y + bendA) * _Density * 3.14159));
                float bandB = abs(sin((uv.y + bendB + 0.07) * (_Density * 0.55) * 3.14159));
                float thinStreaks = pow(saturate(1.0 - bandA), 11.0);
                float wideStreaks = pow(saturate(1.0 - bandB), 3.5);

                // Every horizontal band gets independent timing, speed, length,
                // and idle cycles. Its head moves along +X and leaves a short
                // fading trail, preventing synchronized clumps.
                float bandIndexA = floor(uv.y * _Density);
                float bandIndexB = floor((uv.y + 0.07) * (_Density * 0.55));
                float phaseA = hash21(float2(bandIndexA * 2.17, 7.3));
                float phaseB = hash21(float2(13.1, bandIndexB * 3.71));
                float speedA = lerp(0.18, 0.52,
                    hash21(float2(bandIndexA + 19.4, bandIndexA * 0.37)));
                float speedB = lerp(0.12, 0.38,
                    hash21(float2(bandIndexB * 0.61, bandIndexB + 31.8)));
                float movementA = time * speedA + phaseA;
                float movementB = time * speedB + phaseB;
                float headA = frac(movementA);
                float headB = frac(movementB);
                float trailA = frac(headA - uv.x);
                float trailB = frac(headB - uv.x);
                float lengthA = lerp(0.07, 0.2,
                    hash21(float2(bandIndexA + 4.6, 22.9)));
                float lengthB = lerp(0.1, 0.26,
                    hash21(float2(41.2, bandIndexB + 8.7)));
                float segmentA = (1.0 - smoothstep(lengthA * 0.55, lengthA, trailA))
                    * smoothstep(0.0, 0.025, trailA);
                float segmentB = (1.0 - smoothstep(lengthB * 0.5, lengthB, trailB))
                    * smoothstep(0.0, 0.035, trailB);

                float cycleA = floor(movementA);
                float cycleB = floor(movementB);
                float activeA = step(0.3,
                    hash21(float2(bandIndexA * 5.3, cycleA + 17.0)));
                float activeB = step(0.42,
                    hash21(float2(cycleB + 29.0, bandIndexB * 7.1)));
                segmentA *= activeA;
                segmentB *= activeB;

                float fineVariation = windNoise(float2(
                    uv.x * 6.0 - time * 1.1,
                    uv.y * 8.0
                ));
                float streakBody = thinStreaks * segmentA
                    * smoothstep(0.3, 0.68, fineVariation);
                float softStreakBody = wideStreaks * segmentB
                    * smoothstep(0.38, 0.76, fineVariation)
                    * 0.38;

                // Layered, rotated noise produces round wisps instead of visible
                // rectangular cells. It drifts forward more slowly than streaks.
                float2 gustUV = float2(
                    uv.x * 2.7 - time * 0.32,
                    uv.y * 3.4 + sin(uv.x * 5.0 - time) * 0.12
                );
                float gustNoise = windNoise(gustUV);
                float detailNoise = windNoise(gustUV * 2.1 + 9.7);
                float gustBody = smoothstep(0.5, 0.78, gustNoise)
                    * smoothstep(0.22, 0.72, detailNoise)
                    * 0.32;

                // All boundaries dissolve softly; the tapered mesh supplies the
                // funnel silhouette while these fades remove visible hard edges.
                float sideFade = smoothstep(0.0, 0.22, uv.y)
                    * smoothstep(0.0, 0.22, 1.0 - uv.y);
                float sourceFade = smoothstep(0.0, 0.12, uv.x);
                float frontFade = 1.0 - smoothstep(0.68, 1.0, uv.x);
                float breathing = 0.82 + sin(time * 1.7 + uv.x * 5.0) * 0.12;

                half alpha = _WindColor.a
                    * (gustBody + streakBody + softStreakBody)
                    * sideFade
                    * sourceFade
                    * frontFade
                    * breathing;

                return half4(_WindColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
