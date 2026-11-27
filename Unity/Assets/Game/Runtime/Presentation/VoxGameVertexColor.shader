// Игровой слой: цвет в вершинах (sRGB), свет по пикселю той же формулой, что у анатомической головы
// (VoxProbe/Anatomy: окружение _Ambient·(0.8+0.2·n.y) + ламберт с подсветкой тени), чтобы тело и ринг
// стояли в одном свете с головой. _Glow — самосвечение (лампы), туман уводит дальний зал в темноту.
// Вариант _VOX_JAW (борода соперника, HeadGear): вершина поворачивается с челюстью мимики на долю uv1.x вокруг оси суставов —
// как кожа в VoxProbe/Anatomy (AnatFace.Deform); _VoxJaw.x — угол, рад, .y — сдвиг вбок, см. Объект — оси головы в метрах.
Shader "VoxGame/VertexColor"
{
    Properties
    {
        _Ambient ("Ambient", Float) = 0.42
        _Glow ("Glow", Float) = 0
        _FogColor ("Fog Color", Color) = (0.02, 0.02, 0.035, 1)
        _FogStart ("Fog Start", Float) = 1000
        _FogEnd ("Fog End", Float) = 2000
        _VoxJaw ("Jaw (angle, side)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _VOX_JAW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Ambient;
            float _Glow;
            float4 _FogColor;
            float _FogStart;
            float _FogEnd;
            float4 _VoxJaw;
            CBUFFER_END

            // челюсть мимики: точка (м, оси головы) с долей поворота w
            float3 VoxJaw(float3 p, float w)
            {
                float3 pv = float3(0, -0.006, 0.0025);   // AnatFace.JawPivot, м
                float a = _VoxJaw.x * w, c = cos(a), s = sin(a);
                float3 v = p - pv;
                return pv + float3(v.x, v.y * c - v.z * s, v.y * s + v.z * c) + float3(_VoxJaw.y * 0.01 * w, 0, 0);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv1        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 nw          : TEXCOORD0;
                float3 ws          : TEXCOORD1;
                float4 color       : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 nOS = IN.normalOS;
                #ifdef _VOX_JAW
                IN.positionOS.xyz = VoxJaw(IN.positionOS.xyz, IN.uv1.x);
                float aj = _VoxJaw.x * IN.uv1.x, cj = cos(aj), sj = sin(aj);
                nOS = float3(nOS.x, nOS.y * cj - nOS.z * sj, nOS.y * sj + nOS.z * cj);
                #endif
                OUT.ws          = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.ws);
                OUT.nw          = TransformObjectToWorldNormal(nOS);
                OUT.color       = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 col = IN.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                col = SRGBToLinear(col);
                #endif
                float3 n = normalize(IN.nw);
                Light L = GetMainLight();
                float dif = saturate(dot(n, L.direction) * 0.8 + 0.2);
                float3 amb = _Ambient * (0.8 + 0.2 * n.y);
                float3 c = col * (amb + dif * 0.72 * L.color);
                c = lerp(c, col, _Glow);
                float fog = saturate((distance(IN.ws, _WorldSpaceCameraPos) - _FogStart) / max(_FogEnd - _FogStart, 0.001));
                c = lerp(c, _FogColor.rgb, fog * (1.0 - _Glow));
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_local _ _VOX_JAW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Ambient;
            float _Glow;
            float4 _FogColor;
            float _FogStart;
            float _FogEnd;
            float4 _VoxJaw;
            CBUFFER_END

            // челюсть мимики: точка (м, оси головы) с долей поворота w
            float3 VoxJaw(float3 p, float w)
            {
                float3 pv = float3(0, -0.006, 0.0025);   // AnatFace.JawPivot, м
                float a = _VoxJaw.x * w, c = cos(a), s = sin(a);
                float3 v = p - pv;
                return pv + float3(v.x, v.y * c - v.z * s, v.y * s + v.z * c) + float3(_VoxJaw.y * 0.01 * w, 0, 0);
            }

            struct Attrs { float4 positionOS : POSITION; float2 uv1 : TEXCOORD1; };
            struct Vary  { float4 positionHCS : SV_POSITION; };

            Vary DepthVert(Attrs IN)
            {
                Vary OUT;
                #ifdef _VOX_JAW
                IN.positionOS.xyz = VoxJaw(IN.positionOS.xyz, IN.uv1.x);
                #endif
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 DepthFrag(Vary IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
