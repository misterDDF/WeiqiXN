Shader "WeiqiXN/StoneContactShadow"
{
    // 棋子接触阴影：贴在盘面上方的方形面片，以乘法混合压暗盘面，代替棋子的实时阴影。
    // 两部分叠加：紧贴子边的环境遮蔽，和沿主光方向偏移的柔和投影；投影方向每帧取主光方向，与场景灯光一致。
    // 面片中心被棋子挡住的部分由深度测试剔除；距离按世界单位计算，不受面片偏航影响。
    Properties
    {
        _ShadowColor ("Shadow Color (Full Occlusion)", Color) = (0.3, 0.22, 0.15, 1)
        _StoneRadius ("Stone Radius", Float) = 1.95
        _ContactStrength ("Contact Occlusion Strength", Range(0, 1)) = 0.5
        _ContactWidth ("Contact Occlusion Width", Float) = 0.9
        _CastStrength ("Cast Shadow Strength", Range(0, 1)) = 0.4
        _CastHeight ("Cast Shadow Height", Float) = 0.9
        _CastSoftness ("Cast Shadow Softness", Float) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                float _StoneRadius;
                half _ContactStrength;
                float _ContactWidth;
                half _CastStrength;
                float _CastHeight;
                float _CastSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 offsetWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 centerWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.offsetWS = positionWS.xz - centerWS.xz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.offsetWS;

                // 子边与盘面之间的缝隙挡住了大半天空光：贴边最暗，向外按平方衰减。
                float edgeDistance = length(p) - _StoneRadius;
                half contact = saturate(1.0 - edgeDistance / _ContactWidth);
                contact *= contact * _ContactStrength;

                // 投影：子的外轮廓约在半厚处，沿主光反方向偏移；柔光窗光源使边缘虚化。
                float3 lightDir = _MainLightPosition.xyz;
                float2 castOffset = -lightDir.xz / max(lightDir.y, 0.25) * _CastHeight;
                float castDistance = length(p - castOffset) - _StoneRadius;
                half cast = (1.0 - smoothstep(-_CastSoftness * 0.5, _CastSoftness * 0.5, castDistance)) * _CastStrength;

                half occlusion = 1.0h - (1.0h - contact) * (1.0h - cast);
                return half4(lerp(half3(1.0h, 1.0h, 1.0h), _ShadowColor.rgb, occlusion), 1.0h);
            }
            ENDHLSL
        }
    }
}
