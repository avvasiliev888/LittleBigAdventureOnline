#ifndef ALBION_OUTLINE_INCLUDED
#define ALBION_OUTLINE_INCLUDED
struct AlbionOutlineAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct AlbionOutlineVaryings
{
    float4 positionCS : SV_POSITION;
    float fogFactor : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};
AlbionOutlineVaryings AlbionOutlineVertex(AlbionOutlineAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    AlbionOutlineVaryings output = (AlbionOutlineVaryings)0;
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    // Width is in world metres, independent of nonuniform object scale.
    positionWS += normalize(TransformObjectToWorldNormal(input.normalOS)) * _OutlineWidth;
    output.positionCS = TransformWorldToHClip(positionWS);
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    return output;
}
half4 AlbionOutlineFragment(AlbionOutlineVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(_OutlineWidth - 0.000001);
    return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1);
}
#endif
