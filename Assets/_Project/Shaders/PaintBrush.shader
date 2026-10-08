// 메쉬를 UV 공간에 펼쳐 캔버스 RenderTexture에 그립니다.
// 픽셀마다 보간된 오브젝트 공간 위치로 브러시와의 3D 거리를 재므로 UV 이음새를 넘어도 획이 끊기지 않습니다.
Shader "Hidden/Wonju/PaintBrush"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionOS : TEXCOORD0;
            float3 normalOS : TEXCOORD1;
        };

        // UV(0~1)를 렌더 타깃 전체에 대응하는 클립 좌표로 바꿉니다.
        float4 UVToClip(float2 uv)
        {
            float2 p = uv * 2.0 - 1.0;
            #if UNITY_UV_STARTS_AT_TOP
                p.y = -p.y;
            #endif
            return float4(p, 0.0, 1.0);
        }

        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionCS = UVToClip(input.uv);
            o.positionOS = input.positionOS.xyz;
            o.normalOS = input.normalOS;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "Brush"
            // 색만 섞고 알파 채널은 1로 유지합니다.
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 _BrushStart;   // xyz: 획 시작점 (오브젝트 공간)
            float4 _BrushEnd;     // xyz: 획 끝점
            float4 _BrushNormal;  // xyz: 칠하는 면의 법선
            float4 _BrushColor;
            float _BrushRadius;
            float _BrushHardness; // 0~1, 1이면 가장자리가 딱 떨어짐

            half4 Frag(Varyings input) : SV_Target
            {
                float3 a = _BrushStart.xyz;
                float3 ab = _BrushEnd.xyz - a;
                float t = saturate(dot(input.positionOS - a, ab) / max(dot(ab, ab), 1e-10));
                float d = distance(input.positionOS, a + ab * t);

                float alpha = 1.0 - smoothstep(_BrushRadius * _BrushHardness, _BrushRadius, d);
                // 브러시가 닿은 면과 반대쪽을 향하는 면(얇은 손, 몸 뒤쪽)에는 번지지 않게 합니다.
                float facing = dot(normalize(input.normalOS), _BrushNormal.xyz);
                alpha *= smoothstep(-0.1, 0.25, facing);

                clip(alpha - 0.001);
                return half4(_BrushColor.rgb, alpha * _BrushColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Coverage"
            // UV 섬이 차지하는 픽셀을 1로 표시합니다. Dilate 패스가 여백을 구분하는 데 씁니다.

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target { return 1.0h; }
            ENDHLSL
        }
    }
}
