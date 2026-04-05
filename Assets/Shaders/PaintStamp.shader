Shader "Hidden/PaintStamp"
{
    // Graphics.Blit으로 페인트맵 RenderTexture 위에 원형 스플랫을 그리는 셰이더.
    // 기존 페인트 위에 새 페인트를 누적(max alpha blend)한다.
    Properties
    {
        _MainTex      ("기존 페인트맵", 2D)            = "black" {}
        _SplatCenter  ("스플랫 중심 UV", Vector)       = (0.5, 0.5, 0, 0)
        _SplatRadius  ("스플랫 반지름",  Float)         = 0.05
        _SplatColor   ("스플랫 색상",    Color)         = (1, 0, 0, 1)
        _SplatHardness("가장자리 경도",  Range(0.1, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off  ZTest Always  ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings  { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            float4 _SplatCenter;
            float  _SplatRadius;
            float4 _SplatColor;
            float  _SplatHardness;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 existing = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                float dist = distance(IN.uv, _SplatCenter.xy);

                // 스플랫 범위 밖이면 기존 페인트 유지
                if (dist >= _SplatRadius)
                    return existing;

                // 가장자리 부드러운 감쇠
                float edge = 1.0 - smoothstep(_SplatRadius * _SplatHardness, _SplatRadius, dist);

                // 새 페인트를 기존 위에 누적 (더 진한 쪽이 남음)
                half3 blended = lerp(existing.rgb, _SplatColor.rgb, edge);
                float alpha   = max(existing.a, edge);

                return half4(blended, alpha);
            }
            ENDHLSL
        }
    }
}
