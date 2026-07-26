Shader "TowerDefense/SwampWater"
{
    Properties
    {
        _ShallowColor ("Shallow Water", Color) = (0.221, 0.298, 0.145, 1)
        _DeepColor ("Deep Water", Color) = (0.043, 0.066, 0.02, 1)
        _SheenColor ("Ripple Sheen", Color) = (0.36, 0.45, 0.24, 1)
        _ScumColor ("Algae Scum", Color) = (0.294, 0.361, 0.129, 1)
        _SurfaceColor ("Waterline", Color) = (0.478, 0.573, 0.302, 1)
        _Aspect ("Quad Aspect (width / height)", Float) = 10.4
        _WaveHeight ("Wave Height", Range(0, 0.25)) = 0.05
        _WaveSpeed ("Wave Speed", Float) = 0.55
        _RippleScale ("Ripple Scale", Float) = 2.4
        _RippleSpeed ("Ripple Speed", Float) = 0.5
        _Perspective ("Perspective Compression", Range(0.02, 0.6)) = 0.22
        _DepthFalloff ("Depth Falloff", Float) = 1.15
        _Reflections ("Reflection Strength", Range(0, 1)) = 0.55
        _Scum ("Scum Amount", Range(0, 1)) = 0.35
        _EdgeFade ("Horizontal Edge Fade", Range(0, 0.5)) = 0.1
        _Opacity ("Opacity", Range(0, 1)) = 1
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
            Name "SwampWater"
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
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SheenColor;
                half4 _ScumColor;
                half4 _SurfaceColor;
                float _Aspect;
                float _WaveHeight;
                float _WaveSpeed;
                float _RippleScale;
                float _RippleSpeed;
                float _Perspective;
                float _DepthFalloff;
                float _Reflections;
                float _Scum;
                float _EdgeFade;
                float _Opacity;
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

            float waterNoise(float2 p)
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
                float time = _Time.y;

                // The quad is far wider than it is tall, so raw UVs would smear every
                // noise cell and sine wave into a horizontal streak. Stretching x by the
                // quad's aspect gives a space where one unit is one quad-height in both
                // directions, and every frequency below is in those world-proportional
                // units rather than in UVs.
                float2 st = float2(uv.x * _Aspect, uv.y);

                // The waterline is a layered swell rather than a straight cut, so the
                // island never sits on a ruler-edged strip of water. Three octaves at
                // unrelated speeds stop the crests from marching in lockstep.
                float swell = sin(st.x * 7.9 + time * _WaveSpeed) * 0.5
                    + sin(st.x * 15.7 - time * _WaveSpeed * 1.37) * 0.3
                    + sin(st.x * 31.4 + time * _WaveSpeed * 0.71 + 1.9) * 0.2;
                float surfaceY = 1.0 - _WaveHeight * (0.5 - swell * 0.5);

                // Distance below the waterline drives every other term.
                float below = surfaceY - uv.y;

                // Rows crowd together as they approach the waterline and stretch out
                // toward the camera - the fake perspective that makes a flat quad read
                // as a receding surface. Adding time marches the ripples forward.
                // The max() matters: above the waterline `below` goes negative, and a
                // large wave height against a small perspective term could otherwise
                // divide through zero and spray NaNs across the quad.
                float rows = 1.0 / max(below + _Perspective, 0.02);
                float scrolledRows = rows * _RippleScale + time * _RippleSpeed;

                // Base body: light pools just under the surface and drains into near
                // black with depth, following the painted background's falloff.
                float3 water = lerp(
                    _ShallowColor.rgb,
                    _DeepColor.rgb,
                    saturate(pow(saturate(below * _DepthFalloff), 0.8))
                );

                // Broken horizontal glints. Two masks at very different frequencies do
                // the work: the fine one chops each band into short dashes, the coarse
                // one gathers those dashes into patches. With only the fine mask the
                // bands survive as continuous lines and the water reads as a contour map.
                float warp = waterNoise(float2(st.x * 2.6, rows * 0.5 + time * 0.13)) - 0.5;
                float band = abs(sin((scrolledRows + warp * 2.2) * 3.14159));
                float glint = pow(saturate(1.0 - band), 7.0);
                float glintDashes = smoothstep(0.46, 0.76,
                    waterNoise(float2(st.x * 3.4 - time * 0.07, rows * 1.6)));
                float glintPatches = smoothstep(0.32, 0.7,
                    waterNoise(float2(st.x * 1.6 + time * 0.03, rows * 0.35)));
                // Sparkle belongs in the middle distance. Right at the horizon the rows
                // crowd past what the pixels can resolve and alias into hard stripes, and
                // the painted background hazes that edge over anyway.
                float glintFade = smoothstep(0.02, 0.14, below)
                    * (1.0 - smoothstep(0.3, 0.85, below));
                water += _SheenColor.rgb * glint * glintDashes * glintPatches * glintFade * 0.55;

                // Slow, broad swings in brightness. Without them the body is a single
                // flat wash of green; the painted water pools light in patches.
                float murk = waterNoise(float2(
                    st.x * 0.55 - time * 0.015,
                    below * 1.1 + time * 0.008
                ));
                water *= lerp(0.82, 1.18, murk);

                // Vertical smears standing in for the treeline mirrored in the water.
                // Sampling at a fixed row makes the pattern purely a function of x; the
                // wobble term shears it per-row so the reflection ripples.
                float wobble = sin(below * 26.0 - time * 1.15) * 0.018
                    + sin(below * 41.0 + time * 0.77) * 0.011;
                float trunks = waterNoise(float2((st.x + wobble) * 1.1, 0.5));
                float trunkMask = smoothstep(0.42, 0.8, trunks)
                    * (1.0 - smoothstep(0.0, 0.55, below));
                water *= lerp(1.0, 0.52, trunkMask * _Reflections);

                // Slow algae drifting on the surface, thickest near the waterline.
                float scum = smoothstep(0.54, 0.84, waterNoise(float2(
                    st.x * 0.7 - time * 0.022,
                    below * 2.4 + time * 0.014
                )));
                water = lerp(water, _ScumColor.rgb,
                    scum * _Scum * (1.0 - smoothstep(0.0, 0.4, below)));

                // A bright, speckled lip right at the waterline sells the contact edge.
                float lipSpeckle = 0.55 + 0.45 * smoothstep(0.4, 0.75,
                    waterNoise(float2(st.x * 14.0 - time * 0.35, time * 0.2)));
                float lip = (1.0 - smoothstep(0.0, 0.02, below)) * lipSpeckle;
                water += _SurfaceColor.rgb * lip * 0.35;

                // The far waterline hazes in over a band rather than starting on a drawn
                // edge - it is a horizon seen through swamp air, and the painted
                // background fades the treeline into the water the same way. Left and
                // right ends fade too so the quad melts into that background.
                float surfaceMask = smoothstep(-0.006, 0.045, below);
                float edgeFade = smoothstep(0.0, _EdgeFade, uv.x)
                    * smoothstep(0.0, _EdgeFade, 1.0 - uv.x);

                half alpha = _ShallowColor.a * _Opacity * surfaceMask * edgeFade;
                return half4(water, alpha);
            }
            ENDHLSL
        }
    }
}
