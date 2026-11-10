#ifndef ALBION_DEPTH_INCLUDED
#define ALBION_DEPTH_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
struct AlbionDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct AlbionDepthVaryings { float4 positionCS : SV_POSITION; };
AlbionDepthVaryings AlbionDepthVertex(AlbionDepthAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    AlbionDepthVaryings output;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}
#if defined(ALBION_SHADOW_PASS)
float3 _LightDirection;
float3 _LightPosition;
AlbionDepthVaryings AlbionShadowVertex(AlbionDepthAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    AlbionDepthVaryings output;
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 direction = normalize(_LightPosition - positionWS);
    #else
        float3 direction = _LightDirection;
    #endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, direction));
    output.positionCS = ApplyShadowClamping(output.positionCS);
    return output;
}
#endif
half AlbionDepthFragment(AlbionDepthVaryings input) : SV_Target { return input.positionCS.z; }
#endif
