// Assign as RenderSettings.skybox; directions are evaluated in world space.
Shader "Albion/Gradient Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.07, 0.22, 0.34, 1)
        _HorizonColor ("Horizon", Color) = (0.60, 0.78, 0.77, 1)
        _GroundColor ("Below Horizon", Color) = (0.16, 0.25, 0.29, 1)
        [HDR] _SunColor ("Sun Color", Color) = (1.0, 0.81, 0.46, 1)
        _SunDirection ("Direction Toward Sun (world XYZ)", Vector) = (-0.4, 0.65, -0.5, 0)
        _SunSize ("Sun Radius (degrees)", Range(0.1, 12)) = 2.5
        _SunStrength ("Sun Strength", Range(0, 8)) = 1.2
        _HorizonHeight ("Horizon Height", Range(-0.5, 0.5)) = 0
        _GradientPower ("Gradient Power", Range(0.1, 4)) = 0.65
        _HazeStrength ("Golden Sun Haze", Range(0, 1)) = 0.16
        _Exposure ("Exposure", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Pass
        {
            Name "Sky"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float4 _SunColor;
                float4 _SunDirection;
                float _SunSize;
                float _SunStrength;
                float _HorizonHeight;
                float _GradientPower;
                float _HazeStrength;
                float _Exposure;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.directionWS);
                float height = direction.y - _HorizonHeight;
                float above = pow(saturate(height), max(_GradientPower, 0.001));
                float below = smoothstep(0.0, 0.45, -height);
                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, above);
                color = lerp(color, _GroundColor.rgb, below);
                float3 sun = normalize(_SunDirection.xyz + float3(0.0001, 0.0001, 0));
                float alignment = dot(direction, sun);
                float radius = radians(_SunSize);
                float edge = max(fwidth(alignment), 0.00002);
                float disk = smoothstep(cos(radius) - edge, cos(radius) + edge, alignment);
                float haze = pow(saturate(alignment), 12.0) * _HazeStrength;
                color += _SunColor.rgb * (disk * _SunStrength + haze);
                return half4(color * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
