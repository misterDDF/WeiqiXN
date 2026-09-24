Shader "XNShader/Ground"
{
    // 榧木棋盘面：木纹按棋盘空间整盘铺一次；网格线与星位按棋盘坐标解析绘制并做像素级抗锯齿；
    // 受主光、主光阴影与环境光影响，盘边用法线倒角暗示厚度。
    // 棋盘范围、路数和星位由 RectGrid 写入 _XNBoard* 全局参数。
    Properties
    {
        _BaseMap ("Kaya Wood", 2D) = "white" {}
        _BaseColor ("Wood Tint", Color) = (1, 1, 1, 1)
        _InkColor ("Ink Color (A = Opacity)", Color) = (0.085, 0.072, 0.06, 0.94)
        _InkGrain ("Ink Wood Grain", Range(0, 1)) = 0.25
        _LineWidth ("Line Width (Cell)", Range(0.01, 0.2)) = 0.075
        _EdgeLineWidth ("Edge Line Width (Cell)", Range(0.01, 0.3)) = 0.13
        _StarRadius ("Star Point Radius (Cell)", Range(0.02, 0.3)) = 0.1
        _BevelWidth ("Edge Bevel Width", Range(0, 2)) = 0.5
        _BevelSlope ("Edge Bevel Slope", Range(0, 3)) = 1.1
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
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
            half4 _InkColor;
            half _InkGrain;
            float _LineWidth;
            float _EdgeLineWidth;
            float _StarRadius;
            float _BevelWidth;
            float _BevelSlope;
            half _AmbientStrength;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // x=路数，y=格子边长；未初始化时为 0，此时只画木纹。
            float4 _XNBoardGrid;
            // 星位：xyz=低/中/高位线序号，w=0 无星位、1 只有四角与天元、2 九个星位。
            float4 _XNBoardStar;
            // 棋盘外缘（棋盘本地坐标）：xy=最小 xz，zw=最大 xz。
            float4 _XNBoardRect;
            // 木纹 UV：uv = (本地 xz - xy) * zw。
            float4 _XNBoardUV;
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

            float NearestOfThree(float value, float3 candidates)
            {
                float nearest = candidates.x;
                nearest = abs(value - candidates.y) < abs(value - nearest) ? candidates.y : nearest;
                nearest = abs(value - candidates.z) < abs(value - nearest) ? candidates.z : nearest;
                return nearest;
            }

            // 返回 0..1 的墨色覆盖率；g 为以线序号为单位的坐标（第 k 条线位于 g = k）。
            half GridInkCoverage(float2 g, float lineCount)
            {
                float2 pixel = max(fwidth(g), 1e-5);
                float lastLine = lineCount - 1.0;

                float2 nearestLine = clamp(round(g), 0.0, lastLine);
                float2 distanceToLine = abs(g - nearestLine);
                float2 isEdgeLine = max(step(nearestLine, 0.5), step(lastLine - 0.5, nearestLine));
                float2 halfWidth = lerp(_LineWidth, _EdgeLineWidth, isEdgeLine) * 0.5;
                float2 lineCoverage = saturate((halfWidth - distanceToLine) / pixel + 0.5);

                float capHalfWidth = _EdgeLineWidth * 0.5;
                float2 insideSpan = saturate((g + capHalfWidth) / pixel + 0.5) * saturate((lastLine + capHalfWidth - g) / pixel + 0.5);
                half ink = max(lineCoverage.x * insideSpan.y, lineCoverage.y * insideSpan.x);

                if (_XNBoardStar.w > 0.5) {
                    float2 star = float2(NearestOfThree(g.x, _XNBoardStar.xyz), NearestOfThree(g.y, _XNBoardStar.xyz));
                    float2 isMid = step(abs(star - _XNBoardStar.y), 0.01);
                    float starValid = _XNBoardStar.w > 1.5 ? 1.0 : 1.0 - abs(isMid.x - isMid.y);
                    float starCoverage = saturate((_StarRadius - length(g - star)) / max(pixel.x, pixel.y) + 0.5);
                    ink = max(ink, starCoverage * starValid);
                }

                return ink;
            }

            float3 BevelNormalGrid(float2 boardPosition)
            {
                float2 distanceToMin = boardPosition - _XNBoardRect.xy;
                float2 distanceToMax = _XNBoardRect.zw - boardPosition;
                float2 edgeDistance = min(distanceToMin, distanceToMax);
                float2 outward = sign(distanceToMax - distanceToMin) * -1.0;
                float2 bevel = saturate(1.0 - edgeDistance / max(_BevelWidth, 1e-4));
                bevel *= bevel * _BevelSlope;
                return normalize(float3(outward.x * bevel.x, 1.0, outward.y * bevel.y));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 boardPosition = mul(_XNBoardWorldToGrid, float4(input.positionWS, 1.0)).xz;
                float2 woodUV = (boardPosition - _XNBoardUV.xy) * _XNBoardUV.zw;
                woodUV = woodUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half3 wood = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, woodUV).rgb * _BaseColor.rgb;

                half3 albedo = wood;
                float3 normalGrid = float3(0.0, 1.0, 0.0);
                if (_XNBoardGrid.x > 0.5) {
                    float2 g = boardPosition / _XNBoardGrid.y - 0.5;
                    half ink = GridInkCoverage(g, _XNBoardGrid.x) * _InkColor.a;
                    half3 inkColor = _InkColor.rgb * lerp(1.0h, wood * 1.6h, _InkGrain);
                    albedo = lerp(wood, inkColor, ink);
                    normalGrid = BevelNormalGrid(boardPosition);
                }

                // 棋盘本地到世界：只允许旋转与等比缩放，法线可直接用逆矩阵的转置变换。
                float3 normalWS = normalize(mul(normalGrid, (float3x3)_XNBoardWorldToGrid));
                if (_XNBoardGrid.x < 0.5) {
                    normalWS = float3(0.0, 1.0, 0.0);
                }

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
                half lambert = saturate(dot(normalWS, mainLight.direction));
                half3 direct = mainLight.color * (lambert * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(normalWS) * _AmbientStrength;
                return half4(albedo * (direct + ambient), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex XNShadowVert
            #pragma fragment XNShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Graphics/ShaderLibrary/XNShadowCaster.hlsl"
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
