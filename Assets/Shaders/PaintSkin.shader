Shader "Custom/PaintSkin"
{
    // 페인트맵(RenderTexture)에 색이 칠해진 부분만 보이는 셰이더.
    // 페인트 없음 = 투명(clip), 페인트 있음 = 해당 색상 표시.
    Properties
    {
        _BaseMap       ("스킨 텍스처",   2D)           = "white" {}
        _BaseColor     ("기본 색상",     Color)        = (1,1,1,1)
        _PaintMap      ("페인트 맵",     2D)           = "black" {}
        _PaintThreshold("표시 임계값",   Range(0,0.5)) = 0.01
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "TransparentCutout"
            "Queue"          = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PaintMap); SAMPLER(sampler_PaintMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _PaintThreshold;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 페인트맵 샘플링 — alpha가 임계값 미만이면 투명 처리
                half4 paint = SAMPLE_TEXTURE2D(_PaintMap, sampler_PaintMap, IN.uv);
                clip(paint.a - _PaintThreshold);

                // 조명 계산 (Half-Lambert)
                Light mainLight = GetMainLight();
                float NdotL    = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float lighting = NdotL * 0.6 + 0.4;

                // 스킨 텍스처로 미세한 디테일만 가져오고, 페인트 색이 주도
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half3 finalColor = paint.rgb * (base.rgb * 0.2 + 0.8);
                finalColor *= mainLight.color * lighting;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On  ZTest LEqual  ColorMask 0  Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings  { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_PaintMap); SAMPLER(sampler_PaintMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _PaintThreshold;
            CBUFFER_END

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return OUT;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                half4 paint = SAMPLE_TEXTURE2D(_PaintMap, sampler_PaintMap, IN.uv);
                clip(paint.a - _PaintThreshold);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
