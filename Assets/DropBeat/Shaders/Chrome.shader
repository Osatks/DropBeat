Shader "DropBeat/Chrome"
{
    // Polished metal ball. Reflects a procedural neon "studio" environment (no reflection probes or
    // lights needed, so it reads as chrome even in a dark scene), with Schlick fresnel, sharp
    // highlights that drive the bloom, and engraved glowing seams in object space so spin is visible.
    Properties
    {
        _BaseColor ("Metal Tint (F0)", Color) = (0.93, 0.94, 0.98, 1)
        [HDR] _Accent ("Neon Reflection", Color) = (0, 2.2, 2.6, 1)
        [HDR] _SeamColor ("Seam Glow", Color) = (0, 1.6, 2.0, 1)
        _SeamWidth ("Seam Width", Range(0, 0.2)) = 0.03
        [HDR] _Flash ("Hit Flash", Color) = (0, 0, 0, 0)
        _Exposure ("Exposure", Float) = 1.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Accent;
                half4 _SeamColor;
                half _SeamWidth;
                half4 _Flash;
                half _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.normalOS = v.normalOS;
                return o;
            }

            float3 Environment(float3 d)
            {
                float y = d.y;
                // Dark violet floor fading to a deep night sky, brightening to silver overhead
                // (the light top / dark bottom split is what makes a sphere read as chrome).
                float3 c = lerp(float3(0.015, 0.008, 0.04), float3(0.07, 0.045, 0.15), saturate(y * 0.5 + 0.5));
                c += float3(0.55, 0.56, 0.68) * pow(saturate(y), 1.4) * 0.75;
                // Thin neon horizon in the song colour, plus a pink band just below it.
                c += _Accent.rgb * exp(-(y * 10.0) * (y * 10.0)) * 0.45;
                c += float3(2.2, 0.3, 1.7) * exp(-((y + 0.3) * 12.0) * ((y + 0.3) * 12.0)) * 0.22;
                // Overhead studio softbox (key light) - the main white glint.
                float3 key = normalize(float3(-0.45, 0.75, -0.5));
                float k = saturate(dot(d, key));
                c += pow(k, 90.0) * 9.0 + pow(k, 8.0) * 0.4;
                // Second, softer softbox on the right for a secondary sparkle.
                float k2 = saturate(dot(d, normalize(float3(0.6, 0.45, -0.65))));
                c += pow(k2, 45.0) * 2.5;
                // Accent kicker from behind-right gives the coloured rim.
                float3 kick = normalize(float3(0.75, 0.15, 0.65));
                c += _Accent.rgb * pow(saturate(dot(d, kick)), 14.0) * 0.8;
                // Small cool fill from the lower left.
                float3 fill = normalize(float3(-0.8, -0.3, -0.4));
                c += float3(0.35, 0.45, 0.9) * pow(saturate(dot(d, fill)), 6.0) * 0.5;
                return c;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float3 r = reflect(-v, n);
                float ndv = saturate(dot(n, v));

                float3 f0 = _BaseColor.rgb;
                float3 fresnel = f0 + (1.0 - f0) * pow(1.0 - ndv, 5.0);
                float3 col = Environment(r) * fresnel * _Exposure;

                // Engraved seams: equator + one meridian in object space (they turn with the ball).
                float3 no = normalize(i.normalOS);
                float w = max(1e-4, _SeamWidth);
                float seamA = 1.0 - smoothstep(w * 0.45, w, abs(no.y));
                float seamB = 1.0 - smoothstep(w * 0.45, w, abs(no.x));
                float seam = saturate(seamA + seamB);
                float groove = 1.0 - smoothstep(w, w * 2.2, min(abs(no.y), abs(no.x)));
                col *= 1.0 - 0.55 * groove;               // dark groove around the seam
                col += _SeamColor.rgb * seam * 0.9;        // glowing line inside it

                col += _Flash.rgb * (0.35 + 0.65 * pow(1.0 - ndv, 2.0));
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
