Shader "TowerDefense/EnemyBolt"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1, 1, 0.88, 1)
        _BoltColor ("Bolt Color", Color) = (1, 0.92, 0.15, 1)
        _Intensity ("Intensity", Range(0, 2)) = 1
        _CoreRadius ("Core Radius", Range(0, 1)) = 0.16
        _ArcReach ("Arc Reach", Range(0, 1)) = 0.55
        _ArcCount ("Arc Count", Float) = 5
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1.1
        _CrackleSpeed ("Crackle Speed", Float) = 16
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Premultiplied, matching the tower orb: the core occludes what is behind
        // it while the arcs and halo add light on top of the sky.
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Bolt"
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
                half4 _BoltColor;
                float _Intensity;
                float _CoreRadius;
                float _ArcReach;
                float _ArcCount;
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

                // How far the discharge throws out at this angle. Two waves running
                // around the bolt at different rates and directions keep any one
                // tendril from holding its shape for more than a frame or two.
                float lash =
                    (0.5 + 0.5 * sin(angle * _ArcCount + _Time.y * _CrackleSpeed))
                    * (0.55 + 0.45 * sin(angle * (_ArcCount * 1.7)
                        - _Time.y * (_CrackleSpeed * 0.6)));
                float reach = _CoreRadius + _ArcReach * lash;

                // The tendrils themselves: everything inside the reach at this angle,
                // dimming as it runs out so they taper to points instead of ending flat.
                float arcs = (1.0 - smoothstep(reach - 0.1, reach, radius))
                    * pow(saturate(1.0 - radius), 1.5);

                float core = 1.0 - smoothstep(_CoreRadius, _CoreRadius + 0.1, radius);
                float glow = pow(saturate(1.0 - radius), 3.0) * _GlowStrength;

                // Stepped rather than smooth, so the whole bolt strobes the way a
                // spark gap does instead of breathing.
                float flicker = 0.8 + 0.2 * hash21(float2(floor(_Time.y * 40.0), 3.1));

                half3 rgb = (_CoreColor.rgb * core + _BoltColor.rgb * (arcs + glow))
                    * _Intensity * flicker;
                half coverage = saturate(core + arcs * 0.85 + glow * 0.5)
                    * saturate(_Intensity);
                return half4(rgb, coverage);
            }
            ENDHLSL
        }
    }
}
