// Кольца ударной волны: цвет и прозрачность в вершинах, сложение со сценой (как AdditiveBlending веба),
// без глубины — кольцо видно поверх перчатки, как в веб-версии (depthTest:false).
Shader "VoxGame/FxAdditive"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" "IgnoreProjector"="True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float4 color : COLOR; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 c = IN.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                c = SRGBToLinear(c);
                #endif
                return half4(c, IN.color.a);
            }
            ENDHLSL
        }
    }
}
