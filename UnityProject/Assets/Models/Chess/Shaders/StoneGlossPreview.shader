Shader "WeiqiXN/StoneGlossPreview"
{
    // 落点预览棋子：与正式棋子共用光照（StoneGlossLighting.hlsl），整体半透明、不写深度、不接收也不投射阴影。
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.03, 0.03, 0.03, 1)
        _HighlightColor ("Reflection Tint", Color) = (1, 1, 1, 1)
        _PreviewAlpha ("Preview Alpha", Range(0, 1)) = 0.45
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
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _HighlightColor;
            half _PreviewAlpha;
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

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex StoneVert
            #pragma fragment Frag

            #include "StoneGlossLighting.hlsl"

            half4 Frag(StoneVaryings input) : SV_Target
            {
                return half4(StoneShade(input), _PreviewAlpha);
            }
            ENDHLSL
        }
    }
}
