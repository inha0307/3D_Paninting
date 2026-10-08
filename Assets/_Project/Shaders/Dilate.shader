// UV 섬 바깥 여백 픽셀을 가장 가까운 섬 픽셀의 색으로 채웁니다.
// 텍스처 필터링과 밉맵이 여백을 섞어도 이음새에 선이 보이지 않게 합니다.
Shader "Hidden/Wonju/Dilate"
{
    Properties
    {
        _MainTex ("Canvas", 2D) = "white" {}
        _CoverageTex ("Coverage", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_RADIUS 4

            TEXTURE2D(_MainTex);
            TEXTURE2D(_CoverageTex);
            float4 _MainTex_TexelSize;
            int _Radius;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // PaintBrush와 같은 UV → 클립 변환을 써서 Graphics.Blit의 행렬 설정에 의존하지 않습니다.
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float2 p = input.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    p.y = -p.y;
                #endif
                o.positionCS = float4(p, 0.0, 1.0);
                o.uv = input.uv;
                return o;
            }

            bool Covered(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_CoverageTex, sampler_PointClamp, uv, 0).r > 0.5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, input.uv, 0);
                if (Covered(input.uv))
                    return color;

                float2 texel = _MainTex_TexelSize.xy;
                float bestDist = 1e6;
                half4 best = color;
                [loop] for (int r = 1; r <= MAX_RADIUS; r++)
                {
                    if (r > _Radius || bestDist < 1e6) break;
                    [loop] for (int y = -r; y <= r; y++)
                    [loop] for (int x = -r; x <= r; x++)
                    {
                        if (max(abs(x), abs(y)) != r) continue; // 이번 반경의 테두리만 검사
                        float2 uv = input.uv + float2(x, y) * texel;
                        float dist = x * x + y * y;
                        if (dist < bestDist && Covered(uv))
                        {
                            bestDist = dist;
                            best = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, uv, 0);
                        }
                    }
                }
                return best;
            }
            ENDHLSL
        }
    }
}
