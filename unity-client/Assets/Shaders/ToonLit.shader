// M10: стилизованный toon-шейдер для URP (персонажи, камни, колонны).
// Основной проход — toon-освещение с двухцветной рампой, мягкой тенью, rim-подсветкой и ambient.
// Второй проход — контур (inverted hull), поэтому у шейдера два прохода.
// Проход помечен UniversalForwardOnly: он работает и в Forward, и в Deferred renderer
// (в deferred-профиле такие материалы рендерятся в forward-only проходе, см. ADR-012).
Shader "Albion/Toon Lit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.35, 0.75, 0.45, 1)
        _ShadeColor ("Shade Color", Color) = (0.12, 0.22, 0.24, 1)
        _RimColor ("Rim Color", Color) = (1, 0.95, 0.75, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.35
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _ShadowSmoothness ("Shadow Smoothness", Range(0.001, 0.5)) = 0.08
        _OutlineColor ("Outline Color", Color) = (0.05, 0.06, 0.09, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _ShadowThreshold;
                float _ShadowSmoothness;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float lit = smoothstep(_ShadowThreshold - _ShadowSmoothness, _ShadowThreshold + _ShadowSmoothness, ndl)
                    * mainLight.shadowAttenuation;

                // Двухцветная рампа: тень — отдельный цвет, свет — базовый цвет, плюс ambient.
                half3 ambient = SampleSH(normalWS) * _ShadeColor.rgb;
                half3 diffuse = lerp(_ShadeColor.rgb, _BaseColor.rgb, lit) * mainLight.color;
                half3 color = ambient * 0.65 + diffuse;

                float rim = pow(1.0 - saturate(dot(viewDirWS, normalWS)), _RimPower) * _RimStrength;
                color += _RimColor.rgb * rim;

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertOutline
            #pragma fragment FragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _ShadowThreshold;
                float _ShadowSmoothness;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings VertOutline(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS += normalWS * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 FragOutline(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
