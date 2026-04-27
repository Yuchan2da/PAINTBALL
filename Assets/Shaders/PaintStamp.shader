Shader "Hidden/PaintStamp"
{
    // Graphics.Blit으로 페인트맵 RenderTexture 위에 스플랫 텍스처를 그리는 셰이더.
    // 벽/바닥 데칼과 동일한 PaintSplat 텍스처를 사용하여 자연스러운 페인트 모양.
    // 기존 페인트 위에 새 페인트를 누적(max alpha blend)한다.
    Properties
    {
        _MainTex      ("기존 페인트맵",     2D)            = "black" {}
        _SplatTex     ("스플랫 마스크",     2D)            = "white" {}
        _SplatCenter  ("스플랫 중심 UV",   Vector)        = (0.5, 0.5, 0, 0)
        _SplatRadius  ("스플랫 반지름",     Float)         = 0.05
        _SplatColor   ("스플랫 색상",       Color)         = (1, 0, 0, 1)
        _SplatHardness("가장자리 경도",     Range(0.1, 1)) = 0.6
        _SplatAngle   ("스플랫 회전각(rad)", Float)        = 0
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

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_SplatTex); SAMPLER(sampler_SplatTex);

            float4 _SplatCenter;
            float  _SplatRadius;
            float4 _SplatColor;
            float  _SplatHardness;
            float  _SplatAngle;

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

                // 스플랫 중심 기준 오프셋 계산
                float2 offset = IN.uv - _SplatCenter.xy;

                // 스플랫 범위 밖이면 기존 페인트 유지 (원형 바운딩으로 빠른 reject)
                float dist = length(offset);
                if (dist >= _SplatRadius)
                    return existing;

                // 오프셋을 랜덤 각도로 회전 → 매번 다른 방향의 스플래터
                float cosA = cos(_SplatAngle);
                float sinA = sin(_SplatAngle);
                float2 rotated = float2(
                    offset.x * cosA - offset.y * sinA,
                    offset.x * sinA + offset.y * cosA
                );

                // [-radius, +radius] → [0, 1] UV로 변환하여 스플랫 텍스처 샘플링
                float2 splatUV = rotated / (_SplatRadius * 2.0) + 0.5;

                // UV 범위 밖이면 기존 페인트 유지
                if (splatUV.x < 0 || splatUV.x > 1 || splatUV.y < 0 || splatUV.y > 1)
                    return existing;

                // 스플랫 텍스처의 알파를 마스크로 사용
                half4 splatSample = SAMPLE_TEXTURE2D(_SplatTex, sampler_SplatTex, splatUV);
                float mask = splatSample.a;

                // 가장자리 감쇠 (원형 외곽으로 갈수록 페이드아웃)
                float edgeFade = 1.0 - smoothstep(_SplatRadius * _SplatHardness, _SplatRadius, dist);
                mask *= edgeFade;

                // 새 페인트를 기존 위에 누적 (더 진한 쪽이 남음)
                half3 blended = lerp(existing.rgb, _SplatColor.rgb, mask);
                float alpha   = max(existing.a, mask);

                return half4(blended, alpha);
            }
            ENDHLSL
        }
    }
}
