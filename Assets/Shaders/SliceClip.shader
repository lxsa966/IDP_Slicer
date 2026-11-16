Shader "LayerLab/SliceClip"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.23, 0.64, 0.96, 1)
        _CutoffY ("World Y Cutoff", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ClippedMesh"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _CutoffY;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(_CutoffY - input.positionWS.y);
                half3 n = normalize(input.normalWS);
                half lightAmount = 0.45h + 0.55h * abs(dot(n, normalize(half3(0.35h, 0.8h, -0.4h))));
                return half4(_BaseColor.rgb * lightAmount, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
