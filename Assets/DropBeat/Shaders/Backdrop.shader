Shader "DropBeat/Backdrop"
{
    // Full-view backdrop drawn first: night gradient, slow-drifting nebula (3-octave value noise with
    // camera parallax) and a striped synthwave sun that breathes with the beat. One pass, no textures,
    // cheap enough for phones.
    Properties
    {
        [HDR] _Accent ("Accent", Color) = (0, 2.2, 2.6, 1)
        _Pulse ("Beat Pulse", Range(0, 1)) = 0
        _Scroll ("Parallax Scroll (xy)", Vector) = (0, 0, 0, 0)
        _QuadSize ("Quad Size (world)", Vector) = (260, 160, 0, 0)
        _SunCenter ("Sun Centre (world, rel. to view)", Vector) = (0, 13, 0, 0)
        _SunRadius ("Sun Radius", Float) = 13
        _SunIntensity ("Sun Intensity", Float) = 0.24
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Accent;
                half _Pulse;
                float4 _Scroll;
                float4 _QuadSize;
                float4 _SunCenter;
                float _SunRadius;
                half _SunIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 p : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = (v.uv - 0.5) * _QuadSize.xy;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    v += a * Noise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v / 0.875;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.p;
                float t = _Time.y;

                // Night gradient (relative to the view, so it never runs out while climbing).
                float g = saturate(p.y / 60.0 + 0.5);
                float3 col = lerp(float3(0.008, 0.006, 0.025), float3(0.045, 0.012, 0.085), g);

                // Nebula clouds drifting slowly, scrolled by the camera for depth.
                float2 q = (p + _Scroll.xy) * 0.03 + float2(t * 0.012, t * 0.005);
                float n1 = Fbm(q);
                float n2 = Fbm(q * 1.8 + float2(3.1, -1.7));
                float glow = 1.0 + 0.6 * _Pulse;
                // Kept dark and mostly violet so the cyan rings always stand out against it.
                col += (_Accent.rgb * 0.25 + float3(0.1, 0.05, 0.35)) * 0.035 * smoothstep(0.45, 0.95, n1) * glow;
                col += float3(0.4, 0.05, 0.45) * 0.03 * smoothstep(0.5, 0.95, n2) * glow;

                // Synthwave sun: warm-to-pink gradient, horizontal cuts in its lower half.
                float2 d = (p - _SunCenter.xy) / _SunRadius;
                float r = length(d);
                float disk = 1.0 - smoothstep(0.985, 1.0, r);
                float yy = d.y;
                float gap = saturate((0.15 - yy) * 0.5);
                float bands = frac(yy * 7.0 - t * 0.12);
                float cut = yy < 0.15 ? step(gap, bands) : 1.0;
                float3 sunCol = lerp(float3(2.2, 0.25, 1.0), float3(2.4, 1.3, 0.35), saturate(yy * 0.5 + 0.5));
                float breathe = 0.85 + 0.35 * _Pulse;
                col += sunCol * disk * cut * _SunIntensity * breathe;
                // Soft halo around the sun.
                col += float3(1.6, 0.35, 0.9) * exp(-max(0.0, r - 1.0) * 3.5) * 0.06 * breathe * step(1.0, r);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
