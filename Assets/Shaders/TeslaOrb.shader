Shader "TowerDefense/TeslaOrb"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1, 1, 1, 1)
        _GlowColor ("Glow Color", Color) = (1, 0.85, 0.25, 1)
        _Intensity ("Intensity", Range(0, 2)) = 1
        _CoreRadius ("Core Radius", Range(0, 1)) = 0.22
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1.4
        _CrackleSpeed ("Crackle Speed", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Premultiplied, matching the lightning: the core occludes, the halo
        // both adds light and tints the background so it reads everywhere.
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Orb"
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
                half4 _CoreColor;
                half4 _GlowColor;
                float _Intensity;
                float _CoreRadius;
                float _GlowStrength;
                float _CrackleSpeed;
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

            half4 frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                float radius = length(centered);
                float angle = atan2(centered.y, centered.x);

                // Two counter-rotating ripples crawl around the rim so the
                // surface arcs like contained electricity, not a static disc.
                float wobble =
                    sin(angle * 7.0 + _Time.y * _CrackleSpeed) * 0.05
                    + sin(angle * 11.0 - _Time.y * (_CrackleSpeed * 1.7)) * 0.035;
                float r = saturate(radius + wobble);

                float core = 1.0 - smoothstep(_CoreRadius, _CoreRadius + 0.12, r);
                float glow = pow(saturate(1.0 - r), 2.0) * _GlowStrength;

                float flicker = 0.85 + 0.15 * hash21(float2(
                    floor(_Time.y * 30.0), 1.7));

                half3 rgb = (_CoreColor.rgb * core + _GlowColor.rgb * glow)
                    * _Intensity * flicker;
                half coverage = saturate(core + glow * 0.6)
                    * saturate(_Intensity);
                return half4(rgb, coverage);
            }
            ENDHLSL
        }
    }
}
