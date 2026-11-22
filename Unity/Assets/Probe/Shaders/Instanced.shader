// Кубики крови (инстансная отрисовка) и пол. Оттенок у каждого кубика свой — от хеша его положения.
Shader "VoxProbe/Instanced"
{
    Properties { _Color ("Color", Color) = (0.4, 0.02, 0.03, 1) _Vary ("Vary", Float) = 0.4 _Ambient ("Ambient", Float) = 0.35 }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Vary;
            float _Ambient;
            CBUFFER_END

            struct Attr { float4 p : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; half3 col : COLOR0; };

            Vary vert(Attr i)
            {
                Vary o;
                UNITY_SETUP_INSTANCE_ID(i);
                o.pos = TransformObjectToHClip(i.p.xyz);
                float3 org = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float h = frac(sin(dot(org, float3(129.898, 782.33, 377.19))) * 43758.5453);
                Light L = GetMainLight();
                float ndl = saturate(dot(TransformObjectToWorldNormal(i.n), L.direction));
                o.col = (half3)(_Color.rgb * (1.0 - _Vary * h) * (_Ambient + ndl * L.color));
                return o;
            }
            half4 frag(Vary i) : SV_Target { return half4(i.col, 1); }
            ENDHLSL
        }
    }
}
