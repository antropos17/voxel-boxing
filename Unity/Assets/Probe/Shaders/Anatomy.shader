// Анатомическая голова: вершина half4 (xyz в ячейках чанка, w = материал*8 + грань) + snorm8x4 (нормаль сглаженного угла, затенение).
// Цвет из палитры _AnatPal, разброс по ячейке (хеш ячейки), свет по пикселю: ламберт, блик по влажности материала, затенение углов.
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
            float4 _ChunkOrg;   // начало чанка в ячейках тела; w — номер тела
            CBUFFER_START(UnityPerMaterial)
            float _Ambient;
            CBUFFER_END

            struct Attr { float4 p : POSITION; float4 n : NORMAL; };
            struct Vary
            {
                float4 pos : SV_POSITION;
                float3 os : TEXCOORD0;
                float3 nw : TEXCOORD1;
                float3 ws : TEXCOORD2;
                nointerpolation float2 md : TEXCOORD3; // материал, грань
                float ao : TEXCOORD4;
            };

            static const float3 N6[6] = { float3(1,0,0), float3(-1,0,0), float3(0,1,0), float3(0,-1,0), float3(0,0,1), float3(0,0,-1) };

            Vary vert(Attr i)
            {
                Vary o;
                float w = round(i.p.w);
                float mat = floor(w / 8.0);
                float dir = w - mat * 8.0;
                o.pos = TransformObjectToHClip(i.p.xyz);
                o.os = i.p.xyz;
                o.ws = TransformObjectToWorld(i.p.xyz);
                o.nw = TransformObjectToWorldNormal(normalize(i.n.xyz));
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

            half4 frag(Vary i) : SV_Target
            {
                int mat = (int)(i.md.x + 0.5);
                int dir = (int)(i.md.y + 0.5);
                float3 cell = floor(i.os - N6[dir] * 0.5) + _ChunkOrg.xyz + _ChunkOrg.w * 131.0;
                float4 pal = _AnatPal[mat];
                float h = Hash(cell);
                float3 col = pal.rgb * (1.0 + pal.a * (h * 2.0 - 1.0) * 0.9);
                float3 n = normalize(i.nw);
                Light L = GetMainLight();
                float ndl = dot(n, L.direction);
                float dif = saturate(ndl * 0.8 + 0.2);
                float3 V = normalize(GetWorldSpaceViewDir(i.ws));
                float wet = _AnatPar[mat].x;
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
