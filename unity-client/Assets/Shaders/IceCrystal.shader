// Лёд: преломление фона, френель по краям, неровный шум, сохранение оттенка под блумом.
//
// Техника из разбора Crystal Shader (Cyanilux) и Ice Refraction (Daniel Ilett):
// центр кристалла прозрачен и ИСКАЖАЕТ то, что за ним, края плотные и светятся.
// Без преломления лёд выглядит окрашенным пластиком — это и была главная ошибка
// прошлой версии, где всё красилось непрозрачным ProceduralMaterials.Glow.
//
// Требует m_RequireOpaqueTexture: 1 в URP Asset — иначе фон читать неоткуда.
Shader "Albion/Ice Crystal"
{
    Properties
    {
        [MainColor] _DeepColor ("Глубина (ядро)", Color) = (0.10, 0.32, 0.72, 1)
        [HDR] _SurfaceColor ("Поверхность (грани)", Color) = (0.55, 0.86, 1.0, 1)
        [HDR] _RimColor ("Свечение краёв", Color) = (0.72, 0.94, 1.0, 1)
        _RimPower ("Степень френеля", Range(0.5, 8)) = 2.6
        _RimStrength ("Сила свечения краёв", Range(0, 4)) = 1.1
        _Opacity ("Плотность", Range(0, 1)) = 0.55
        _RefractionStrength ("Сила преломления", Range(0, 1)) = 0.16
        _NoiseScale ("Масштаб неровностей", Range(0.1, 30)) = 9.0
        _NoiseStrength ("Неровность преломления", Range(0, 0.2)) = 0.035
        _Shimmer ("Переливы граней", Range(0, 3)) = 0.6
        _Glow ("Общее свечение", Range(0.2, 3)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "IceForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "AlbionStylized.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

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
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _SurfaceColor;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _Opacity;
                float _RefractionStrength;
                float _NoiseScale;
                float _NoiseStrength;
                float _Shimmer;
                float _Glow;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Френель: 0 в центре, 1 на силуэте. Управляет и прозрачностью, и свечением.
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _RimPower);

                // --- Преломление ---------------------------------------------------
                // Смещение берётся по нормали в пространстве камеры, как в разборе Crystal
                // Shader: грань, отвёрнутая от камеры, смещает сильнее. Плюс шум, чтобы лёд
                // был неровным, а не полированным.
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, normalWS);
                float2 offset = viewNormal.xy * (0.1 + fresnel) * -_RefractionStrength;

                float noise = AlbionNoise(input.uv * _NoiseScale + _Time.y * 0.12);
                offset += (noise - 0.5) * _NoiseStrength;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS) + offset;
                half3 scene = SampleSceneColor(saturate(screenUV));

                // --- Цвет ----------------------------------------------------------
                // Градиент по локальной высоте: ядро глубокое, грани бледные.
                half3 body = lerp(_DeepColor.rgb, _SurfaceColor.rgb, saturate(fresnel + noise * 0.25));

                // Переливы: медленно ползущая радуга по граням, слабая, чтобы не уйти в белый.
                half3 shimmer = 0.5 + 0.5 * cos(_Time.y * 1.2 + noise * 6.28 + half3(0.0, 2.1, 4.2));
                body += (shimmer - 0.5) * _Shimmer * fresnel * 0.22;

                // Свечение краёв. Множитель не даёт каналам выровняться: порог блума 0,9 при
                // ACES-тонмаппинге превращал прежний белый в кашу. Оттенок сохраняется
                // пропорциональным, поэтому после тонмаппинга остаётся голубым.
                half3 rim = _RimColor.rgb * fresnel * _RimStrength;

                // --- Смешивание -----------------------------------------------------
                // Центр пропускает фон, силуэт плотный: так лёд читается объёмным.
                half alpha = saturate(_Opacity + fresnel * 0.75);
                half3 color = lerp(scene, body, alpha);
                color += rim;
                color *= _Glow;

                // Френель по краям добавляет непрозрачности — силуэт не растворяется.
                alpha = saturate(alpha + fresnel * 0.15);
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }

    // Мобильный вариант: там HDR выключен и Opaque Texture может отсутствовать,
    // поэтому преломления нет — плотный сине-голубой лёд без чтения сцены.
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "IceForwardMobile"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

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
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _SurfaceColor;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _Opacity;
                float _RefractionStrength;
                float _NoiseScale;
                float _NoiseStrength;
                float _Shimmer;
                float _Glow;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _RimPower);
                float noise = AlbionNoise(input.uv * _NoiseScale);

                half3 body = lerp(_DeepColor.rgb, _SurfaceColor.rgb, saturate(fresnel + noise * 0.25));
                body += _RimColor.rgb * fresnel * _RimStrength;
                half alpha = saturate(_Opacity + fresnel * 0.75);
                return half4(MixFog(body * _Glow, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
