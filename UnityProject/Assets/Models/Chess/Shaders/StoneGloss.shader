Shader "WeiqiXN/StoneGloss"
{
    // 正式棋子：接收并投射主光阴影；光照见 StoneGlossLighting.hlsl，与落点预览共用。
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.03, 0.03, 0.03, 1)
        _HighlightColor ("Reflection Tint", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.7
        _SoftboxStrength ("Softbox Reflection Strength", Range(0, 16)) = 12
        _SoftboxShape ("Softbox Shape (Half Width, Half Height, Softness, Halo)", Vector) = (0.16, 0.12, 0.06, 0.1)
        _ReflectionStrength ("Environment Reflection Strength", Range(0, 2)) = 1
        _Wrap ("Diffuse Wrap", Range(0, 1)) = 0
        [NoScaleOffset] _DetailMap ("Detail (R Stripes, G Grain, B Cloud)", 2D) = "gray" {}
        _DetailStrength ("Detail Strength (Stripes, Grain, Cloud)", Vector) = (0, 0, 0, 0)
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _HighlightColor;
            half _Smoothness;
            half _SoftboxStrength;
            half4 _SoftboxShape;
            half _ReflectionStrength;
            half _Wrap;
            half4 _DetailStrength;
            half _AmbientStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex StoneVert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "StoneGlossLighting.hlsl"

            half4 Frag(StoneVaryings input) : SV_Target
            {
                return half4(StoneShade(input), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

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
            Tags { "LightMode" = "DepthOnly" }

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
