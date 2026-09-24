#ifndef XN_SHADOW_CASTER_INCLUDED
#define XN_SHADOW_CASTER_INCLUDED

// 自定义 shader 共用的 ShadowCaster / DepthOnly 顶点程序。
// 使用方需要先在 SubShader 级 HLSLINCLUDE 中声明 UnityPerMaterial，保证各 pass 的 CBUFFER 一致以兼容 SRP Batcher。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct XNShadowAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
};

struct XNShadowVaryings
{
    float4 positionCS : SV_POSITION;
};

XNShadowVaryings XNShadowVert(XNShadowAttributes input)
{
    XNShadowVaryings output;
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    output.positionCS = positionCS;
    return output;
}

XNShadowVaryings XNDepthOnlyVert(XNShadowAttributes input)
{
    XNShadowVaryings output;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}

half4 XNShadowFrag(XNShadowVaryings input) : SV_Target
{
    return 0;
}

#endif
