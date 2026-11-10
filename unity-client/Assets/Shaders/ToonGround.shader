// M10: стилизованный пол арены — двухцветная шахматка в мире плюс toon-освещение.
// Шейдер самодостаточен (без текстур), поэтому арена выглядит «нарисованной» и без ассетов.
Shader "Albion/Toon Ground"
{
    Properties
    {
        _ColorA ("Tile A", Color) = (0.26, 0.38, 0.30, 1)
        _ColorB ("Tile B", Color) = (0.20, 0.30, 0.26, 1)
        _TileSize ("Tile Size", Range(0.5, 10)) = 2
        _ShadeColor ("Shade Color", Color) = (0.10, 0.16, 0.18, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _ShadowSmoothness ("Shadow Smoothness", Range(0.001, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _ColorA;
            float4 _ColorB;
            float _TileSize;
            float4 _ShadeColor;
            float _ShadowThreshold;
            float _ShadowSmoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                output.positionWS = positionInputs.positionWS;
                output.normalWS = GetVertexNormalInputs(input.normalOS).normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float lit = smoothstep(_ShadowThreshold - _ShadowSmoothness, _ShadowThreshold + _ShadowSmoothness, ndl)
                    * mainLight.shadowAttenuation;

                float2 cell = floor(input.positionWS.xz / _TileSize);
                float checker = fmod(abs(cell.x + cell.y), 2.0);
                half3 albedo = lerp(_ColorA.rgb, _ColorB.rgb, checker);

                half3 ambient = SampleSH(normalWS) * _ShadeColor.rgb;
                half3 diffuse = lerp(_ShadeColor.rgb, albedo, lit) * mainLight.color;
                return half4(MixFog(ambient * 0.4 + diffuse, input.fogFactor), 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AlbionShadowVertex
            #pragma fragment AlbionDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define ALBION_SHADOW_PASS
            #include "AlbionDepth.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AlbionDepthVertex
            #pragma fragment AlbionDepthFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AlbionDepth.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
