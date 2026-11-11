// Forward-only in mobile forward and desktop deferred. No surface/texture dependency.
// URP draws one matching pass per object: Outline requires a RenderObjects feature with
// ShaderTagId("AlbionOutline"), or a duplicate renderer using Albion/Toon Outline.
Shader "Albion/Toon Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (optional)", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.24, 0.57, 0.52, 1)
        _ShadeColor ("Shade Color", Color) = (0.075, 0.17, 0.23, 1)
        _HighlightColor ("Highlight Tint", Color) = (1.14, 1.10, 0.92, 1)
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _RimColor ("Rim Color", Color) = (0.72, 0.91, 0.90, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.16
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.42
        _ShadowSmoothness ("Shadow Smoothness", Range(0.001, 0.5)) = 0.10
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.45
        _PaintScale ("World Paint Scale", Range(0.01, 10)) = 1.8
        _PaintStrength ("Paint Variation", Range(0, 0.4)) = 0.055
        _OutlineColor ("Outline Color", Color) = (0.035, 0.065, 0.09, 1)
        _OutlineWidth ("Outline Width (world metres)", Range(0, 0.1)) = 0.012
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float4 _ShadeColor;
            float4 _HighlightColor;
            float4 _EmissionColor;
            float4 _RimColor;
            float4 _OutlineColor;
            float _RimPower;
            float _RimStrength;
            float _ShadowThreshold;
            float _ShadowSmoothness;
            float _AmbientStrength;
            float _PaintScale;
            float _PaintStrength;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "AlbionStylized.hlsl"
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = normalize(input.normalWS);
                float paint = AlbionNoise(input.positionWS.xz * _PaintScale + input.positionWS.y);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb
                    * _BaseColor.rgb * (1.0 + (paint - 0.5) * _PaintStrength);
                half3 color = AlbionToonLighting(input.positionWS, normalWS, albedo,
                    _ShadeColor.rgb, _HighlightColor.rgb, _ShadowThreshold,
                    _ShadowSmoothness, _AmbientStrength);
                Light sun = AlbionMainLight(input.positionWS);
                float rim = pow(saturate(1.0 - dot(GetWorldSpaceNormalizeViewDir(input.positionWS), normalWS)), _RimPower);
                rim *= _RimStrength * lerp(0.35, 1.0, saturate(dot(normalWS, sun.direction)));
                color += _RimColor.rgb * rim + _EmissionColor.rgb;
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="AlbionOutline" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AlbionOutlineVertex
            #pragma fragment AlbionOutlineFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "AlbionOutline.hlsl"
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
            #include "AlbionDepth.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
