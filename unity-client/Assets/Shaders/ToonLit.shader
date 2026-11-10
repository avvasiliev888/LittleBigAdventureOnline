// Toon materials use forward-only lighting in both URP renderers.
// Shadow/depth passes are geometry passes, not the expanded outline hull.
Shader "Albion/Toon Lit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.35, 0.75, 0.45, 1)
        _ShadeColor ("Shade Color", Color) = (0.12, 0.22, 0.24, 1)
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _RimColor ("Rim Color", Color) = (1, 0.95, 0.75, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.2
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _ShadowSmoothness ("Shadow Smoothness", Range(0.001, 0.5)) = 0.08
        _OutlineColor ("Outline Color", Color) = (0.05, 0.06, 0.09, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _ShadeColor;
            float4 _EmissionColor;
            float4 _RimColor;
            float _RimPower;
            float _RimStrength;
            float _ShadowThreshold;
            float _ShadowSmoothness;
            float4 _OutlineColor;
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
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 n = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light light = GetMainLight(shadowCoord);
                float ramp = smoothstep(_ShadowThreshold - _ShadowSmoothness,
                    _ShadowThreshold + _ShadowSmoothness, saturate(dot(n, light.direction)));
                float lit = ramp * light.shadowAttenuation;
                half3 color = lerp(_ShadeColor.rgb, _BaseColor.rgb, lit) * light.color;
                color += SampleSH(n) * _ShadeColor.rgb * 0.4;
                float rim = pow(1.0 - saturate(dot(view, n)), _RimPower) * _RimStrength;
                color += _RimColor.rgb * rim * lerp(0.25, 1.0, lit);
                color += _EmissionColor.rgb;
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; float fogFactor : TEXCOORD0; };
            Varyings VertOutline(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += TransformObjectToWorldNormal(input.normalOS) * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 FragOutline(Varyings input) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1);
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
