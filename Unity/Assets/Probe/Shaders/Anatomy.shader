// Анатомическая голова: вершина half4 (xyz в ячейках чанка, w = материал*8 + грань) + snorm8x4 (нормаль сглаженного угла, затенение).
// Цвет из палитры _AnatPal, разброс по ячейке (хеш ячейки), свет по пикселю: ламберт, блик по влажности материала, затенение углов.
// Этап B: COLOR = (покраснение, синяк, ссадина, кровь), TEXCOORD0 = (время удара, время синяка, время крови, отёк) — среднее по ячейкам угла.
// Время в единицах 2 с боя. Отёк растёт за секунды в вершинном шейдере вдоль нормали; синяк меняет цвет по «дням» (_BruiseDay с на день);
// кровь густеет и темнеет за минуты. Цвета повреждений — _AnatDmg (линейные), их задаёт AnatPalette.
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _AnatPal[64];
            float4 _AnatPar[64];
            float4 _AnatDmg[12];
            float _AnatTime, _BruiseDay;
            float4 _ChunkOrg;   // начало чанка в ячейках тела; w — номер тела
            float4 _BodyPar;    // x — шаг решётки тела, см; yzw — центр тела в ячейках (у глаза — ось взгляда)
            // этап A3, изгиб шеи (AnatSkin): мир ↔ см головы, поворот сустава (вектор: ось × угол), (z начала наклона границы, высота, z оси, полуширина),
            // наклон границ в области головы (вперёд, назад, низ и верх перехода по y)
            float4x4 _AnatW2H, _AnatH2W;
            float4 _SkinJ[8], _SkinP[8], _SkinS;
            float _SkinOn;
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
            };

            float3 Rod(float3 v, float3 k, float a) { float c = cos(a), s = sin(a); return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c); }
            // изгиб точки покоя h (см головы) и нормали: повороты суставов сверху вниз, вес — по точке покоя
            void Skin(inout float3 h, inout float3 n)
            {
                float3 r = h;
                float ye = r.y + smoothstep(_SkinS.z, _SkinS.w, r.y) * (_SkinS.x * max(0.0, r.z - _SkinP[0].x) + _SkinS.y * max(0.0, _SkinP[0].x - r.z));
                if (_ChunkOrg.w > 1.5 && _ChunkOrg.w < 98.0) ye = 100.0;   // глаза и зубы — целиком с черепом; челюсть — по весам тканей вокруг (иначе кость выходит из угла челюсти)
                [unroll] for (int j = 0; j < 8; j++)
                {
                    float4 P = _SkinP[j];
                    float w = smoothstep(P.y - P.w, P.y + P.w, ye);
                    float3 rv = _SkinJ[j].xyz * w;
                    float a = length(rv);
                    if (a > 1e-6) { float3 k = rv / a; float3 piv = float3(0, P.y, P.z); h = piv + Rod(h - piv, k, a); n = Rod(n, k, a); }
                }
            }

            static const float3 N6[6] = { float3(1,0,0), float3(-1,0,0), float3(0,1,0), float3(0,-1,0), float3(0,0,1), float3(0,0,-1) };

            Vary vert(Attr i)
            {
                Vary o;
                float w = round(i.p.w);
                float mat = floor(w / 8.0);
                float dir = w - mat * 8.0;
                // отёк: цель в 0,05 мм, растёт с постоянной 4 с от последнего удара
                float sw = i.t.w * 255.0 * 0.005;
                float grow = 1.0 - exp(-max(0.0, _AnatTime - i.t.x * 510.0) / 4.0);
                float3 p = i.p.xyz + normalize(i.n.xyz) * (sw * grow / max(_BodyPar.x, 0.01));
                float3 ws = TransformObjectToWorld(p);
                float3 nw = TransformObjectToWorldNormal(normalize(i.n.xyz));
                if (_SkinOn > 0.5)
                {
                    float3 h = mul(_AnatW2H, float4(ws, 1)).xyz, hn = mul((float3x3)_AnatW2H, nw);
                    Skin(h, hn);
                    ws = mul(_AnatH2W, float4(h, 1)).xyz; nw = normalize(mul((float3x3)_AnatH2W, hn));
                }
                o.pos = TransformWorldToHClip(ws);
                o.os = i.p.xyz;
                o.ws = ws;
                o.c = i.c; o.t = i.t;
                o.nw = nw;
                o.md = float2(mat, dir);
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

            half4 frag(Vary i) : SV_Target
            {
                int mat = (int)(i.md.x + 0.5);
                int dir = (int)(i.md.y + 0.5);
                float4 pal = _AnatPal[mat];
                float h = VNoise(i.os + _ChunkOrg.xyz + _ChunkOrg.w * 131.0);
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
                // ссадина: снятый эпидермис, точечная кровь
                float ab = saturate(i.c.z * 1.5) * skin;
                col = lerp(col, _AnatDmg[6].rgb * (0.9 + 0.2 * h), ab * 0.85);
                float dots = Hash(floor(i.os * 4.0) + 7.0);
                if (dots < ab * 0.35) col = lerp(col, _AnatDmg[7].rgb, 0.8);
                wet = lerp(wet, 0.7, ab);
                // кровь: свежая яркая и мокрая, за ~2 мин густеет и темнеет
                float bage = saturate(max(0.0, _AnatTime - i.t.z * 510.0) / 120.0);
                float bcov = saturate(i.c.w * 1.7);
                float3 blc = lerp(_AnatDmg[8].rgb, _AnatDmg[9].rgb, pow(bage, 0.7));
                col = lerp(col, blc * (0.92 + 0.16 * h), bcov * lerp(0.75, 0.95, bcov));
                wet = lerp(wet, lerp(1.0, 0.35, bage), bcov);
                float3 n = normalize(i.nw);
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
