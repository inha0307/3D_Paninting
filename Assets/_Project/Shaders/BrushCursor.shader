// 캐릭터 표면에 붙는 브러시 미리보기 원. 캐릭터 메쉬를 한 번 더 그리면서 커서 중심과의 3D 거리로 원을 그립니다.
// 붓: 어두운 바깥 선 + 현재 색 안쪽 선, 지우개: 흰색 점선, 스포이드: 작은 원 안을 집을 색으로 채움.
Shader "Hidden/Wonju/BrushCursor"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Cursor"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _CursorCenter;   // xyz: 월드 좌표
            float4 _CursorNormal;   // xyz: 월드 법선
            float4 _CursorColor;    // 선형 색 (붓 색 또는 스포이드 미리보기 색)
            float _CursorRadius;    // 월드 단위
            float _CursorMode;      // 0 붓, 1 지우개, 2 스포이드

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            // 반지름 r에 놓인 두께 w의 선. fwidth로 화면 1px 정도 부드럽게 처리합니다.
            float Ring(float d, float r, float w)
            {
                float aa = max(fwidth(d), 1e-5);
                return 1.0 - smoothstep(w * 0.5, w * 0.5 + aa, abs(d - r));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 offset = input.positionWS - _CursorCenter.xyz;
                float d = length(offset);
                float facing = smoothstep(-0.1, 0.25, dot(normalize(input.normalWS), _CursorNormal.xyz));
                float px = max(fwidth(d), 1e-5);   // 화면 1px에 해당하는 월드 거리
                float w = px * 2.0;
                float r = _CursorRadius;

                half4 color = 0;
                if (_CursorMode < 0.5)
                {
                    // 붓: 바깥쪽 어두운 선 + 안쪽 현재 색 선 → 어떤 바탕색에서도 보임
                    float outer = Ring(d, r, w);
                    float inner = Ring(d, r - w * 1.5, w * 1.5);
                    color = half4(0.1h, 0.1h, 0.12h, outer * 0.9h);
                    color = lerp(color, half4(_CursorColor.rgb, 1.0h), inner);
                }
                else if (_CursorMode < 1.5)
                {
                    // 지우개: 흰 점선 + 얇은 어두운 테두리
                    float3 n = normalize(_CursorNormal.xyz);
                    float3 u = normalize(abs(n.y) < 0.9 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0)));
                    float3 v = cross(n, u);
                    float angle = atan2(dot(offset, v), dot(offset, u));
                    float dash = step(0.5, frac(angle / (2.0 * PI) * 16.0));
                    float edge = Ring(d, r, w * 2.5);
                    float dashed = Ring(d, r, w * 1.2) * dash;
                    color = half4(0.1h, 0.1h, 0.12h, edge * 0.6h);
                    color = lerp(color, half4(1.0h, 1.0h, 1.0h, 1.0h), dashed);
                }
                else
                {
                    // 스포이드: 작은 원 안을 집을 색으로 채우고 테두리를 그림
                    float fill = 1.0 - smoothstep(r - px, r, d);
                    float edge = Ring(d, r, w);
                    color = half4(_CursorColor.rgb, fill);
                    color = lerp(color, half4(0.1h, 0.1h, 0.12h, 1.0h), edge);
                }
                color.a *= facing;
                clip(color.a - 0.001);
                return color;
            }
            ENDHLSL
        }
    }
}
