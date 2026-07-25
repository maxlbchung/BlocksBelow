Shader "TowerDefense/TeslaLightning"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1, 1, 1, 1)
        _GlowColor ("Glow Color", Color) = (1, 0.85, 0.25, 1)
        _CoreSharpness ("Core Sharpness", Float) = 3
        _BodyWidth ("Body Width", Range(0, 1)) = 0.35
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1.6
        _BloomBoost ("Bloom Boost", Range(1, 8)) = 4
        _ShimmerSpeed ("Shimmer Speed", Float) = 16
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.25
        _FlickerSpeed ("Flicker Speed", Float) = 40
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Premultiplied: the solid body occludes what is behind it like an
        // opaque sprite line, while the rim beyond it only adds light. A pure
        // additive blend made the bolt wash out to a thin sliver over bright
        // backgrounds.
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Lightning"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _GlowColor;
                float _CoreSharpness;
                float _BodyWidth;
                float _GlowStrength;
                float _BloomBoost;
                float _ShimmerSpeed;
                float _FlickerAmount;
                float _FlickerSpeed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // The line mesh puts uv.y at 0 on one edge and 1 on the other,
                // so this is 0 at the center of the bolt and 1 at both edges.
                float across = abs(input.uv.y * 2.0 - 1.0);
                float profile = saturate(1.0 - across);

                // Solid body like the original flat line so the bolt keeps its
                // thickness; only the rim outside it is a soft glow.
                float body = 1.0 - smoothstep(
                    _BodyWidth - 0.15,
                    _BodyWidth + 0.15,
                    across);
                float core = pow(profile, _CoreSharpness);
                float glow = pow(profile, 1.6) * _GlowStrength * (1.0 - body);

                // Brightness shivers in patches along the bolt rather than the
                // whole strike pulsing at once.
                float flicker = 1.0 - _FlickerAmount * hash21(float2(
                    floor(input.uv.x * 5.0),
                    floor(_Time.y * _FlickerSpeed)
                ));

                // Two out-of-phase waves crawl along the bolt so bright pulses
                // visibly travel from the tower toward the target.
                float shimmer = 0.78
                    + 0.22 * sin(input.uv.x * 26.0 - _Time.y * _ShimmerSpeed)
                    * sin(input.uv.x * 9.0 + _Time.y * (_ShimmerSpeed * 0.63));

                half fade = input.color.a;
                half3 bodyRgb = lerp(_GlowColor.rgb, _CoreColor.rgb, core)
                    * input.color.rgb;
                half3 glowRgb = _GlowColor.rgb * input.color.rgb;

                // The whole bolt is pushed into HDR so the bloom pass sees it.
                // A thin line covers few pixels and its energy dilutes when
                // bloom downsamples, so it needs to sit well above the
                // threshold - a value just past 1.0 reads as no glow at all.
                // On screen the core clamps to white-hot; the rim keeps its
                // hue because color ratios survive the multiply.
                half3 rgb = (bodyRgb * body + glowRgb * glow)
                    * fade * flicker * shimmer * _BloomBoost;
                // The halo carries some opacity so it tints what is behind it
                // instead of only adding light; an add-only halo disappears
                // against bright backgrounds.
                half coverage = saturate(body + glow * 0.6) * fade;
                return half4(rgb, coverage);
            }
            ENDHLSL
        }
    }
}
