// Assign to a separate renderer sharing the filled mesh, with one hull material
// per submesh. This avoids relying on URP's single-pass material tag selection.
Shader "Albion/Toon Outline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.035, 0.065, 0.09, 1)
        _OutlineWidth ("Outline Width (world metres)", Range(0, 0.1)) = 0.012
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AlbionOutlineVertex
            #pragma fragment AlbionOutlineFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END
            #include "AlbionOutline.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
