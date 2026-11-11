// Stylized surface, analytic sky reflection and moving foam. No camera color/depth
// texture is required. _DEPTH_FOAM is an explicit optional shoreline enhancement.
Shader "Albion/Stylized Water"
{
    Properties
    {
        [MainColor] _BaseColor ("Shallow Teal", Color) = (0.13, 0.52, 0.57, 1)
        _DeepColor ("Deep Blue", Color) = (0.035, 0.16, 0.25, 1)
        _FoamColor ("Foam", Color) = (0.78, 0.94, 0.88, 1)
        _ReflectionColor ("Reflected Sky", Color) = (0.48, 0.76, 0.82, 1)
        _FlowDirection ("Flow Direction (XY maps world XZ)", Vector) = (0.8, 0.35, 0, 0)
        _FlowSpeed ("Flow Speed", Range(0, 3)) = 0.22
        _WaveScale ("Wave Frequency", Range(0.02, 4)) = 0.7
        _WaveStrength ("Wave Height (metres)", Range(0, 0.4)) = 0.055
        _FoamScale ("Foam Scale", Range(0.02, 8)) = 0.65
        _FoamStrength ("Flowing Foam", Range(0, 1)) = 0.45
        _Opacity ("Opacity", Range(0, 1)) = 0.88
        _Smoothness ("Sun Sparkle", Range(0, 1)) = 0.75
        [Toggle(_DEPTH_FOAM)] _DepthFoam ("Depth Shoreline (requires camera depth)", Float) = 0
        _FoamDepth ("Shoreline Width (metres)", Range(0.02, 3)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "WaterForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _DEPTH_FOAM
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "AlbionStylized.hlsl"
            #if defined(_DEPTH_FOAM)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float4 _ReflectionColor;
                float4 _FlowDirection;
                float _FlowSpeed;
                float _WaveScale;
                float _WaveStrength;
                float _FoamScale;
                float _FoamStrength;
                float _Opacity;
                float _Smoothness;
                float _FoamDepth;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            float3 Waves(float2 p)
            {
                float t = _Time.y * _FlowSpeed;
                float a = dot(p, float2(0.85, 0.53)) * _WaveScale + t * 2.1;
                float b = dot(p, float2(-0.38, 0.92)) * _WaveScale * 1.63 - t * 1.7;
                float height = (sin(a) + sin(b) * 0.45) * _WaveStrength;
                float2 slope = (cos(a) * float2(0.85, 0.53)
                    + cos(b) * float2(-0.38, 0.92) * 0.7335) * _WaveScale * _WaveStrength;
                return float3(height, slope);
            }
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS.y += Waves(output.positionWS.xz).x;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 direction = normalize(_FlowDirection.xy + float2(0.0001, 0));
                float2 flow = input.positionWS.xz * _FoamScale - direction * _Time.y * _FlowSpeed;
                float noise = AlbionNoise(flow * 0.9);
                float ripple = sin(dot(flow, float2(1.4, 2.1)) + noise * 7.0);
                float lineWidth = max(fwidth(ripple), 0.025);
                float foam = smoothstep(0.88 - lineWidth, 0.88 + lineWidth, ripple)
                    * smoothstep(0.25, 0.72, noise) * _FoamStrength;
                float3 waves = Waves(input.positionWS.xz);
                float micro = sin(dot(flow, float2(-3.2, 2.4)) + noise * 4.0) * 0.025;
                float3 normalWS = normalize(float3(-waves.y + micro, 1.0, -waves.z + micro));
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1.0 - saturate(dot(normalWS, view)), 4.0);
                half3 water = lerp(_DeepColor.rgb, _BaseColor.rgb, 0.35 + noise * 0.65);
                #if defined(_DEPTH_FOAM)
                    float rawDepth = SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                    float eyeDepth;
                    if (unity_OrthoParams.w > 0.5)
                    {
                        #if UNITY_REVERSED_Z
                            rawDepth = 1.0 - rawDepth;
                        #endif
                        eyeDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                    }
                    else
                    {
                        eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                    }
                    float surfaceDepth = -TransformWorldToView(input.positionWS).z;
                    float thickness = max(0.0, eyeDepth - surfaceDepth);
                    float shore = 1.0 - saturate(thickness / max(_FoamDepth, 0.001));
                    foam = max(foam, shore * smoothstep(0.25, 0.6, noise + ripple * 0.15));
                    water = lerp(_BaseColor.rgb, water, saturate(thickness / max(_FoamDepth * 3.0, 0.001)));
                #endif
                Light sun = AlbionMainLight(input.positionWS);
                water *= lerp(half3(0.65, 0.73, 0.8), sun.color, 0.55);
                // Analytic horizon reflection remains useful without reflection probes or opaque texture.
                float3 reflection = reflect(-view, normalWS);
                half3 reflectedSky = _ReflectionColor.rgb * lerp(0.65, 1.15, saturate(reflection.y));
                water = lerp(water, reflectedSky, 0.15 + fresnel * 0.55);
                float3 halfDirection = normalize(sun.direction + view + float3(0.0001, 0, 0));
                float sparkle = pow(saturate(dot(normalWS, halfDirection)), lerp(24.0, 180.0, _Smoothness));
                water += sun.color * sparkle * 0.65 * sun.shadowAttenuation;
                water = lerp(water, _FoamColor.rgb, saturate(foam));
                half alpha = saturate(_Opacity * _BaseColor.a + foam * 0.15 + fresnel * 0.06);
                return half4(MixFog(water, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
