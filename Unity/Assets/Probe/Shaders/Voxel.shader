// Воксели из компактной вершины half4: xyz в пределах чанка, w = материал*32 + грань*4 + затенение угла.
// Цвет из палитры (_VoxPalette, задаётся из C#), одна направленная лампа, свет в вершинах, без теней.
Shader "VoxProbe/Voxel"
{
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

            float4 _VoxPalette[16];
            CBUFFER_START(UnityPerMaterial)
            float _Ambient;
            CBUFFER_END

            struct Attr { float4 p : POSITION; };
            struct Vary { float4 pos : SV_POSITION; half3 col : COLOR0; };

            static const float3 N6[6] = { float3(1,0,0), float3(-1,0,0), float3(0,1,0), float3(0,-1,0), float3(0,0,1), float3(0,0,-1) };

            Vary vert(Attr i)
            {
                Vary o;
                float w = round(i.p.w);
                float mat = floor(w / 32.0);
                float r = w - mat * 32.0;
                float n = floor(r / 4.0);
                float ao = r - n * 4.0;
                o.pos = TransformObjectToHClip(i.p.xyz);
                float3 nw = TransformObjectToWorldNormal(N6[(int)n]);
                Light L = GetMainLight();
                float ndl = saturate(dot(nw, L.direction));
                float3 c = _VoxPalette[(int)mat].rgb;
                o.col = (half3)(c * (_Ambient + ndl * L.color) * (0.5 + 0.5 * ao / 3.0));
                return o;
            }
            half4 frag(Vary i) : SV_Target { return half4(i.col, 1); }
            ENDHLSL
        }
    }
}
