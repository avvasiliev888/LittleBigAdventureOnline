Shader "Albion/Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap ("Leaf Map (optional)", 2D) = "white" {}
        [MainColor] _BaseColor ("Root / Canopy Color", Color) = (0.12, 0.34, 0.29, 1)
        _TipColor ("Sunlit Tips", Color) = (0.42, 0.62, 0.42, 1)
        _ShadeColor ("Cool Shadow", Color) = (0.055, 0.15, 0.18, 1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip (leaf cards)", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _WindDirection ("Wind Direction (world XZ)", Vector) = (1, 0, 0.4, 0)
        _WindStrength ("Wind Displacement (metres)", Range(0, 1)) = 0.16
        _WindSpeed ("Wind Speed", Range(0, 5)) = 1.1
        _WindScale ("World Gust Scale", Range(0.01, 3)) = 0.18
        _RootHeight ("Fixed Root Height (object Y)", Float) = 0
        _WindHeight ("Bend Height (object metres)", Float) = 2
        [Toggle] _VertexColorWind ("Use Vertex Alpha As Wind Weight", Float) = 0
        _Translucency ("Leaf Transmission", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "DisableBatching"="True" }
        HLSLINCLUDE
        #include "AlbionStylized.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float4 _TipColor;
            float4 _ShadeColor;
            float4 _WindDirection;
            float _Cutoff;
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;
            float _RootHeight;
            float _WindHeight;
            float _VertexColorWind;
            float _Translucency;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float height : TEXCOORD3;
            float fogFactor : TEXCOORD4;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        float3 FoliagePositionWS(Attributes input, out float height)
        {
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            height = saturate((input.positionOS.y - _RootHeight) / max(_WindHeight, 0.001));
            float weight = height * height * lerp(1.0, input.color.a, _VertexColorWind);
            float3 direction = normalize(float3(_WindDirection.x, 0, _WindDirection.z) + float3(0.0001, 0, 0));
            float phase = dot(p.xz, float2(0.71, 0.39)) * _WindScale + _Time.y * _WindSpeed;
            float gust = sin(phase) * 0.7 + sin(phase * 1.73 + 2.1) * 0.3;
            p += direction * gust * _WindStrength * weight;
            p.y -= abs(gust) * _WindStrength * weight * 0.15;
            return p;
        }
        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output = (Varyings)0;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = FoliagePositionWS(input, output.height);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.fogFactor = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 LeafSample(float2 uv)
        {
            half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            #if defined(_ALPHATEST_ON)
                clip(texel.a * _BaseColor.a - _Cutoff);
            #endif
            return texel;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 map = LeafSample(input.uv).rgb;
                float3 n = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float pattern = AlbionNoise(input.positionWS.xz * 1.8 + input.positionWS.y);
                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb,
                    saturate(input.height * 0.65 + pattern * 0.35)) * map;
                half3 color = AlbionToonLighting(input.positionWS, n, albedo, _ShadeColor.rgb,
                    half3(1.09, 1.09, 0.94), 0.35, 0.14, 0.48);
                Light sun = AlbionMainLight(input.positionWS);
                float backLight = saturate(dot(-n, sun.direction));
                color += albedo * sun.color * backLight * _Translucency * sun.shadowAttenuation;
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings output = Vert(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - output.positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,
                    normalize(output.normalWS), lightDirection));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }
            half DepthFrag(Varyings input) : SV_Target
            {
                LeafSample(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            half DepthFrag(Varyings input) : SV_Target
            {
                LeafSample(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
