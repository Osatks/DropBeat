Shader "DropBeat/NeonAdditive"
{
    // Additive glow for lines, halos, particles and the backdrop.
    // _Soft = 1 turns a quad into a soft round glow using its UVs (no texture needed); 2 softens only across
    // the width, for trails and lines whose UVs run along their length.
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Soft ("Soft Circle", Float) = 0
        _SoftPower ("Soft Power", Float) = 2
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
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Soft;
                half _SoftPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = 1.0h;
                if (_Soft > 0.5h)
                {
                    float2 d = i.uv * 2.0 - 1.0;
                    // 1 = round glow (quads, particles); 2 = soft across the width only (trails, lines).
                    a = _Soft > 1.5h ? saturate(1.0 - d.y * d.y) : saturate(1.0 - dot(d, d));
                    a = pow(a, _SoftPower);
                }
                half3 c = i.color.rgb * _Color.rgb * (a * i.color.a * _Color.a);
                return half4(c, 1.0h);
            }
            ENDHLSL
        }
    }
}
