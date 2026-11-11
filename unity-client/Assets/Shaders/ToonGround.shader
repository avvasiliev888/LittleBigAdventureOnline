// World-space triplanar painterly terrain: UV-free and entirely texture-free.
Shader "Albion/Toon Ground"
{
    Properties
    {
        _ColorA ("Meadow Color", Color) = (0.20, 0.39, 0.35, 1)
        _ColorB ("Earth / Brush Color", Color) = (0.31, 0.46, 0.37, 1)
        _TileSize ("World Brush Size (metres)", Range(0.5, 20)) = 3.5
        _ShadeColor ("Shade Color", Color) = (0.075, 0.16, 0.20, 1)
        _RockColor ("Steep Slope Color", Color) = (0.30, 0.36, 0.37, 1)
        _SlopeStrength ("Rock On Slopes", Range(0, 1)) = 0.55
        _NoiseContrast ("Brush Contrast", Range(0.1, 4)) = 1.8
        _DetailStrength ("Fine Grain", Range(0, 0.3)) = 0.06
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.42
        _ShadowSmoothness ("Shadow Smoothness", Range(0.001, 0.5)) = 0.12
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _ColorA;
            float4 _ColorB;
            float4 _ShadeColor;
            float4 _RockColor;
            float _TileSize;
            float _SlopeStrength;
            float _NoiseContrast;
            float _DetailStrength;
            float _ShadowThreshold;
            float _ShadowSmoothness;
            float _AmbientStrength;
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
                float paint = AlbionTriplanarPaint(input.positionWS, n, rcp(max(_TileSize, 0.01)));
                float blend = smoothstep(0.15, 0.85, saturate((paint - 0.5) * _NoiseContrast + 0.5));
                half3 albedo = lerp(_ColorA.rgb, _ColorB.rgb, blend);
                float slope = smoothstep(0.15, 0.75, 1.0 - abs(n.y)) * _SlopeStrength;
                albedo = lerp(albedo, _RockColor.rgb * lerp(0.86, 1.12, paint), slope);
                float grain = AlbionNoise(input.positionWS.xz * 8.0 + input.positionWS.y);
                albedo *= 1.0 + (grain - 0.5) * _DetailStrength;
                half3 color = AlbionToonLighting(input.positionWS, n, albedo,
                    _ShadeColor.rgb, half3(1.06, 1.05, 0.96), _ShadowThreshold,
                    _ShadowSmoothness, _AmbientStrength);
                return half4(MixFog(color, input.fogFactor), 1);
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
