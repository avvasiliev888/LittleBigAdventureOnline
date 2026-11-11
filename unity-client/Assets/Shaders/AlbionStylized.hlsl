#ifndef ALBION_STYLIZED_INCLUDED
#define ALBION_STYLIZED_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Smooth value noise: no lookup textures, UVs, downloads or per-frame allocations.
float AlbionHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}
float AlbionNoise(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(AlbionHash21(cell), AlbionHash21(cell + float2(1, 0)), u.x),
        lerp(AlbionHash21(cell + float2(0, 1)), AlbionHash21(cell + 1.0), u.x), u.y);
}
float AlbionPaint(float2 p)
{
    float broad = AlbionNoise(p * 0.53);
    float grain = AlbionNoise(p * 2.87 + broad * 0.7);
    float brush = sin(p.x * 3.3 + p.y * 2.5 + broad * 6.0);
    return saturate(broad * 0.72 + grain * 0.28 + brush * 0.045);
}
float AlbionTriplanarPaint(float3 positionWS, float3 normalWS, float scale)
{
    float3 weights = abs(normalWS);
    weights *= weights;
    weights *= weights;
    weights /= max(dot(weights, float3(1, 1, 1)), 0.0001);
    float3 p = positionWS * scale;
    return dot(float3(AlbionPaint(p.zy + 13.7), AlbionPaint(p.xz),
        AlbionPaint(p.xy + 37.2)), weights);
}
Light AlbionMainLight(float3 positionWS)
{
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
    #else
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    #endif
    return GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));
}
half3 AlbionToonLighting(float3 positionWS, float3 normalWS, half3 baseColor,
    half3 shadeColor, half3 highlightTint, float threshold, float softness, float ambientStrength)
{
    Light sun = AlbionMainLight(positionWS);
    float ndl = saturate(dot(normalWS, sun.direction));
    float width = max(softness, 0.001);
    float band = smoothstep(threshold - width, threshold + width, ndl);
    float highlight = smoothstep(0.78 - width, 0.78 + width, ndl);
    // Keep PCF penumbrae continuous rather than quantizing the shadow map into hard bands.
    float visibility = sun.shadowAttenuation * sun.distanceAttenuation;
    half3 direct = lerp(shadeColor, baseColor, band * visibility);
    direct = lerp(direct, baseColor * highlightTint, highlight * visibility);
    return direct * sun.color + max(SampleSH(normalWS), 0.0h) * baseColor * ambientStrength;
}
#endif
