// Анатомическая голова: вершина half4 (xyz в ячейках чанка, w = материал*8 + грань) + snorm8x4 (нормаль сглаженного угла, затенение).
// Цвет из палитры _AnatPal, разброс по ячейке (хеш ячейки), свет по пикселю: ламберт, блик по влажности материала, затенение углов.
// Этап B: COLOR = (покраснение, синяк, ссадина, кровь), TEXCOORD0 = (время удара, время синяка, время крови, отёк) — среднее по ячейкам угла.
// Время в единицах 2 с боя. Отёк растёт за секунды в вершинном шейдере вдоль нормали; синяк меняет цвет по «дням» (_BruiseDay с на день);
// кровь густеет и темнеет за минуты. Цвета повреждений — _AnatDmg (линейные), их задаёт AnatPalette.
// Часть 8: мышцы на срезе — номер части из 3D-текстуры тела (_AnatPartTex, ячейка → номер), направление волокон по мышце (_AnatFib),
// фасции между мышцами. ANAT_LITE (по умолчанию на телефоне, AnatPalette.Lite) — без рельефа отёка, трещин, шума синяка и крови, борозд
// мозга с искажением и рельефом, фасций; отёк, кровь, синяк, оттенок и волокна мышц остаются.
Shader "VoxProbe/Anatomy"
{
    Properties { _Ambient ("Ambient", Float) = 0.42 }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ANAT_LITE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _AnatPal[64];
            float4 _AnatPar[64];
            float4 _AnatDmg[12];
            float _AnatTime, _BruiseDay;
            float _AnatB10Off;   // часть 10: 1 — без русел крови и слоёв мышц (снимки и замер «без»); по умолчанию 0
            // часть 5: переломы головы для трещин на кости — пары (центр в ячейках тела, радиус кольца в ячейках), (ось удара, фаза); число и номер тела головы
            float4 _AnatFrac[32];
            float _AnatFracN, _AnatFracBody;
            float4 _ChunkOrg;   // начало чанка в ячейках тела; w — номер тела
            float4 _BodyPar;    // x — шаг решётки тела, см; yzw — центр тела в ячейках (у глаза — ось взгляда)
            // этап A3, изгиб шеи (AnatSkin): мир ↔ см головы, поворот сустава (вектор: ось × угол), (0, высота, z оси, полуширина)
            float4x4 _AnatW2H, _AnatH2W;
            // _SkinS: (YBot, YRef, z первой точки таблицы, последний индекс), _SkinT — граница жёсткой головы T(z) с шагом 1 см
            float4 _SkinJ[8], _SkinP[8], _SkinS, _SkinT[7];
            float _SkinOn;
            // мимика (AnatFace): 24 канала, роль тела по номеру (0 мягкая голова, 1 челюсть и нижние зубы, 2/3 глаз +x/−x, 4 жёстко),
            // рот: (раскрытие губ, см; видимая высота верхних и нижних зубов, см); зубы: (x шейки, ±1 верхний/нижний, ±0,5 — выбит) × 2
            float4 _Face[6], _FaceMouth, _FaceTX[16];
            float _FaceRole[48];
            float _FaceOn;
            // часть 8: номер части ячейки (R8, точечная выборка); _BodyDim — размеры решётки тела в ячейках, w = 1 — текстура есть
            TEXTURE3D(_AnatPartTex); SAMPLER(sampler_AnatPartTex);
            float4 _BodyDim;
            // волокна мышцы (номер − 32): xyz — направление в см тела (x зеркалится по стороне), w = 1 — круговые вокруг _AnatFibC[0] (глаз), 2 — вокруг _AnatFibC[1] (рот), 3 — веером к точке xyz (AnatPalette.Fib)
            float4 _AnatFib[32], _AnatFibC[2];
            // часть 9: переломы §5 — вес отломков в 3D-текстурах тела (тексель = 2 ячейки, канал = отломок; A — 0–3, B — 4–7),
            // смещение каждого отломка в ячейках тела (_FragD, w — время), _FragPar: x = 1 — есть отломки, y — число текстур; сдвиг тела целиком (глаз, зубы)
            TEXTURE3D(_AnatFragA); SAMPLER(sampler_AnatFragA); TEXTURE3D(_AnatFragB); SAMPLER(sampler_AnatFragB);
            float4 _FragPar, _FragDim, _FragD[8], _BodyShift;
            CBUFFER_START(UnityPerMaterial)
            float _Ambient;
            CBUFFER_END

            struct Attr { float4 p : POSITION; float4 n : NORMAL; float4 c : COLOR; float4 t : TEXCOORD0; };
            struct Vary
            {
                float4 pos : SV_POSITION;
                float3 os : TEXCOORD0;
                float3 nw : TEXCOORD1;
                float3 ws : TEXCOORD2;
                nointerpolation float2 md : TEXCOORD3; // материал, грань
                float ao : TEXCOORD4;
                float4 c : TEXCOORD5;
                float4 t : TEXCOORD6;
                float sw : TEXCOORD7;   // отёк в вершине, см (для рельефа и цвета)
                float fw : TEXCOORD8;   // часть 9: вес отломка в вершине (трещина на кости по краю, синяк над отломком)
                float3 fx : TEXCOORD9;  // мимика: шов губ (0 верх … 1 низ), маска рта, x покоя (см головы)
            };

            float3 Rod(float3 v, float3 k, float a) { float c = cos(a), s = sin(a); return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c); }
            // изгиб точки покоя h (см головы) и нормали: повороты суставов сверху вниз, вес — по точке покоя
            void Skin(inout float3 h, inout float3 n)
            {
                float3 r = h;
                // то же, что AnatSkin.YEff: выше границы YTop(x, z) — жёсткая голова, ниже высота сжимается к якорю
                float zf = clamp(r.z - _SkinS.z, 0.0, _SkinS.w); int ti = min((int)zf, (int)_SkinS.w - 1);
                float t0 = _SkinT[ti >> 2][ti & 3], t1 = _SkinT[(ti + 1) >> 2][(ti + 1) & 3];
                float ms = (1.0 - smoothstep(2.0, 3.5, abs(r.x))) * smoothstep(-8.0, -6.5, r.z) * (1.0 - smoothstep(0.0, 1.0, r.z));
                float yt = lerp(lerp(t0, t1, zf - ti), _SkinS.y, ms);
                float ye = r.y >= yt ? r.y - yt + _SkinS.y : _SkinS.x + (r.y - _SkinS.x) * (_SkinS.y - _SkinS.x) / (yt - _SkinS.x);
                if (_ChunkOrg.w > 0.5 && _ChunkOrg.w < 98.0) ye = 100.0;   // нижняя челюсть, глаза и зубы — целиком с черепом
                [unroll] for (int j = 0; j < 8; j++)
                {
                    float4 P = _SkinP[j];
                    float w = smoothstep(P.y - P.w, P.y + P.w, ye);
                    float3 rv = _SkinJ[j].xyz * w;
                    float a = length(rv);
                    if (a > 1e-6) { float3 k = rv / a; float3 piv = float3(0, P.y, P.z); h = piv + Rod(h - piv, k, a); n = Rod(n, k, a); }
                }
            }

            // мимика (AnatFace.Deform — та же функция на C#): точка покоя h (см головы) и нормаль
            float FC(int i) { return _Face[i >> 2][i & 3]; }
            float3 RotX(float3 v, float a) { float c = cos(a), s = sin(a); return float3(v.x, v.y * c - v.z * s, v.y * s + v.z * c); }
            float3 RotY(float3 v, float a) { float c = cos(a), s = sin(a); return float3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c); }
            float SS(float e0, float e1, float x) { float t = saturate((x - e0) / (e1 - e0)); return t * t * (3.0 - 2.0 * t); }
            void Face(inout float3 h, inout float3 n, int role, out float3 fx)
            {
                fx = float3(0, 0, h.x);
                if (role == 4) return;
                float jawA = saturate(FC(12)) * 0.3;
                float3 JP = float3(0, -0.6, 0.25);
                if (role == 1) { h = JP + RotX(h - JP, jawA) + float3(FC(13) * 0.35, 0, 0); n = RotX(n, jawA); return; }
                if (role == 2 || role == 3)
                {
                    float sxe = role == 2 ? 1.0 : -1.0; float3 ce = float3(sxe * 3.2, 2.3, 6.2);
                    float yaw = (FC(9) + sxe * FC(11) * 0.6) * 0.32, pitch = -FC(10) * 0.22;
                    h = ce + RotX(RotY(h - ce, yaw), pitch); n = RotX(RotY(n, yaw), pitch); return;
                }
                if (h.z < 3.0) return;
                float ax = abs(h.x), sx = h.x >= 0 ? 1.0 : -1.0; int sd = h.x >= 0 ? 0 : 1;
                float3 q = h;
                float3 ce2 = float3(sx * 3.2, 2.3, 6.2), v = h - ce2; float er = length(v);
                if (er < 2.1 && v.z > -0.4)
                {
                    float u = (ax - 3.2) / 1.45, wal = pow(max(0.0, 1.0 - u * u), 0.7), mid = 0.085 * wal + 0.06 * u;
                    float shell = SS(2.05, 1.6, er) * SS(-0.3, 0.3, v.z);
                    float wU = shell * SS(-0.2, 0.15, v.y - mid), wL = shell * SS(0.2, -0.15, v.y - mid);
                    float cu = FC(5 + sd) - FC(10) * 0.25;
                    float aU = (cu > 0 ? cu * 0.62 : cu * 0.07) * wal, aL = -max(FC(7 + sd), 0.0) * 0.2 * wal;
                    float a = aU * wU + aL * wL;
                    q = ce2 + RotX(v, a) * (1.0 + 0.06 * max(cu, 0.0) * wU); n = RotX(n, a);
                }
                float3 d = 0;
                float t = saturate((ax - 0.75) / 4.75), yb = 3.45 + 0.42 * sin(3.14159265 * min(1.0, t * 1.2)) - 0.15 * t, dyb = h.y - yb;
                float wb = exp(-dyb * dyb / (dyb > 0 ? 2.6 : 0.55)) * (1.0 - SS(5.6, 6.8, ax)) * SS(4.5, 6.0, h.z);
                float up = lerp(FC(0 + sd), FC(2 + sd), SS(1.0, 4.2, ax));
                d.y += wb * up * (up > 0 ? 0.8 : 0.6);
                float wk = wb * (1.0 - SS(1.2, 3.5, ax));
                d.x -= sx * 0.25 * FC(4) * wk * SS(0.0, 0.6, ax); d.y -= 0.18 * FC(4) * wk;
                float wc = exp(-((ax - 2.9) * (ax - 2.9) + h.y * h.y) / 1.1) * SS(5.5, 7.0, h.z);
                float ck = FC(21 + sd); d.y += 0.32 * ck * wc; d.z += 0.1 * ck * wc;
                float wn = exp(-((ax - 0.9) * (ax - 0.9) * 2.0 + (h.y + 2.3) * (h.y + 2.3)) / 0.6) * SS(7.0, 8.5, h.z);
                d.y += 0.22 * FC(20) * wn;
                float dyl = h.y + 5.25, fm = SS(6.0, 7.6, h.z), lipX = 1.0 - SS(1.7, 3.0, ax), lipC = 1.0 - SS(1.2, 2.6, ax);
                float wul = SS(-0.13, 0.13, dyl) * (1.0 - SS(0.7, 1.9, dyl)) * fm * lipC;
                float wll = SS(0.13, -0.13, dyl) * (1.0 - SS(-0.7, -1.9, dyl)) * fm * lipC;
                d.y += 0.6 * max(FC(17), 0.0) * wul - 0.55 * max(FC(18), 0.0) * wll;
                float cr = FC(14 + sd), wcor = exp(-((ax - 2.2) * (ax - 2.2) * 1.2 + dyl * dyl * 1.6) / 0.9) * SS(5.0, 7.0, h.z);
                d.y += cr * (cr > 0 ? 0.65 : 0.55) * wcor; d.x += sx * 0.12 * max(cr, 0.0) * wcor; d.z -= 0.08 * max(cr, 0.0) * wcor;
                float wd = FC(16), ww = exp(-((ax - 2.0) * (ax - 2.0) * 0.8 + dyl * dyl * 1.2) / 1.4) * fm * SS(0.0, 1.2, ax);
                d.x += sx * 0.42 * wd * ww * (wd < 0 ? 0.7 : 1.0);
                float lb = lipX * fm * exp(-dyl * dyl / 0.8);
                d.z += 0.25 * max(-wd, 0.0) * lb - 0.15 * max(FC(19), 0.0) * lb;
                q += d;
                float sharp = 1.0 - SS(1.8, 3.4, ax), wdt = lerp(1.4, 0.13, sharp), yc = -5.25 - (1.0 - sharp) * 0.6;
                float wJ = SS(yc + wdt, yc - wdt, h.y) * SS(-1.5, 2.5, h.z) * SS(-11.0, -8.8, h.y);
                float aj = jawA * wJ;
                q = JP + RotX(q - JP, aj) + float3(FC(13) * 0.35 * wJ, 0, 0); n = RotX(n, aj);
                fx = float3(SS(0.13, -0.13, dyl), lipX * fm, h.x);
                h = q;
            }

            static const float3 N6[6] = { float3(1,0,0), float3(-1,0,0), float3(0,1,0), float3(0,-1,0), float3(0,0,1), float3(0,0,-1) };

            Vary vert(Attr i)
            {
                Vary o;
                float w = round(i.p.w);
                float mat = floor(w / 8.0);
                float dir = w - mat * 8.0;
                // отёк: цель в 0,05 мм; часть 8: растёт в две ступени — 65 % за ~4 с, остальное за ~40 с от последнего удара
                float sw = i.t.w * 255.0 * 0.005;
                float dts = max(0.0, _AnatTime - i.t.x * 510.0);
                float grow = 0.65 * (1.0 - exp(-dts / 4.0)) + 0.35 * (1.0 - exp(-dts / 40.0));
                // часть 8: на экране — с насыщением до 0,9 см (модельный отёк одного удара 0,5–1,5 мм давал на экране 1–3 мм и не читался
                // с расстояния боя): лёгкий удар ≈ 5 мм, мощный ≈ 9 мм, повторные — до 14 мм (рельеф отёка виден только при сантиметре)
                float swv = 1.4 * (1.0 - exp(-sw / 0.09)) * grow;
                float3 pn = normalize(i.n.xyz), fd = 0; float fwv = 0;
                if (_FragPar.x > 0.5)
                {
                    // часть 9: отломок сдвигается вместе с мягкими тканями над ним; нормаль наклоняется по градиенту веса: n' = n − Σ ∇w_k (d_k·n)
                    float3 du = 1.0 / _FragDim.xyz, uv = (i.p.xyz + _ChunkOrg.xyz) * 0.5 * du;
                    float4 wa = SAMPLE_TEXTURE3D_LOD(_AnatFragA, sampler_AnatFragA, uv, 0), wb = 0;
                    if (_FragPar.y > 1.5) wb = SAMPLE_TEXTURE3D_LOD(_AnatFragB, sampler_AnatFragB, uv, 0);
                    fd = wa.x * _FragD[0].xyz + wa.y * _FragD[1].xyz + wa.z * _FragD[2].xyz + wa.w * _FragD[3].xyz
                       + wb.x * _FragD[4].xyz + wb.y * _FragD[5].xyz + wb.z * _FragD[6].xyz + wb.w * _FragD[7].xyz;
                    fwv = max(max(max(wa.x, wa.y), max(wa.z, wa.w)), max(max(wb.x, wb.y), max(wb.z, wb.w)));
                    #ifndef ANAT_LITE
                    float4 da = float4(dot(_FragD[0].xyz, pn), dot(_FragD[1].xyz, pn), dot(_FragD[2].xyz, pn), dot(_FragD[3].xyz, pn));
                    float4 db = float4(dot(_FragD[4].xyz, pn), dot(_FragD[5].xyz, pn), dot(_FragD[6].xyz, pn), dot(_FragD[7].xyz, pn));
                    float3 gr = 0;
                    [unroll] for (int e = 0; e < 3; e++)
                    {
                        float3 o = 0; o[e] = du[e];
                        float4 ea = SAMPLE_TEXTURE3D_LOD(_AnatFragA, sampler_AnatFragA, uv + o, 0), eb = 0;
                        if (_FragPar.y > 1.5) eb = SAMPLE_TEXTURE3D_LOD(_AnatFragB, sampler_AnatFragB, uv + o, 0);
                        gr[e] = (dot(ea - wa, da) + dot(eb - wb, db)) * 0.5;   // тексель = 2 ячейки
                    }
                    pn = normalize(pn - gr);
                    #endif
                }
                float3 p = i.p.xyz + normalize(i.n.xyz) * (swv / max(_BodyPar.x, 0.01)) + fd + _BodyShift.xyz;
                float3 ws = TransformObjectToWorld(p);
                float3 nw = TransformObjectToWorldNormal(pn);
                o.fx = 0;
                // оторванные куски (тело ≥ 100) лежат в мире сами по себе; мимика — до изгиба шеи, от позы покоя
                bool fon = _FaceOn > 0.5 && _ChunkOrg.w < 47.5;
                if ((_SkinOn > 0.5 || fon) && _ChunkOrg.w < 99.5)
                {
                    float3 h = mul(_AnatW2H, float4(ws, 1)).xyz, hn = mul((float3x3)_AnatW2H, nw);
                    if (fon) { float3 fx; Face(h, hn, (int)(_FaceRole[(int)(_ChunkOrg.w + 0.5)] + 0.5), fx); o.fx = fx; }
                    if (_SkinOn > 0.5) Skin(h, hn);
                    ws = mul(_AnatH2W, float4(h, 1)).xyz; nw = normalize(mul((float3x3)_AnatH2W, hn));
                }
                o.pos = TransformWorldToHClip(ws);
                o.os = i.p.xyz;
                o.ws = ws;
                o.c = i.c; o.t = i.t;
                o.nw = nw;
                o.md = float2(mat, dir);
                o.sw = swv;
                o.fw = fwv;
                o.ao = saturate(i.n.w * 127.0 / 126.0);
                return o;
            }

            float Hash(float3 c)
            {
                c = frac(c * 0.1031 + 0.37);
                c += dot(c, c.yzx + 33.33);
                return frac((c.x + c.y) * c.z);
            }
            // плавный шум по узлам решётки: разброс цвета без квадратов на сглаженной поверхности
            float VNoise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), b = lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), d = lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
            }

            // часть 8: номер части ячейки c (целые координаты в решётке тела)
            int PartAt(float3 c)
            {
                return (int)(SAMPLE_TEXTURE3D_LOD(_AnatPartTex, sampler_AnatPartTex, (c + 0.5) / _BodyDim.xyz, 0).r * 255.0 + 0.5);
            }

            // часть 8: фасция у края ячейки cs вдоль оси e (f — дробная часть по ней): соседняя ячейка — другая мышца
            float Fascia(float3 cs, float f, float3 e, int pt)
            {
                float wd = _AnatB10Off > 0.5 ? 0.38 : 0.5;   // часть 10: прослойка шире — с расстояния боя мышцы слоями
                if (f >= wd && f <= 1.0 - wd) return 0;
                int pn = PartAt(floor(cs) + e * (f < 0.5 ? -1.0 : 1.0));
                bool other = _AnatB10Off > 0.5 ? pn >= 32 && pn < 64 : pn != 0;   // часть 10: и граница мышцы с жиром, костью, сосудом (0 — воздух вне тела)
                return other && pn != pt ? 1.0 - smoothstep(wd * 0.26, wd, min(f, 1.0 - f)) : 0;
            }

            half4 frag(Vary i) : SV_Target
            {
                int mat = (int)(i.md.x + 0.5);
                int dir = (int)(i.md.y + 0.5);
                float4 pal = _AnatPal[mat];
                float3 pc = i.os + _ChunkOrg.xyz;   // точка в ячейках тела
                float h = VNoise(pc + _ChunkOrg.w * 131.0);
                float3 n = normalize(i.nw);
                // часть 5: рельеф отёка — наклон нормали по производным высоты отёка (без этого сдвиг вершин не меняет свет)
                #ifndef ANAT_LITE
                float3 dpx = ddx(i.ws), dpy = ddy(i.ws);
                float hsx = ddx(i.sw * 0.01), hsy = ddy(i.sw * 0.01);
                if (hsx != 0 || hsy != 0)
                {
                    float3 r1 = cross(dpy, n), r2 = cross(n, dpx); float det = dot(dpx, r1);
                    float3 sg = (det < 0 ? -1.0 : 1.0) * (hsx * r1 + hsy * r2);
                    if (abs(det) > 1e-12) n = normalize(abs(det) * n - sg * 0.8);
                }
                #endif
                float3 col = pal.rgb * (1.0 + pal.a * (h * 2.0 - 1.0) * 0.9);
                // радужка и зрачок: круги по расстоянию от оси взгляда (ячейка глаза 1 мм), тёмный ободок радужки, радиальные волокна
                if (mat == 27 || mat == 28)
                {
                    float2 d = i.os.xy + _ChunkOrg.xy - _BodyPar.yz;
                    float r = length(d);
                    float fib = Hash(float3(floor(atan2(d.y, d.x) * 9.0), 3.0, 5.0));
                    float3 iris = _AnatPal[27].rgb * (0.85 + 0.3 * fib) * lerp(1.0, 0.55, smoothstep(4.6, 6.0, r));
                    col = lerp(_AnatPal[28].rgb, iris, smoothstep(2.1, 2.5, r));
                }
                float wet = _AnatPar[mat].x;
                float skin = _AnatPar[mat].y;
                // покраснение: гаснет за ~25 с
                float red = i.c.x * exp(-max(0.0, _AnatTime - i.t.x * 510.0) / 25.0);
                col *= lerp(float3(1, 1, 1), _AnatDmg[0].rgb, saturate(red * 0.9) * skin);
                // синяк: красно-розовый → тёмно-фиолетовый → синий → зеленоватый → жёлтый (дни 0, 2, 4,5, 7,5, 11), к 16-му сходит
                float day = max(0.0, _AnatTime - i.t.y * 510.0) / _BruiseDay;
                float3 bc = day < 2.0 ? lerp(_AnatDmg[1].rgb, _AnatDmg[2].rgb, day / 2.0)
                          : day < 4.5 ? lerp(_AnatDmg[2].rgb, _AnatDmg[3].rgb, (day - 2.0) / 2.5)
                          : day < 7.5 ? lerp(_AnatDmg[3].rgb, _AnatDmg[4].rgb, (day - 4.5) / 3.0)
                          : lerp(_AnatDmg[4].rgb, _AnatDmg[5].rgb, saturate((day - 7.5) / 3.5));
                float ba = saturate(i.c.y * 1.6) * (1.0 - saturate((day - 11.0) / 5.0)) * skin;
                col *= lerp(float3(1, 1, 1), bc, ba);
                // часть 5: синяк читается издали — плотный темнее, края пятнами
                float bv = i.c.y * (1.0 - saturate((day - 11.0) / 5.0)) * skin;
                #ifdef ANAT_LITE
                float bvis = bv > 0.002 ? saturate(sqrt(bv) * 1.7) * lerp(0.6, 1.0, h) : 0.0;
                #else
                float bvis = bv > 0.002 ? saturate(sqrt(bv) * 1.7) * lerp(0.6, 1.0, VNoise(pc * 0.8 + 17.0)) : 0.0;
                #endif
                col *= lerp(float3(1, 1, 1), bc * lerp(0.85, 0.6, saturate(bv * 2.5)), saturate(bvis - ba));
                col *= 1.0 - 0.18 * bvis;
                // отёк: натянутая блестящая кожа, чуть краснее
                float swc = saturate(i.sw / 0.9) * skin;
                col *= lerp(float3(1, 1, 1), float3(1.0, 0.8, 0.8), swc * 0.6);
                wet = lerp(wet, 0.75, swc);
                // ссадина: снятый эпидермис, точечная кровь
                float ab = saturate(i.c.z * 1.5) * skin;
                col = lerp(col, _AnatDmg[6].rgb * (0.9 + 0.2 * h), ab * 0.85);
                float dots = Hash(floor(i.os * 4.0) + 7.0);
                if (dots < ab * 0.35) col = lerp(col, _AnatDmg[7].rgb, 0.8);
                wet = lerp(wet, 0.7, ab);
                // кровь: свежая яркая и мокрая, за ~2 мин густеет и темнеет
                float bage = saturate(max(0.0, _AnatTime - i.t.z * 510.0) / 120.0);
                float bcov = saturate(i.c.w * 1.7);
                // часть 5: край крови неровный, ширина плавает (шум по ячейкам); на кости кровь не держится; в мозге — в бороздах
                if (bcov > 0.002)
                {
                    #ifdef ANAT_LITE
                    float bn = h;
                    #else
                    float bn = VNoise(pc * 0.4 + 41.0) * 0.85 + h * 0.15;
                    // часть 8: потёки — шум, вытянутый вдоль вертикали тела: тонкая кровь у края пятна и внизу следа распадается на ручейки
                    float bst = VNoise(float3(pc.x * 0.9, pc.y * 0.08, pc.z * 0.9) + 23.0);
                    bn += (bst - 0.5) * 1.5 * (1.0 - 0.55 * saturate(bcov));
                    #endif
                    bcov = smoothstep(0.06, 0.6, bcov * 1.15 + (bn - 0.5) * 0.5);
                    if (_AnatPar[mat].w > 1.5 && _AnatPar[mat].w < 2.5) bcov *= 0.55;
                    // срез внутренних тканей: редкие пятна разрыва не заливают слои, лужа остаётся лужей
                    if (skin < 0.5 && mat != 41 && mat != 43) bcov *= smoothstep(0.35, 0.7, saturate(i.c.w * 1.7) + (bn - 0.5) * 0.6);
                    if (mat == 10) bcov *= _AnatB10Off > 0.5 ? 0.6 : 0.3;   // часть 8: на срезе мышцы кровь пятнами — мышцы различимы; часть 10 — тоньше
                    // часть 10: кровь по коже — русла. Лист крови (объединение многих подтёков) разбит на сеть ручьёв вдоль вертикали тела:
                    // изолинии двух шумов, вытянутых по вертикали и искажённых, ветвятся и сливаются; между руслами — тонкий мазок
                    if (_AnatB10Off < 0.5 && skin > 0.5)
                    {
                        float3 rq = float3(pc.x * 0.2, pc.y * 0.035, pc.z * 0.2);
                        rq.xz += (VNoise(pc * 0.06 + 5.0) - 0.5) * 1.6;
                        float r1 = 1.0 - smoothstep(0.07, 0.16, abs(VNoise(rq + 11.0) - 0.5));
                        #ifdef ANAT_LITE
                        float riv = r1;
                        #else
                        float r2 = 1.0 - smoothstep(0.05, 0.12, abs(VNoise(rq * float3(1.9, 1.6, 1.9) + 37.0) - 0.5));
                        float riv = max(r1, r2 * 0.85);
                        #endif
                        bcov *= lerp(0.15, 1.0, riv);
                    }
                }
                if (mat == 18 || mat == 19 || mat == 20)
                {
                    // извилины: изолиния шума с искажением — извилистые борозды; рельеф по разностям шума
                    float fq = mat == 20 ? 0.9 : 0.42;
                    #ifdef ANAT_LITE
                    float sul = 1.0 - smoothstep(0.035, 0.11, abs(VNoise(pc * fq) - 0.5));   // борозды без искажения и рельефа
                    #else
                    float3 q = pc * fq; float wq = VNoise(q * 0.6 + 3.0); q += float3(wq, 1.0 - wq, wq * wq) * 2.2;
                    float g = VNoise(q), e = 0.25;
                    float3 gg = float3(VNoise(q + float3(e, 0, 0)), VNoise(q + float3(0, e, 0)), VNoise(q + float3(0, 0, e))) - g;
                    float ad = abs(g - 0.5);
                    float sul = 1.0 - smoothstep(0.035, 0.11, ad);
                    float3 hg = -(g > 0.5 ? 1.0 : -1.0) * gg / e * (1.0 - smoothstep(0.0, 0.15, ad)) * 3.5;
                    n = normalize(n + hg - n * dot(hg, n));
                    #endif
                    float kw = mat == 19 ? 0.5 : 1.0;
                    if (mat == 19) col = lerp(col, _AnatPal[18].rgb * 1.15, 0.5);   // открытое белое вещество ближе к коре: меньше пестроты
                    col *= lerp(1.0, lerp(1.1, 0.35, sul), kw);
                    col = lerp(col, float3(0.22, 0.02, 0.03), sul * 0.55 * kw);
                    wet = lerp(wet, 0.9, sul);
                    bcov *= lerp(0.3, 1.0, sul);
                }
                float3 blc = lerp(_AnatDmg[8].rgb, _AnatDmg[9].rgb, pow(bage, 0.7));
                col = lerp(col, blc * (0.92 + 0.16 * h), bcov * lerp(0.75, 0.95, bcov));
                wet = lerp(wet, lerp(1.0, 0.35, bage), bcov);
                col *= 1.0 - 0.3 * saturate(bcov * (1.0 - bcov) * 4.0);   // тёмная кайма плёнки крови
                // мышца на срезе: пучки со светлыми прослойками; часть 8 — у каждой мышцы свой оттенок и направление волокон, фасции между мышцами
                if (mat == 10)
                {
                    float3 cs = pc - N6[dir] * 0.5;   // ячейка за гранью (сама мышца)
                    int pt = _BodyDim.w > 0.5 ? PartAt(floor(cs)) : 0;
                    if (pt >= 32 && pt < 64)
                    {
                        // волокна как AnatGeo.Fiber: в зеркале на сторону x ≥ 0, затем x со знаком
                        float4 F = _AnatFib[pt - 32]; float3 pcm = (pc - _BodyPar.yzw) * _BodyPar.x; float sx = pcm.x >= 0 ? 1.0 : -1.0;
                        float3 am = float3(abs(pcm.x), pcm.y, pcm.z), fd = F.xyz;
                        if (F.w > 2.5) fd = F.xyz - am;
                        else if (F.w > 0.5) { float3 r = am - _AnatFibC[F.w > 1.5 ? 1 : 0].xyz; fd = float3(-r.y, r.x * (F.w > 1.5 ? 0.6 : 1.0), 0); }
                        fd = normalize(fd + float3(0, 1e-4, 0)); fd.x *= sx;
                        float tn = Hash(float3(pt, 1.0, 2.0)), tg = Hash(float3(pt, 4.0, 5.0));
                        // часть 10: яркость мышцы — две ступени (светлая / тёмная), соседние мышцы чаще разные — срез читается слоями с расстояния боя
                        float tl = _AnatB10Off > 0.5 ? lerp(0.6, 1.3, tn) : (frac(tn * 7.0) > 0.5 ? lerp(1.2, 1.4, tn) : lerp(0.5, 0.68, tn));
                        col *= float3(lerp(0.6, 1.3, tn), lerp(0.55, 1.6, tg), lerp(0.6, 1.45, tg)) * tl * lerp(1.0, 0.85, bcov);
                        float3 q = pcm - fd * dot(pcm, fd);   // поперёк волокон: на продольном срезе — полосы, на поперечном — пучки пятнами
                        float fb = VNoise(q * 6.0 + dot(pcm, fd) * 0.35 + pt * 3.1);
                        col *= lerp(0.82, 1.1, fb);
                        float per = 1.0 - smoothstep(0.025, 0.06, abs(fb - 0.5));
                        col = lerp(col, float3(0.62, 0.42, 0.36), per * (_AnatB10Off > 0.5 ? 0.55 : 0.2) * (1.0 - bcov));   // часть 10: мелкие прослойки пучков слабее — не забивают границы мышц
                        // фасция: у края ячейки соседняя ячейка — другая мышца (часть 10: или не мышца — жир, кость, сосуд); в ANAT_LITE тоже
                        #ifdef ANAT_LITE
                        if (_AnatB10Off < 0.5)
                        #endif
                        {
                        float3 fc = frac(cs);
                        float fas = max(Fascia(cs, fc.x, float3(1, 0, 0), pt), max(Fascia(cs, fc.y, float3(0, 1, 0), pt), Fascia(cs, fc.z, float3(0, 0, 1), pt)));
                        col = lerp(col, _AnatB10Off > 0.5 ? float3(0.78, 0.72, 0.64) : float3(0.86, 0.8, 0.68), fas * (_AnatB10Off > 0.5 ? 0.85 : 0.95) * (1.0 - 0.5 * bcov));
                        wet = lerp(wet, 0.8, fas);
                        }
                    }
                    else
                    {
                        float fm = VNoise(pc * float3(0.9, 0.35, 0.9) + 7.0);
                        float per = 1.0 - smoothstep(0.02, 0.06, abs(fm - 0.5));
                        col = lerp(col, float3(0.62, 0.42, 0.36), per * 0.6 * (1.0 - bcov));
                    }
                }
                // мимика: раскрытый рот — растянутые грани шва губ красятся полостью рта, сверху и снизу зубы (выбитый — тёмная щель)
                if (_FaceOn > 0.5 && i.fx.y > 0.02 && _FaceMouth.x > 0.02 && i.fx.x > 0.03 && i.fx.x < 0.97)
                {
                    float sep = _FaceMouth.x * saturate(i.fx.y * 1.4);
                    float pm = smoothstep(0.02, 0.1, sep) * smoothstep(0.03, 0.12, i.fx.x) * smoothstep(0.97, 0.88, i.fx.x);
                    float g = saturate((i.fx.x - 0.06) / 0.88) * sep;
                    float3 mc = lerp(float3(0.10, 0.025, 0.025), _AnatPal[5].rgb * 0.45, smoothstep(0.55, 1.0, g / max(sep, 1e-3)) * 0.6);
                    float upT = _FaceMouth.y * i.fx.y, loT = _FaceMouth.z * i.fx.y;
                    bool upper = g < upT, lower = sep - g < loT;
                    if (upper || lower)
                    {
                        float want = upper ? 1.0 : -1.0, d1 = 9, d2 = 9, pr = 0;
                        [loop] for (int k = 0; k < 32; k++)
                        {
                            float4 T = _FaceTX[k >> 1]; float tx = (k & 1) ? T.z : T.x, tp = (k & 1) ? T.w : T.y;
                            if (tp * want < 0) continue;
                            float dd = abs(i.fx.z - tx);
                            if (dd < d1) { d2 = d1; d1 = dd; pr = step(0.75, abs(tp)); } else if (dd < d2) d2 = dd;
                        }
                        float edge = smoothstep(0.0, 0.06, d2 - d1);
                        float rim = upper ? smoothstep(0.0, 0.07, upT - g) : smoothstep(0.0, 0.07, loT - (sep - g));
                        mc = lerp(mc, _AnatPal[33].rgb * 0.92, pr * edge * rim);
                    }
                    col = lerp(col, mc, pm); wet = lerp(wet, 0.5, pm); skin *= 1.0 - pm;
                }
                // часть 9: перелом §5 — тёмная линия по краю отломка на открытой кости, синяк на коже над отломком (и в ANAT_LITE)
                if (i.fw > 0.01)
                {
                    if (mat == 12 || mat == 13 || mat == 14)
                    {
                        float fcr = 1.0 - smoothstep(0.1, 0.24, abs(i.fw - 0.5));
                        col = lerp(col, float3(0.07, 0.015, 0.01), fcr * 0.92); wet = lerp(wet, 0.1, fcr);
                    }
                    col *= lerp(float3(1, 1, 1), _AnatDmg[2].rgb, saturate(i.fw) * 0.45 * skin);
                }
                // трещины на кости вокруг переломов головы: лучи от кольца и обрывки второго кольца
                #ifndef ANAT_LITE
                if ((mat == 12 || mat == 13 || mat == 14) && _AnatFracN > 0.5 && abs(_ChunkOrg.w - _AnatFracBody) < 0.5)
                {
                    float cr = 0;
                    [loop] for (int k = 0; k < 16; k++)
                    {
                        if (k >= (int)_AnatFracN) break;
                        float4 A = _AnatFrac[k * 2], B = _AnatFrac[k * 2 + 1];
                        float3 d = pc - A.xyz; float al = dot(d, B.xyz); float3 rv = d - B.xyz * al; float rad = length(rv);
                        float R = A.w;
                        if (rad > R * 2.8 || abs(al) > R * 1.4) continue;
                        float3 u = normalize(cross(B.xyz, abs(B.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0))), w = cross(B.xyz, u);
                        float ang = atan2(dot(rv, w), dot(rv, u));
                        float s = (ang / 6.2832 + 0.5) * 12.0, si = floor(s);
                        float ray = si + 0.3 + 0.4 * Hash(float3(si, k, B.w)) + sin(rad * 0.9 + si * 2.3 + B.w) * 0.12 + sin(rad * 2.3 + si) * 0.05;
                        float dA = abs(s - ray) * 0.5236 * rad;
                        float len = R * (1.5 + 1.2 * Hash(float3(si, k, 3.0 + B.w)));
                        float ln = (1.0 - smoothstep(0.3, 0.6, dA)) * smoothstep(R * 0.5, R * 0.7, rad) * (1.0 - smoothstep(len * 0.75, len, rad));
                        float dr = abs(rad - R * (1.6 + 0.12 * sin(ang * 4.0 + B.w)));
                        float arc = (1.0 - smoothstep(0.28, 0.55, dr)) * step(0.45, Hash(float3(floor(ang * 2.5 + B.w), k, 7.0)));
                        cr = max(cr, max(ln, arc));
                    }
                    col = lerp(col, float3(0.07, 0.015, 0.01), cr * 0.92);
                    wet = lerp(wet, 0.1, cr);
                }
                #endif
                Light L = GetMainLight();
                float ndl = dot(n, L.direction);
                float dif = saturate(ndl * 0.8 + 0.2);
                float3 V = normalize(GetWorldSpaceViewDir(i.ws));
                float spec = wet * pow(saturate(dot(n, normalize(L.direction + V))), 18.0 + 50.0 * wet) * 0.45;
                float ao = 0.72 + 0.28 * i.ao;
                float3 amb = _Ambient * (0.8 + 0.2 * n.y);
                float3 c = col * (amb + dif * 0.72 * L.color) * ao + spec * L.color * ao;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
