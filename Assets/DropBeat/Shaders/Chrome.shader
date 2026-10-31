Shader "DropBeat/Chrome"
{
    // The ball. Default skin: polished metal reflecting a procedural neon "studio" environment (no
    // reflection probes or lights needed), Schlick fresnel, sharp highlights that drive the bloom, and
    // engraved glowing seams in object space so spin is visible. _Skin picks other looks: gold, black
    // chrome, rose gold, plasma, lava, disco mirror ball, pixel, neon wireframe, planet.
    Properties
    {
        _BaseColor ("Metal Tint (F0)", Color) = (0.93, 0.94, 0.98, 1)
        [HDR] _Accent ("Neon Reflection", Color) = (0, 2.2, 2.6, 1)
        [HDR] _SeamColor ("Seam Glow", Color) = (0, 1.6, 2.0, 1)
        _SeamWidth ("Seam Width", Range(0, 0.2)) = 0.03
        [HDR] _Flash ("Hit Flash", Color) = (0, 0, 0, 0)
        _Exposure ("Exposure", Float) = 1.25
        _Skin ("Skin", Float) = 0
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
                float _Skin;
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

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float3 Metal(float3 n, float3 v, float3 f0, float exposure)
            {
                float3 r = reflect(-v, n);
                float ndv = saturate(dot(n, v));
                float3 fresnel = f0 + (1.0 - f0) * pow(1.0 - ndv, 5.0);
                return Environment(r) * fresnel * exposure;
            }

            float Seams(float3 no, float w, out float groove)
            {
                float seamA = 1.0 - smoothstep(w * 0.45, w, abs(no.y));
                float seamB = 1.0 - smoothstep(w * 0.45, w, abs(no.x));
                groove = 1.0 - smoothstep(w, w * 2.2, min(abs(no.y), abs(no.x)));
                return saturate(seamA + seamB);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float ndv = saturate(dot(n, v));
                float rim = pow(1.0 - ndv, 2.0);
                float3 no = normalize(i.normalOS);
                float w = max(1e-4, _SeamWidth);
                float t = _Time.y;
                int skin = (int)round(_Skin);
                float3 col;
                float groove;

                if (skin == 4)
                {
                    // Plasma: dark glass shell, swirling energy inside, bright rim.
                    float3 q = no * 2.2 + float3(0.0, t * 0.6, t * 0.35);
                    float e = Noise3(q) * 0.65 + Noise3(q * 2.3 + 4.1) * 0.35;
                    float filaments = pow(1.0 - abs(e * 2.0 - 1.0), 6.0);
                    col = Metal(n, v, float3(0.04, 0.04, 0.05), _Exposure) * 0.6;
                    col += _Accent.rgb * (0.12 + 0.9 * filaments) * (0.6 + 0.4 * ndv);
                    col += _Accent.rgb * rim * 0.6;
                }
                else if (skin == 5)
                {
                    // Lava: dark cooled rock with glowing cracks that pulse.
                    float3 q = no * 3.0;
                    float c1 = abs(Noise3(q) * 2.0 - 1.0);
                    float c2 = abs(Noise3(q * 2.1 + 7.3) * 2.0 - 1.0);
                    float crack = 1.0 - smoothstep(0.0, 0.12, min(c1, c2 * 1.3));
                    float heat = 0.75 + 0.25 * sin(t * 3.0 + no.y * 4.0);
                    float3 rock = float3(0.06, 0.035, 0.03) * (0.4 + 0.6 * saturate(dot(n, normalize(float3(-0.4, 0.8, -0.4)))));
                    col = rock + Metal(n, v, float3(0.03, 0.03, 0.03), _Exposure) * 0.35;
                    col += float3(3.0, 0.9, 0.15) * crack * heat;
                    col += float3(1.6, 0.35, 0.05) * rim * 0.35;
                }
                else if (skin == 6)
                {
                    // Disco: mirror tiles in object space (they turn with the spin), dark grout, sparkles.
                    float lat = asin(clamp(no.y, -1.0, 1.0));
                    float lon = atan2(no.z, no.x);
                    float rows = 10.0;
                    float cl = (floor(lat / 3.14159 * rows) + 0.5) / rows * 3.14159;
                    float cols = max(4.0, floor(rows * 2.0 * cos(cl)));
                    float cn = (floor(lon / 6.28318 * cols) + 0.5) / cols * 6.28318;
                    float3 tileOS = float3(cos(cl) * cos(cn), sin(cl), cos(cl) * sin(cn));
                    float3 tn = normalize(TransformObjectToWorldNormal(tileOS));
                    col = Metal(tn, v, float3(0.9, 0.9, 0.95), _Exposure * 1.15);
                    float fl = frac(lat / 3.14159 * rows), fn = frac(lon / 6.28318 * cols);
                    float grout = step(0.08, fl) * step(fl, 0.92) * step(0.08, fn) * step(fn, 0.92);
                    col *= 0.25 + 0.75 * grout;
                    float id = Hash3(floor(float3(lat * 40.0, lon * 40.0, floor(t * 8.0))));
                    col += float3(3.0, 3.0, 3.2) * step(0.985, id) * grout;
                }
                else if (skin == 7)
                {
                    // Pixel: chunky voxel facets with a posterised palette (the 8-bit ball).
                    float3 qn = normalize(round(no * 2.0));
                    float3 pn = normalize(TransformObjectToWorldNormal(qn));
                    float3 base = lerp(_Accent.rgb * 0.18, float3(1.0, 1.0, 1.0), step(0.5, Hash3(round(no * 2.0) + 3.0)) * 0.15);
                    float light = saturate(dot(pn, normalize(float3(-0.5, 0.8, -0.4))));
                    col = base * (0.35 + 0.9 * light) + Metal(pn, v, float3(0.5, 0.5, 0.55), _Exposure) * 0.35;
                    col = floor(col * 6.0 + 0.5) / 6.0;
                }
                else if (skin == 8)
                {
                    // Neon wire: matte dark sphere with a glowing latitude / longitude grid.
                    float lat = asin(clamp(no.y, -1.0, 1.0)) / 3.14159 * 8.0;
                    float lon = atan2(no.z, no.x) / 6.28318 * 12.0;
                    float gl = min(abs(frac(lat) - 0.5), abs(frac(lon) - 0.5));
                    col = float3(0.02, 0.02, 0.035) + Metal(n, v, float3(0.05, 0.05, 0.06), _Exposure) * 0.4;
                    col += _Accent.rgb * (1.0 - smoothstep(0.02, 0.08, gl)) * 1.1;
                    col += _Accent.rgb * rim * 0.4;
                }
                else if (skin == 9)
                {
                    // Planet: oceans and continents (3D noise on the object), clouds, atmosphere glow.
                    float h = Noise3(no * 2.4) * 0.6 + Noise3(no * 5.1 + 2.0) * 0.3 + Noise3(no * 11.0) * 0.1;
                    float land = smoothstep(0.5, 0.53, h);
                    float3 ocean = float3(0.02, 0.09, 0.32);
                    float3 ground = lerp(float3(0.08, 0.22, 0.05), float3(0.32, 0.25, 0.12), smoothstep(0.55, 0.7, h));
                    ground = lerp(ground, float3(0.85, 0.88, 0.95), smoothstep(0.82, 0.9, abs(no.y))); // ice caps
                    float light = saturate(dot(n, normalize(float3(-0.6, 0.6, -0.5)))) * 0.85 + 0.15;
                    col = lerp(ocean, ground, land) * light * 1.6;
                    col += Metal(n, v, float3(0.04, 0.04, 0.05), _Exposure) * (1.0 - land) * 0.5; // sea glint
                    float cloud = smoothstep(0.55, 0.75, Noise3(no * 3.3 + float3(t * 0.05, 0.0, t * 0.03)));
                    col = lerp(col, float3(0.9, 0.92, 1.0) * light * 1.4, cloud * 0.8);
                    col += float3(0.25, 0.55, 1.6) * pow(1.0 - ndv, 3.0) * 1.2;
                }
                else if (skin == 10)
                {
                    // Champion (first Hard clear): a cut diamond. Facets reflect the studio, each one throws
                    // its own rainbow fire that shifts with the view and the spin, and gold seams glow.
                    float3 qn = normalize(round(no * 3.0));
                    float3 fn = normalize(TransformObjectToWorldNormal(qn));
                    float facet = Hash3(round(no * 3.0) + 11.0);
                    float h = frac(facet + dot(fn, v) * 0.6 + t * 0.08);
                    float3 fire = saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                    col = Metal(fn, v, float3(0.75, 0.78, 0.85), _Exposure) * 0.85;
                    col += fire * (0.35 + 0.9 * pow(saturate(dot(fn, v)), 3.0));
                    col += fire * rim * 0.8;
                    float sparkle = step(0.97, Hash3(floor(float3(facet * 50.0, floor(t * 6.0), 0.0))));
                    col += float3(2.5, 2.5, 2.6) * sparkle * saturate(dot(fn, v));
                    float seam = Seams(no, w, groove);
                    col += float3(2.4, 1.6, 0.35) * seam * (0.8 + 0.4 * sin(t * 3.0));
                }
                else
                {
                    // Metals: chrome (0), gold (1), black chrome (2), rose gold (3). Tint comes from _BaseColor.
                    float3 f0 = _BaseColor.rgb;
                    col = Metal(n, v, f0, _Exposure);
                    if (skin == 1 || skin == 3) col *= lerp(float3(1.0, 1.0, 1.0), f0 * 1.15, 0.55); // warm the reflections
                    float seam = Seams(no, w, groove);
                    col *= 1.0 - 0.55 * groove;
                    col += _SeamColor.rgb * seam * (skin == 2 ? 1.6 : 0.9);
                }

                col += _Flash.rgb * (0.35 + 0.65 * rim);
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
