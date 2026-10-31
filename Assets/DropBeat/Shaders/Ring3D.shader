Shader "DropBeat/Ring3D"
{
    // Solid segmented neon ring (ProceduralMeshes.RingBand). Same parameters as DropBeat/Ring so one property
    // block drives both: the band gives the ring real thickness and depth when the camera changes angle, the
    // Ring quad behind it adds the glow halo, landing zone and swipe arrow.
    //  Segments are cut with clip(); charged segments glow with _FillColor; the walls are shaded darker than
    //  the faces with a rim light, so the shape reads as 3D. Writes depth, so the ball passes through it.
    Properties
    {
        [HDR] _Color ("Color", Color) = (0, 2.2, 2.6, 1)
        [HDR] _FillColor ("Charged Color", Color) = (2.5, 2.4, 2.2, 1)
        _Fill ("Charge", Range(0, 1)) = 0
        _Burst ("Burst", Range(0, 1)) = 0
        _Segments ("Segments (0 = solid)", Float) = 8
        _Gap ("Gap", Range(0, 0.5)) = 0.12
        _Spin ("Spin (turns)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite On
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FillColor;
                half _Fill;
                half _Burst;
                float _Segments;
                half _Gap;
                float _Spin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;
                p.xy *= 1.0 + _Burst * 0.5; // hit: the ring expands like the quad version
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float burst = _Burst;
                float u = i.uv.x;
                if (_Segments > 0.5)
                {
                    float f = frac(frac(u + _Spin) * _Segments);
                    float g = (_Gap + burst * 0.5) * 0.5;
                    clip(min(f, 1.0 - f) - g);
                }

                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 N = normalize(i.normalWS);
                N = dot(N, V) < 0.0 ? -N : N;
                float ndv = saturate(dot(N, V));

                float lit = step(u, _Fill);
                float3 base = lerp(_Color.rgb * 0.45, _FillColor.rgb, lit);

                float face = i.uv.y;
                bool flat = face < 0.5 || (face > 1.5 && face < 2.5); // front / back faces
                bool inner = face > 2.5;

                // Key light from upper front plus a rim: faces bright, walls darker, edges glowing.
                float3 L = normalize(float3(-0.35, 0.75, -0.55));
                float diff = saturate(dot(N, L)) * 0.6 + 0.4;
                float3 col = base * diff * (flat ? 1.0 : 0.55);
                col += base * pow(1.0 - ndv, 3.0) * 0.9;
                // Charged segments: white-hot inner wall.
                col += _FillColor.rgb * (inner ? 0.6 : 0.0) * lit;

                float fade = 1.0 - burst * burst;
                float intensity = _Color.a * fade;
                float alpha = saturate(_Color.a * 1.8) * fade;
                clip(alpha - 0.05); // a fading band must not keep writing depth over later glows and particles
                return half4(col * intensity, alpha);
            }
            ENDHLSL
        }
    }
}
