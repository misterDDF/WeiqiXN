#ifndef XN_STONE_GLOSS_LIGHTING_INCLUDED
#define XN_STONE_GLOSS_LIGHTING_INCLUDED

// 正式棋子与落点预览共用的顶点程序和光照，保证两者观感一致。
// 使用方需要先在 SubShader 级 HLSLINCLUDE 中声明 UnityPerMaterial：
// _BaseColor、_HighlightColor、_Smoothness、_SoftboxStrength、_SoftboxShape、_ReflectionStrength、_Wrap、_DetailStrength、_AmbientStrength。
// 是否接收主光阴影由使用方的 _MAIN_LIGHT_SHADOWS 关键字决定。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_DetailMap);
SAMPLER(sampler_DetailMap);

struct StoneAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
};

struct StoneVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 detailUV : TEXCOORD2;
};

StoneVaryings StoneVert(StoneAttributes input)
{
    StoneVaryings output;
    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = positionInputs.positionCS;
    output.positionWS = positionInputs.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    // 网格物体空间 xz 范围为 [-0.5, 0.5]；棋子偏航整圈随机，细节图方向随之每颗不同，落子动画中也保持稳定。
    output.detailUV = input.positionOS.xz + 0.5;
    return output;
}

// 主光当作一盏面光源而不是点光源：光源中心在主光方向上，反射方向落进光源轮廓就映出一块亮斑。
// 以主光方向为轴取切平面坐标（正切值），轮廓为短边整圆角的圆角矩形（_SoftboxShape.xy 为半宽/半高）。
// 边缘模糊宽度按角度计（_SoftboxShape.z 加粗糙度项），不随轮廓尺寸缩放，小光源也有柔和边缘；
// 轮廓外再叠一层指数衰减的光晕（_SoftboxShape.w），模拟灯具周围被照亮的天花板。
half SoftboxReflection(half3 reflectDir, half3 lightDir, half perceptualRoughness)
{
    half3 axisUp = abs(lightDir.y) < 0.999h ? half3(0.0h, 1.0h, 0.0h) : half3(1.0h, 0.0h, 0.0h);
    half3 tangent = normalize(cross(axisUp, lightDir));
    half3 bitangent = cross(lightDir, tangent);
    half facing = dot(reflectDir, lightDir);
    half2 window = half2(dot(reflectDir, tangent), dot(reflectDir, bitangent)) / max(facing, 0.05h);

    half cornerRadius = min(_SoftboxShape.x, _SoftboxShape.y);
    half2 q = abs(window) - _SoftboxShape.xy + cornerRadius;
    half shapeDistance = length(max(q, 0.0h)) + min(max(q.x, q.y), 0.0h) - cornerRadius;
    half blur = _SoftboxShape.z + perceptualRoughness * 0.3h;
    half panel = 1.0h - smoothstep(-blur, blur, shapeDistance);
    half halo = exp(-max(shapeDistance, 0.0h) / (blur * 2.5h)) * _SoftboxShape.w;
    return (panel + halo) * step(0.0h, facing);
}

half3 StoneShade(StoneVaryings input)
{
    half3 normalWS = normalize(input.normalWS);
    half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
    half4 detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, input.detailUV);

    // 细节图 R=蛤碁石条纹、G=微颗粒、B=云状色差；条纹压暗固有色，颗粒同时调制固有色与反射。
    half stripe = detail.r * _DetailStrength.x;
    half grain = (detail.g - 0.5h) * 2.0h * _DetailStrength.y;
    half cloud = (detail.b - 0.5h) * 2.0h * _DetailStrength.z;
    half3 albedo = _BaseColor.rgb * saturate(1.0h - stripe) * (1.0h + grain + cloud);

    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
    half3 radiance = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation);
    half nDotL = dot(normalWS, mainLight.direction);

    // 包裹漫反射模拟蛤壳的轻微透光；分母保持能量大致守恒。
    half wrapped = saturate((nDotL + _Wrap) / ((1.0h + _Wrap) * (1.0h + _Wrap)));
    half3 color = albedo * (radiance * wrapped + SampleSH(normalWS) * _AmbientStrength);

    // 反射：介质 F0 取 0.04 的 Schlick Fresnel。环境用 SH 近似（上方为室内、下方为暖色棋盘），
    // 主光只以柔光窗形式出现在反射里，不再叠加点光源高光，避免棋子像打了硬光的小球。
    half oneMinusNdotV = 1.0h - saturate(dot(normalWS, viewDirWS));
    half fresnel = 0.04h + 0.96h * oneMinusNdotV * Pow4(oneMinusNdotV);
    half3 reflectDir = reflect(-viewDirWS, normalWS);
    half softbox = SoftboxReflection(reflectDir, mainLight.direction, 1.0h - _Smoothness);
    half3 reflection = SampleSH(reflectDir) * (_AmbientStrength * _ReflectionStrength)
        + radiance * (softbox * _SoftboxStrength * saturate(1.0h + grain));
    color += reflection * _HighlightColor.rgb * fresnel;
    return color;
}

#endif
