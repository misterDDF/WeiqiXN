// 盘面标记：形势方块、最后一手圆点、AI 推荐圆片。
// 形状按 UV 解析绘制并用 fwidth 做像素级抗锯齿，不依赖网格细分和 MSAA；描边透明度随填充透明度一起变化。
Shader "XNShader/BoardOverlay"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 0.4)
        _Color ("Legacy Color", Color) = (1, 1, 1, 1)
        [Enum(Square, 0, Disc, 1)] _Shape ("Shape", Float) = 0
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 0)
        _OutlineWidth ("Outline Width (fraction of half size)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color;
                half4 _OutlineColor;
                float _Shape;
                float _OutlineWidth;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = abs(input.uv * 2.0 - 1.0);
                bool disc = _Shape > 0.5;
                // 0 在中心，1 在形状边缘。
                float d = disc ? length(p) : max(p.x, p.y);
                float aa = max(fwidth(d), 1e-5);

                half fillAlpha = saturate(_BaseColor.a * _Color.a);
                half coverage = disc ? saturate((1.0 - d) / aa) : 1.0;
                half outline = _OutlineWidth > 0.0 ? saturate((d - (1.0 - _OutlineWidth)) / aa + 0.5) : 0.0;

                half3 rgb = lerp(_BaseColor.rgb, _OutlineColor.rgb, outline);
                half alpha = lerp(fillAlpha, fillAlpha * _OutlineColor.a, outline) * coverage;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
