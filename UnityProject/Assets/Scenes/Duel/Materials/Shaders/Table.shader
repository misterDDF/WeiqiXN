Shader "XNShader/Table"
{
    // 棋盘下方的素麻桌布：按世界 xz 平铺，只做漫反射与环境光，不需要 UV 与高光。
    // 棋盘在桌面上的投影与贴地遮蔽按 _XNBoard* 全局参数解析计算（盘体视为从桌面到盘面的实心板），
    // 柔光窗主光下半影随离盘边的距离变宽；场景不使用实时阴影，这里也不采样阴影贴图。
    Properties
    {
        _BaseMap ("Linen", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
        _BoardContactStrength ("Board Contact Occlusion Strength", Range(0, 1)) = 0.35
        _BoardContactWidth ("Board Contact Occlusion Width", Float) = 2.5
        _BoardCastSoftness ("Board Shadow Softness", Float) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _AmbientStrength;
            half _BoardContactStrength;
            float _BoardContactWidth;
            float _BoardCastSoftness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // 与 Ground.shader 相同的棋盘全局参数：x=路数（0 表示未初始化）；盘面外缘为棋盘本地 xz 矩形，盘面在本地 y=0。
            float4 _XNBoardGrid;
            float4 _XNBoardRect;
            float4x4 _XNBoardWorldToGrid;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            // x=盘体投影（遮挡主光的比例），y=贴地遮蔽（遮挡环境光的比例）。
            half2 BoardOcclusion(float3 positionWS, float3 lightDirWS)
            {
                if (_XNBoardGrid.x < 0.5) {
                    return half2(0.0h, 0.0h);
                }

                float3 p = mul(_XNBoardWorldToGrid, float4(positionWS, 1.0)).xyz;
                float3 lightDir = normalize(mul((float3x3)_XNBoardWorldToGrid, lightDirWS));
                float2 rectCenter = (_XNBoardRect.xy + _XNBoardRect.zw) * 0.5;
                float2 rectHalfSize = (_XNBoardRect.zw - _XNBoardRect.xy) * 0.5;

                float2 q = abs(p.xz - rectCenter) - rectHalfSize;
                float outsideDistance = length(max(q, 0.0));
                half contact = exp(-outsideDistance / _BoardContactWidth) * _BoardContactStrength;

                // 桌面点沿主光方向上行到盘面高度的途中，只要有一处落进盘体足迹就被挡住；
                // 取几档高度的覆盖率最大值，越靠上的遮挡离桌面越远、半影越宽。
                float2 travel = lightDir.xz / max(lightDir.y, 0.2) * max(-p.y, 0.0);
                half cast = 0.0h;
                [unroll]
                for (int i = 1; i <= 6; i++) {
                    float t = i / 6.0;
                    float softness = _BoardCastSoftness * t + 0.05;
                    float2 d = abs(p.xz + travel * t - rectCenter) - rectHalfSize;
                    float2 inside = 1.0 - smoothstep(-softness, softness, d);
                    cast = max(cast, (half)(inside.x * inside.y));
                }

                return half2(cast, contact);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionWS.xz * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb;

                float3 normalWS = float3(0.0, 1.0, 0.0);
                Light mainLight = GetMainLight();
                half2 occlusion = BoardOcclusion(input.positionWS, mainLight.direction);
                half lambert = saturate(dot(normalWS, mainLight.direction));
                half3 direct = mainLight.color * (lambert * (1.0h - occlusion.x));
                half3 ambient = SampleSH(normalWS) * (_AmbientStrength * (1.0h - occlusion.y));
                return half4(albedo * (direct + ambient), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex XNDepthOnlyVert
            #pragma fragment XNShadowFrag
            #include "Assets/Graphics/ShaderLibrary/XNShadowCaster.hlsl"
            ENDHLSL
        }
    }
}
