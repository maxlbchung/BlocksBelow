Shader "TowerDefense/SwampFog"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.45, 0.52, 0.38, 0.36)
        _MoteColor ("Particle Color", Color) = (0.72, 0.8, 0.58, 1)
        _Aspect ("Quad Aspect (width / height)", Float) = 5.8
        _RiseSpeed ("Rise Speed", Float) = 0.11
        _DriftSpeed ("Drift Speed", Float) = 0.04
        _WispScale ("Wisp Scale", Float) = 2.6
        _Density ("Density", Range(0, 2)) = 1
        _BedHeight ("Mist Bed Height", Range(0.02, 1)) = 0.2
        _Reach ("Fog Reach", Range(0.1, 1)) = 0.78
        _Plumes ("Rising Plumes", Range(0, 1)) = 0.7
        _PlumeColumns ("Plume Columns", Float) = 7
        _MoteAmount ("Particle Amount", Range(0, 1)) = 0.45
        _MoteScale ("Particle Scale", Float) = 9
        _EdgeFade ("Horizontal Edge Fade", Range(0, 0.5)) = 0.12
        _Strength ("Strength", Range(0, 1)) = 1
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
            Name "SwampFog"
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
                half4 _FogColor;
                half4 _MoteColor;
                float _Aspect;
                float _RiseSpeed;
                float _DriftSpeed;
                float _WispScale;
                float _Density;
                float _BedHeight;
                float _Reach;
                float _Plumes;
                float _PlumeColumns;
                float _MoteAmount;
                float _MoteScale;
                float _EdgeFade;
                float _Strength;
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

            float fogNoise(float2 p)
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

            // One plume climbing out of the water on a given column. Each seed draws its
            // own phase, speed, length and per-cycle idle chance, so plumes surface at
            // scattered moments instead of belching in unison.
            float plumeSegment(float column, float uvy, float time, float seed)
            {
                float2 randomBase = float2(
                    column * 3.11 + seed * 17.31,
                    seed * 7.73 + 2.91
                );
                float phase = hash21(randomBase);
                float speed = lerp(0.07, 0.21, hash21(randomBase + 21.7));
                float movement = time * speed + phase;
                float head = frac(movement);
                float trail = frac(head - uvy);
                float segmentLength = lerp(0.18, 0.5, hash21(randomBase + 6.13));

                float segment = (1.0 - smoothstep(segmentLength * 0.45, segmentLength, trail))
                    * smoothstep(0.0, 0.06, trail);

                float cycle = floor(movement);
                float active = step(0.35, hash21(float2(randomBase.x * 4.73, cycle + 13.0)));
                return segment * active;
            }

            // Sparse specks carried up on the mist - the "particles" half of the effect.
            // One point per noise cell keeps it to a single hash lookup per sample.
            // Takes world-proportional coordinates so the cells stay square and the
            // specks come out round rather than smeared into horizontal dashes.
            float moteField(float2 st, float time, float seed)
            {
                // Sway is keyed off height, not the cell, so a speck drifts smoothly
                // sideways as it climbs rather than snapping between columns.
                float sway = sin(st.y * 4.3 + time * 0.8 + seed) * 0.22
                    + sin(st.y * 9.1 - time * 0.53 + seed * 2.1) * 0.1;
                float2 p = float2(st.x * _MoteScale + sway, st.y * _MoteScale - time * 0.55);

                float2 cell = floor(p);
                float2 offset = frac(p);
                float2 seedCell = cell + seed;
                float2 center = float2(hash21(seedCell), hash21(seedCell + 4.31));

                float mote = smoothstep(0.11, 0.0, length(offset - center));

                // Most cells stay empty, and the survivors pulse out of sync. Keep the
                // threshold high - specks in every cell read as falling snow, not as
                // the odd fleck of muck lifted off a swamp.
                float life = hash21(seedCell + 9.17);
                mote *= step(0.82, life);
                mote *= 0.55 + 0.45 * sin(time * 2.6 + life * 37.0);
                return saturate(mote);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float time = _Time.y;

                // Sampling the noise with a downward-scrolling V walks the pattern up
                // the quad: uv.y 0 is the waterline, 1 is the top of the fog volume.
                float rise = time * _RiseSpeed;
                float drift = time * _DriftSpeed;

                // The quad is much wider than it is tall, so raw UVs would flatten every
                // puff into a horizontal smear. Stretching x by the quad's aspect gives a
                // space where one unit is one quad-height either way, and the scales
                // below are all in those world-proportional units.
                float2 st = float2(uv.x * _Aspect, uv.y);

                // 1. The bed - a thick, slow blanket lying on the water. Deliberately
                // stretched wider than it is tall, since mist pools flat across a
                // surface. It hugs the water and dies off exponentially with height, so
                // it never floats free of it.
                float bed = fogNoise(float2(
                    st.x * _WispScale * 0.55 - drift,
                    st.y * _WispScale * 1.6 - rise * 0.45
                ));
                float bedProfile = exp(-uv.y / max(_BedHeight, 0.02));
                float mistBed = smoothstep(0.3, 0.78, bed) * bedProfile;

                // 2. Wisps - domain warped so the columns curl and shear as they climb
                // instead of sliding straight up like a scrolling texture.
                float2 warp = float2(
                    fogNoise(float2(st.x * 1.5 + 5.2, st.y * 2.0 - rise * 1.4)),
                    fogNoise(float2(st.x * 1.5 - 3.7, st.y * 2.0 - rise * 1.1))
                ) - 0.5;
                float wisps = fogNoise(float2(
                    st.x * _WispScale + warp.x * 0.85 - drift * 1.6,
                    st.y * _WispScale * 1.25 + warp.y * 0.55 - rise * 2.1
                ));
                // Fog thins out with height and never quite reaches the top edge.
                float reach = 1.0 - smoothstep(_Reach * 0.45, _Reach, uv.y);
                // A wide ramp on purpose: a tight one clips the noise flat wherever it
                // clears the threshold, and those plateaus read as solid grey blobs
                // rather than as mist.
                float wispBody = smoothstep(0.47, 0.9, wisps) * reach;

                // 3. Plumes - discrete puffs breaking the surface and dissipating as
                // they widen. Neighbouring columns are sampled so a plume that drifts
                // past its own cell boundary is not sliced in half.
                float columnPosition = uv.x * _PlumeColumns;
                float baseColumn = floor(columnPosition);
                float plumeBody = 0.0;

                [unroll]
                for (int neighbour = -1; neighbour <= 1; neighbour++)
                {
                    float column = baseColumn + neighbour;
                    float jitter = hash21(float2(column, 3.71));
                    float spread = lerp(0.3, 1.15, uv.y);
                    float across = 1.0 - smoothstep(0.0, spread,
                        abs(columnPosition - (column + jitter)));

                    float lift = 0.0;
                    [unroll]
                    for (int puff = 0; puff < 2; puff++)
                    {
                        lift = max(lift, plumeSegment(column, uv.y, time, (float)puff));
                    }

                    plumeBody = max(plumeBody, across * lift);
                }

                // Texture the plumes with the wisp noise so they are ragged smoke, not
                // smooth blobs, and let them fade out over the same height as the wisps.
                plumeBody *= smoothstep(0.3, 0.72, wisps) * reach * _Plumes;

                // The quad's bottom edge is sunk below the waterline, so fade the fog in
                // across that overlap. Without it the mesh boundary reads as a ruled
                // line of mist lying on the water.
                float emergence = smoothstep(0.0, 0.07, uv.y);

                // The bed is held down relative to the other two: it is the term with no
                // shape to it, so leaning on it turns the whole effect into flat haze.
                // The wisps and plumes are what actually read as fog rising.
                float fog = (mistBed * 0.8 + wispBody * 0.8 + plumeBody * 0.95)
                    * _Density
                    * emergence;

                float motes = moteField(st, time, 0.0) + moteField(st, time, 31.7);
                // Specks die off far lower than the mist does - they are heavy scraps
                // lifted a little way off the water, not part of the drifting bank.
                motes *= (1.0 - smoothstep(0.04, 0.42, uv.y)) * emergence * _MoteAmount;

                float edgeFade = smoothstep(0.0, _EdgeFade, uv.x)
                    * smoothstep(0.0, _EdgeFade, 1.0 - uv.x);

                half3 color = lerp(_FogColor.rgb, _MoteColor.rgb, saturate(motes * 1.6));
                half alpha = saturate(saturate(fog) * _FogColor.a + motes)
                    * edgeFade
                    * _Strength;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
