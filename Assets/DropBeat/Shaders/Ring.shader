Shader "DropBeat/Ring"
{
    // Segmented neon ring drawn with a signed distance field on one quad (crisp at any size, cheap).
    //  _Fill    0..1  segments light up clockwise from the top: the ring "charges" until the beat.
    //  _Burst   0..1  hit animation: the ring expands, segments split apart and fade.
    //  _Zone    0..1  soft glow inside the ring (landing zone of the target).
    // Additive, so it glows with bloom; the ball occludes the part of the ring behind it.
    Properties
    {
        [HDR] _Color ("Color", Color) = (0, 2.2, 2.6, 1)
        [HDR] _FillColor ("Charged Color", Color) = (2.5, 2.4, 2.2, 1)
        _Fill ("Charge", Range(0, 1)) = 0
        _Burst ("Burst", Range(0, 1)) = 0
        _Zone ("Landing Zone", Range(0, 1)) = 0
        _Segments ("Segments (0 = solid)", Float) = 8
        _Gap ("Gap", Range(0, 0.5)) = 0.12
        _Thickness ("Half Thickness", Range(0.005, 0.2)) = 0.07
        _Radius ("Radius", Range(0.1, 0.9)) = 0.6
        _Spin ("Spin (turns)", Float) = 0
        _Arrow ("Arrow (x = on, y = angle rad, 0 = up)", Vector) = (0, 0, 0, 0)
        _BandScale ("Band (0 = glow only, the 3D band draws the ring)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
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
                half _Zone;
                float _Segments;
                half _Gap;
                half _Thickness;
                half _Radius;
                float _Spin;
                float4 _Arrow;
                half _BandScale;
            CBUFFER_END

            float SegDist(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

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
                o.p = v.uv * 2.0 - 1.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                const float TAU = 6.28318530718;
                float2 p = i.p;
                float r = length(p);
                // Angle measured clockwise from the top, 0..1.
                float u = atan2(p.x, p.y) / TAU;
                u = u < 0.0 ? u + 1.0 : u;

                float burst = _Burst;
                float R = _Radius + burst * 0.3;
                float T = _Thickness * (1.0 - burst * 0.55);
                float d = abs(r - R) - T;
                float aa = max(fwidth(r), 1e-4) * 1.2;
                float band = 1.0 - smoothstep(-aa, aa, d);

                float seg = 1.0;
                if (_Segments > 0.5)
                {
                    float su = frac(u + _Spin) * _Segments;
                    float f = frac(su);
                    float g = (_Gap + burst * 0.5) * 0.5;
                    // AA width from the position (su jumps where the angle wraps, so fwidth(su) would draw a seam there).
                    float du = length(fwidth(p)) / (TAU * max(r, 1e-3));
                    float aaS = max(du * _Segments, 1e-4) * 1.2;
                    seg = smoothstep(g - aaS, g + aaS, f) * smoothstep(g - aaS, g + aaS, 1.0 - f);
                }

                float lit = step(u, _Fill);
                float3 col = lerp(_Color.rgb * 0.4, _FillColor.rgb, lit) * band * seg * _BandScale;
                // Bright inner edge on the charged part.
                col += _FillColor.rgb * exp(-abs(r - (R - T)) * 80.0) * 0.4 * seg * lit * _BandScale;
                // Soft outer glow.
                // Faded out before the quad's edge so no square outline shows when the ring faces the camera.
                col += _Color.rgb * exp(-max(d, 0.0) * 9.0) * 0.16 * (0.45 + 0.55 * lit) * (seg * 0.7 + 0.3) * (1.0 - smoothstep(0.8, 1.0, r));
                // Landing zone: faint disc inside the ring.
                col += _Color.rgb * (1.0 - smoothstep(0.0, max(0.01, R - T), r)) * 0.05 * _Zone;

                // Swipe rings: double chevron pointing the swipe direction.
                if (_Arrow.x > 0.5)
                {
                    float ca = cos(_Arrow.y);
                    float sa = sin(_Arrow.y);
                    float2 q = float2(ca * p.x - sa * p.y, sa * p.x + ca * p.y);
                    q.x = abs(q.x);
                    float d1 = SegDist(q, float2(0.0, 0.2), float2(0.2, 0.0));
                    float d2 = SegDist(q + float2(0.0, 0.18), float2(0.0, 0.2), float2(0.2, 0.0));
                    float dd = min(d1, d2) - 0.045;
                    float arrow = 1.0 - smoothstep(-aa, aa, dd);
                    col += _FillColor.rgb * arrow * (0.55 + 0.45 * lit);
                }

                col *= _Color.a * (1.0 - burst * burst);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
