Shader "DropBeat/Backdrop"
{
    // Full-view backdrop drawn first: a sky gradient with drifting nebula, then one procedural scene picked
    // per song (_Scene): synthwave sun, outrun mountains, aurora over ice, city skyline, misty mountains,
    // pixel sky, underwater, petals, volcano, galaxy, jungle, disco beams, desert dunes.
    // One pass, no textures, a handful of noise lookups per pixel: cheap enough for phones.
    // p is in world units on the backdrop quad (y up, origin at the view centre); the horizon sits just
    // below the sun so silhouettes frame the bottom of the view.
    Properties
    {
        [HDR] _Accent ("Accent", Color) = (0, 2.2, 2.6, 1)
        [HDR] _Accent2 ("Accent 2", Color) = (2.6, 0.35, 1.9, 1)
        _Pulse ("Beat Pulse", Range(0, 1)) = 0
        _Scroll ("Parallax Scroll (xy)", Vector) = (0, 0, 0, 0)
        _QuadSize ("Quad Size (world)", Vector) = (260, 160, 0, 0)
        _SunCenter ("Sun Centre (world, rel. to view)", Vector) = (0, 13, 0, 0)
        _SunRadius ("Sun Radius", Float) = 13
        _SunIntensity ("Sun Intensity", Float) = 0.24
        _SkyTop ("Sky Top", Color) = (0.045, 0.012, 0.085, 1)
        _SkyBottom ("Sky Bottom", Color) = (0.008, 0.006, 0.025, 1)
        [HDR] _SunTop ("Sun Top", Color) = (2.4, 1.3, 0.35, 1)
        [HDR] _SunBottom ("Sun Bottom", Color) = (2.2, 0.25, 1.0, 1)
        _Haze ("Nebula", Color) = (0.4, 0.05, 0.45, 1)
        _Scene ("Scene", Float) = 0
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
                half4 _Accent2;
                half _Pulse;
                float4 _Scroll;
                float4 _QuadSize;
                float4 _SunCenter;
                float _SunRadius;
                half _SunIntensity;
                half4 _SkyTop;
                half4 _SkyBottom;
                half4 _SunTop;
                half4 _SunBottom;
                half4 _Haze;
                float _Scene;
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

            // Twinkling point stars, one candidate per cell.
            float Stars(float2 p, float cell, float density, float t)
            {
                float2 c = floor(p / cell);
                float h = Hash(c);
                if (h < 1.0 - density) return 0.0;
                float2 o = (float2(Hash(c + 3.1), Hash(c + 7.7)) * 0.6 + 0.2) * cell;
                float d = length(p - (c * cell + o));
                float tw = 0.55 + 0.45 * sin(t * (1.5 + h * 3.0) + h * 40.0);
                return (1.0 - smoothstep(0.03, 0.16, d)) * tw;
            }

            // Particles drifting with velocity v (units / s): returns a soft dot mask; seed varies the field.
            float Drift(float2 p, float2 v, float cell, float density, float size, float t, float seed)
            {
                float2 q = p - v * t;
                float2 c = floor(q / cell);
                float h = Hash(c + seed);
                if (h < 1.0 - density) return 0.0;
                float2 o = (float2(Hash(c + seed + 1.7), Hash(c + seed + 5.3)) * 0.6 + 0.2) * cell;
                float d = length(q - (c * cell + o));
                return 1.0 - smoothstep(size * 0.4, size, d);
            }

            // Striped synthwave sun (the classic look); returns its colour contribution.
            float3 SynthSun(float2 p, float2 sc, float R, float t, float breathe, float sunScale)
            {
                float2 d = (p - sc) / R;
                float r = length(d);
                float disk = 1.0 - smoothstep(0.985, 1.0, r);
                float yy = d.y;
                float gap = saturate((0.15 - yy) * 0.5);
                float bands = frac(yy * 7.0 - t * 0.12);
                float cut = yy < 0.15 ? step(gap, bands) : 1.0;
                float3 sunCol = lerp(_SunBottom.rgb, _SunTop.rgb, saturate(yy * 0.5 + 0.5));
                float3 c = sunCol * disk * cut * _SunIntensity * breathe;
                c += _SunBottom.rgb * 0.75 * exp(-max(0.0, r - 1.0) * 3.5) * 0.06 * breathe * step(1.0, r) * sunScale;
                return c;
            }

            // Plain soft disc (moon / hazy sun) with a halo.
            float3 Disc(float2 p, float2 c, float R, float3 colTop, float3 colBottom, float strength, float breathe)
            {
                float2 d = (p - c) / R;
                float r = length(d);
                float disk = 1.0 - smoothstep(0.97, 1.0, r);
                float3 dc = lerp(colBottom, colTop, saturate(d.y * 0.5 + 0.5));
                return dc * disk * strength * breathe + colBottom * 0.05 * exp(-max(0.0, r - 1.0) * 3.0) * breathe * step(1.0, r);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.p;
                float t = fmod(_Time.y, 1200.0); // keep hash inputs small over long sessions (float precision)
                int scene = (int)round(_Scene);
                if (scene == 5) p = floor(p / 1.6) * 1.6 + 0.8; // pixel scene: chunky pixels everywhere

                float2 sc = _SunCenter.xy;
                float R = _SunRadius;
                float horizon = sc.y - 0.35 * R;
                float sunScale = _SunIntensity / 0.24; // dimmed in the tunnel view
                float glow = 1.0 + 0.6 * _Pulse;
                float breathe = 0.85 + 0.35 * _Pulse;
                float3 sil = _SkyBottom.rgb * 0.45;

                // Sky gradient (relative to the view, so it never runs out while climbing).
                float g = saturate(p.y / 60.0 + 0.5);
                float3 col = lerp(_SkyBottom.rgb, _SkyTop.rgb, g);

                // Nebula clouds drifting slowly, scrolled by the camera for depth.
                float2 q = (p + _Scroll.xy) * 0.03 + float2(t * 0.012, t * 0.005);
                float n1 = Fbm(q);
                float n2 = Fbm(q * 1.8 + float2(3.1, -1.7));
                float neb = scene == 6 ? 0.4 : 1.0;
                col += (_Accent.rgb * 0.25 + _Haze.rgb * 0.75) * 0.035 * smoothstep(0.45, 0.95, n1) * glow * neb;
                col += _Haze.rgb * 0.03 * smoothstep(0.5, 0.95, n2) * glow * neb;

                if (scene == 0)
                {
                    // Synthwave: the striped sun.
                    col += SynthSun(p, sc, R, t, breathe, sunScale);
                }
                else if (scene == 1)
                {
                    // Outrun: striped sun sinking behind jagged mountains with neon rims and scan lines.
                    col += Stars(p, 2.2, 0.12, t) * 0.25 * smoothstep(horizon, horizon + 20.0, p.y);
                    col += SynthSun(p, sc, R, t, breathe, sunScale);
                    float h = horizon - 2.0 + 10.0 * pow(Noise(float2(p.x * 0.05, 1.7)), 1.5) + 1.8 * Noise(float2(p.x * 0.22, 4.1));
                    if (p.y < h)
                    {
                        col = sil;
                        col += _Accent2.rgb * 0.05 * step(0.88, frac(p.y * 0.7)) * smoothstep(h - 14.0, h, p.y);
                    }
                    col += _Accent2.rgb * 0.25 * exp(-abs(p.y - h) * 3.0) * glow;
                }
                else if (scene == 2)
                {
                    // Aurora curtains over ice peaks, a cold moon.
                    col += Stars(p, 2.0, 0.18, t) * 0.3;
                    col += Disc(p, sc + float2(-0.7 * R, 0.45 * R), R * 0.32, _SunTop.rgb, _SunBottom.rgb, 0.22 * sunScale, breathe);
                    for (int k = 0; k < 2; k++)
                    {
                        float fk = (float)k;
                        float yc = 16.0 + 9.0 * fk + 9.0 * (Noise(float2(p.x * 0.035 + t * 0.03 + fk * 7.0, fk)) - 0.5);
                        float band = exp(-max(0.0, p.y - yc) / (5.0 + 3.0 * fk)) * smoothstep(yc - 3.0, yc, p.y);
                        float streak = 0.35 + 0.65 * Noise(float2(p.x * 0.7 + t * 0.35, fk * 3.0));
                        col += lerp(_Accent.rgb, _Accent2.rgb, fk) * band * streak * 0.05 * glow;
                    }
                    float tri = 1.0 - abs(frac(p.x * 0.05 + 0.13) - 0.5) * 2.0;
                    float tri2 = 1.0 - abs(frac(p.x * 0.12 + 0.71) - 0.5) * 2.0;
                    float h = horizon - 5.0 + 10.0 * tri * (0.55 + 0.45 * Noise(float2(floor(p.x * 0.05 + 0.13), 4.0))) + 2.5 * tri2;
                    if (p.y < h)
                    {
                        float lit = step(0.5, frac(p.x * 0.05 + 0.13)); // one face of each peak catches the light
                        col = lerp(_SkyTop.rgb * 0.9, _SkyTop.rgb * 2.2 + _Accent.rgb * 0.015, lit * smoothstep(h - 6.0, h, p.y));
                        col *= 0.75;
                    }
                    col += _Accent.rgb * 0.08 * exp(-abs(p.y - h) * 4.0);
                }
                else if (scene == 3)
                {
                    // Cyberpunk skyline: two rows of buildings with flickering windows under a big moon.
                    col += Disc(p, sc, R, _SunTop.rgb, _SunBottom.rgb, 0.17 * sunScale, breathe);
                    float c1 = floor(p.x / 2.6);
                    float hb1 = horizon - 1.0 + Hash(float2(c1, 1.0)) * 11.0;
                    if (p.y < hb1) col = _SkyBottom.rgb * 0.8 + _Haze.rgb * 0.03;
                    float c2 = floor((p.x + 1.3) / 4.2);
                    float hb2 = horizon - 7.0 + Hash(float2(c2, 2.0)) * 13.0;
                    if (p.y < hb2)
                    {
                        col = sil;
                        float2 wg = float2(p.x / 0.9, p.y / 1.3);
                        float2 wc = floor(wg);
                        float2 wf = frac(wg);
                        float lit = step(0.72, Hash(wc + c2 * 13.0)) * step(0.25, wf.x) * step(wf.x, 0.75) * step(0.3, wf.y) * step(wf.y, 0.75);
                        float flick = 0.7 + 0.3 * sin(t * 2.0 + Hash(wc) * 30.0);
                        col += _Accent.rgb * 0.025 * lit * flick;
                        col += _Accent2.rgb * 0.18 * exp(-(hb2 - p.y) * 5.0) * glow;
                    }
                }
                else if (scene == 4)
                {
                    // Misty mountains: three ridges fading into fog under a pale moon.
                    col += Disc(p, sc + float2(0.0, 0.25 * R), R * 0.8, _SunTop.rgb, _SunBottom.rgb, 0.15 * sunScale, breathe);
                    for (int k = 0; k < 3; k++)
                    {
                        float fk = (float)k;
                        // Ridged noise (1 - |2n - 1|) gives pointed peaks; nearer ridges are lower and darker.
                        // Two smooth octaves only: clean, painterly silhouettes instead of jagged noise.
                        float fq = 0.06 + 0.03 * fk;
                        float rn = 0.72 * Noise(float2(p.x * fq + fk * 11.0, fk)) + 0.28 * Noise(float2(p.x * fq * 2.2 + fk * 5.0, fk + 3.0));
                        float ridge = 1.0 - abs(rn * 2.0 - 1.0);
                        float h = horizon + 8.0 - 6.0 * fk + (13.0 - 3.0 * fk) * ridge * ridge;
                        if (p.y < h)
                        {
                            float fog = 0.75 - 0.3 * fk;
                            col = lerp(sil, _Haze.rgb * 0.22 + _SkyTop.rgb * 1.3, fog);
                        }
                        float mist = 0.5 + 0.5 * Noise(float2(p.x * 0.05 + t * 0.06 + fk * 3.0, fk));
                        col += _Haze.rgb * 0.06 * exp(-abs(p.y - (h - 3.5)) / 1.8) * mist;
                    }
                }
                else if (scene == 5)
                {
                    // Pixel arcade: blocky sun (hard edges), chunky clouds and pixel stars (p is quantised).
                    col += Stars(p, 3.2, 0.2, floor(t * 4.0) * 0.25) * 0.35;
                    float2 d = (p - sc) / R;
                    float r = length(d);
                    float disk = step(r, 1.0);
                    float cut = d.y < 0.1 ? step(0.35, frac(d.y * 4.0 - floor(t * 2.0) * 0.25)) : 1.0;
                    col += lerp(_SunBottom.rgb, _SunTop.rgb, step(0.0, d.y)) * disk * cut * _SunIntensity * breathe;
                    float cloud = step(0.62, Noise(float2(p.x * 0.05 + floor(t * 2.0) * 0.05, p.y * 0.15))) * step(horizon + 12.0, p.y);
                    col = lerp(col, _Haze.rgb * 0.12 + _SkyTop.rgb, cloud * 0.8);
                    float h = horizon - 3.0 + 4.0 * step(0.5, Noise(float2(floor(p.x / 4.8), 6.0)));
                    if (p.y < h) col = sil + _Accent.rgb * 0.05 * step(h - 1.7, p.y); // dark ground, one row of pixel grass
                }
                else if (scene == 6)
                {
                    // Deep ocean: slanted light shafts, caustics near the surface, rising bubbles, a seabed.
                    float sx = p.x + p.y * 0.25;
                    float shafts = Noise(float2(sx * 0.12 + t * 0.03, 0.5)) * Noise(float2(sx * 0.31 - t * 0.02, 1.5));
                    shafts = shafts * shafts * smoothstep(-25.0, 40.0, p.y);
                    col += _Accent.rgb * 0.07 * shafts * glow;
                    float ca = sin(p.x * 0.7 + t * 1.3 + sin(p.y * 0.5 + t)) * sin(p.y * 0.8 - t * 1.1 + sin(p.x * 0.4));
                    col += _Accent2.rgb * 0.012 * saturate(ca) * smoothstep(10.0, 40.0, p.y);
                    float2 bq = p - float2(0.0, t * 2.5);
                    float2 bc = floor(bq / 5.0);
                    if (Hash(bc) > 0.72)
                    {
                        float2 o = (float2(Hash(bc + 1.3), Hash(bc + 2.7)) * 0.6 + 0.2) * 5.0;
                        float rr = 0.15 + 0.35 * Hash(bc + 5.1);
                        float dd = length(bq - (bc * 5.0 + o));
                        col += _Accent2.rgb * 0.08 * (1.0 - smoothstep(0.0, 0.08, abs(dd - rr)));
                    }
                    col += Drift(p, float2(0.2, -0.6), 1.7, 0.25, 0.12, t, 9.0) * _Accent.rgb * 0.02;
                    float h = horizon - 12.0 + 3.5 * Noise(float2(p.x * 0.08, 3.0));
                    if (p.y < h) col = sil;
                }
                else if (scene == 7)
                {
                    // Petals: a big pink sun over soft hills, petals tumbling down on the breeze.
                    col += Disc(p, sc, R, _SunTop.rgb, _SunBottom.rgb, 0.22 * sunScale, breathe);
                    float h = horizon - 2.0 + 5.0 * Noise(float2(p.x * 0.05, 9.0));
                    if (p.y < h) col = sil + _Haze.rgb * 0.02;
                    float2 pq = p + float2(-t * 1.2, t * 2.0);
                    float2 pc = floor(pq / 4.0);
                    float ph = Hash(pc);
                    if (ph > 0.6)
                    {
                        float2 o = (float2(Hash(pc + 2.1), Hash(pc + 4.3)) * 0.6 + 0.2) * 4.0;
                        float2 v = pq - (pc * 4.0 + o);
                        float a = t * (1.0 + ph * 2.0) + ph * 6.28;
                        float cs = cos(a), sn = sin(a);
                        v = float2(cs * v.x - sn * v.y, sn * v.x + cs * v.y);
                        float e = length(v / float2(0.38, 0.2));
                        col += _Accent.rgb * 0.09 * (1.0 - smoothstep(0.75, 1.0, e));
                    }
                }
                else if (scene == 8)
                {
                    // Volcano: a glowing cone, lava plume, rising embers and beat-flashes in the smoke.
                    col += _SunBottom.rgb * 0.05 * exp(-max(0.0, p.y - horizon) / 8.0);
                    float smoke = Fbm(float2(p.x * 0.04 + t * 0.02, p.y * 0.05 - t * 0.04));
                    col *= 1.0 - 0.45 * smoothstep(0.45, 0.8, smoke) * smoothstep(horizon, horizon + 25.0, p.y);
                    col += _Accent2.rgb * 0.05 * _Pulse * _Pulse * smoothstep(0.5, 0.85, smoke) * smoothstep(horizon + 10.0, horizon + 35.0, p.y);
                    float x0 = sc.x - 0.6 * R;
                    float cone = horizon + 13.0 - abs(p.x - x0) * 0.85;
                    float h = min(cone, horizon + 10.5 + 0.6 * Noise(float2(p.x * 0.8, 2.0)));
                    h = max(h, horizon - 4.0 + 3.0 * Noise(float2(p.x * 0.07, 7.0)));
                    float plume = exp(-length((p - float2(x0, horizon + 12.5)) / float2(2.6, 7.0)));
                    col += _Accent.rgb * 0.06 * plume * glow;
                    if (p.y < h)
                    {
                        col = sil;
                        float crack = smoothstep(0.86, 0.94, Noise(float2((p.x - x0) * 0.6, p.y * 0.25 - t * 0.3)));
                        col += _Accent.rgb * 0.1 * crack * smoothstep(horizon - 2.0, horizon + 11.0, p.y) * step(abs(p.x - x0), 9.0);
                        col += _Accent.rgb * 0.18 * exp(-(h - p.y) * 1.2) * step(abs(p.x - x0), 2.5);
                    }
                    col += Drift(p, float2(0.4, 2.2), 2.4, 0.22, 0.14, t, 4.0) * _Accent.rgb * 0.06 * smoothstep(horizon + 30.0, horizon, p.y);
                }
                else if (scene == 9)
                {
                    // Galaxy: tilted spiral arms around a bright core, dense starfield.
                    col += Stars(p, 1.6, 0.3, t) * 0.32;
                    float2 d = p - sc;
                    d.y /= 0.55;
                    float r = length(d) / (R * 1.9);
                    float a = atan2(d.y, d.x);
                    float arms = 0.5 + 0.5 * sin(2.0 * a - log(max(r, 0.04)) * 4.5 + t * 0.06);
                    float n = Fbm(d * 0.07 + t * 0.01);
                    float galaxy = arms * arms * arms * exp(-r * 1.7) * (0.45 + n);
                    col += lerp(_Accent.rgb, _Accent2.rgb, saturate(r * 1.2)) * 0.085 * galaxy * glow;
                    col += _SunTop.rgb * 0.12 * exp(-r * 9.0) * breathe * sunScale;
                }
                else if (scene == 10)
                {
                    // Jungle: hazy sun, hills, leafy canopy hanging from the top, fireflies.
                    col += Disc(p, sc, R, _SunTop.rgb, _SunBottom.rgb, 0.2 * sunScale, breathe);
                    float h = horizon - 3.0 + 6.0 * Fbm(float2(p.x * 0.04, 3.0));
                    if (p.y < h) col = sil;
                    // Canopy: rounded leaf clusters (scalloped edge) hanging from the top.
                    float lobes = 1.0 - pow(abs(frac(p.x * 0.18) - 0.5) * 2.0, 2.0);
                    float canopy = 34.0 - 7.0 * Noise(float2(p.x * 0.06, 2.0)) - 2.5 * lobes;
                    if (p.y > canopy) col = sil * 0.8 + _Accent.rgb * 0.008 * smoothstep(canopy + 6.0, canopy, p.y);
                    col += _Accent.rgb * 0.05 * exp(-abs(p.y - canopy) * 2.5);
                    float ff = Drift(p, float2(0.3 * sin(t * 0.5), 0.25), 3.5, 0.18, 0.18, t, 2.0);
                    col += _Accent2.rgb * 0.12 * ff * (0.5 + 0.5 * sin(t * 3.0 + p.x));
                }
                else if (scene == 11)
                {
                    // Disco: a mirror ball throwing rotating beams, sparkles everywhere.
                    float2 bc = sc + float2(0.0, 0.5 * R);
                    float2 d = p - bc;
                    float dist = length(d);
                    float ang = atan2(d.y, d.x);
                    for (int k = 0; k < 6; k++)
                    {
                        float fk = (float)k;
                        float beamA = fk * 1.0472 + t * 0.35 * (fmod(fk, 2.0) < 1.0 ? 1.0 : -1.0);
                        float da = abs(frac((ang - beamA) / 6.2832 + 0.5) - 0.5) * 6.2832;
                        float beam = exp(-da * 14.0) / (1.0 + dist * 0.04);
                        col += (fmod(fk, 2.0) < 1.0 ? _Accent.rgb : _Accent2.rgb) * 0.035 * beam * glow * step(R * 0.45, dist);
                    }
                    float br = R * 0.45;
                    if (dist < br)
                    {
                        float2 f = floor(d / (br * 0.18) + float2(t * 0.6, 0.0));
                        float sparkle = pow(Hash(f + floor(t * 6.0)), 8.0);
                        col = lerp(_SkyTop.rgb * 2.0, _SunTop.rgb * 0.2, 0.5 + 0.5 * d.y / br) + _SunTop.rgb * 0.5 * sparkle * breathe;
                    }
                    col += Stars(p + float2(t * 0.5, 0.0), 2.6, 0.12, t * 3.0) * 0.4 * _Accent2.rgb * 0.3;
                }
                else
                {
                    // Desert dunes: shimmering sun, two-tone dunes lit from the sun's side, stars above.
                    col += Stars(p, 2.2, 0.14, t) * 0.25 * smoothstep(horizon + 5.0, horizon + 25.0, p.y);
                    float shimmer = sin(p.y * 2.5 + t * 4.0) * 0.18 * smoothstep(horizon + 8.0, horizon, p.y);
                    col += Disc(p + float2(shimmer, 0.0), sc, R, _SunTop.rgb, _SunBottom.rgb, 0.24 * sunScale, breathe);
                    for (int k = 0; k < 3; k++)
                    {
                        float fk = (float)k;
                        float fq = 0.07 + 0.03 * fk;
                        float amp = 3.0 + 1.5 * fk;
                        float base = horizon - 1.0 - 5.0 * fk;
                        float h = base + amp * (0.6 * sin(p.x * fq + fk * 2.1) + 0.3 * sin(p.x * fq * 2.3 + fk) + 0.5 * Noise(float2(p.x * 0.05, fk)));
                        float h2 = base + amp * (0.6 * sin((p.x + 0.5) * fq + fk * 2.1) + 0.3 * sin((p.x + 0.5) * fq * 2.3 + fk) + 0.5 * Noise(float2((p.x + 0.5) * 0.05, fk)));
                        if (p.y < h)
                        {
                            float slope = (h2 - h) * sign(sc.x + 0.001); // faces turned toward the sun are lit
                            float lit = smoothstep(-0.05, 0.08, slope);
                            col = lerp(sil * (1.2 - 0.25 * fk), _SunBottom.rgb * 0.05 + _SkyBottom.rgb * 0.8, lit * (1.0 - 0.3 * fk));
                        }
                    }
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
