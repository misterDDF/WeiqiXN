Shader "WeiqiXN/StoneGlossPreview"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.02, 0.02, 0.02, 1)
        _EdgeColor ("Edge Color", Color) = (0, 0, 0, 1)
        _HighlightColor ("Highlight Color", Color) = (1, 1, 1, 1)
        _PreviewAlpha ("Preview Alpha", Range(0, 1)) = 0.45
        _Smoothness ("Smoothness", Range(0, 1)) = 0.82
        _SpecStrength ("Specular Strength", Range(0, 2)) = 0.6
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12
        _PatternStrength ("Pattern Strength", Range(0, 0.25)) = 0.03
        _PatternScale ("Pattern Scale", Range(0.5, 16)) = 5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 viewDirWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EdgeColor;
                half4 _HighlightColor;
                half _PreviewAlpha;
                half _Smoothness;
                half _SpecStrength;
                half _RimStrength;
                half _PatternStrength;
                half _PatternScale;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                output.viewDirWS = normalize(GetWorldSpaceViewDir(positionInputs.positionWS));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(input.viewDirWS);
                Light mainLight = GetMainLight();
                half3 lightDirWS = normalize(mainLight.direction);
                half3 lightTint = lerp(half3(1.0h, 1.0h, 1.0h), mainLight.color, 0.35h);
                half diffuse = saturate(dot(normalWS, lightDirWS)) * 0.55h + 0.45h;

                half edge = pow(saturate(1.0h - abs(normalWS.y)), 1.65h);
                half top = pow(saturate(normalWS.y), 2.0h);

                half grain =
                    sin(input.positionWS.x * _PatternScale) +
                    sin(input.positionWS.z * _PatternScale * 1.37h) +
                    sin((input.positionWS.x + input.positionWS.z) * _PatternScale * 0.63h);
                grain *= 0.333h * _PatternStrength;

                half3 baseColor = _BaseColor.rgb * (diffuse + grain) * lightTint;
                baseColor = lerp(baseColor, _EdgeColor.rgb, edge * 0.52h);
                baseColor = lerp(baseColor, _HighlightColor.rgb, top * 0.07h);

                half3 halfDir = normalize(lightDirWS + viewDirWS);
                half specPower = lerp(18.0h, 150.0h, _Smoothness);
                half specular = pow(saturate(dot(normalWS, halfDir)), specPower) * _SpecStrength;
                half rim = pow(saturate(1.0h - dot(viewDirWS, normalWS)), 3.0h) * _RimStrength;
                half topSheenPower = lerp(10.0h, 28.0h, _Smoothness);
                half topSheen = pow(saturate(dot(normalWS, viewDirWS)), topSheenPower) * top * _SpecStrength * 0.22h;

                half3 color = baseColor + _HighlightColor.rgb * lightTint * specular + _HighlightColor.rgb * rim + _HighlightColor.rgb * topSheen;
                return half4(saturate(color), saturate(_BaseColor.a * _PreviewAlpha));
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
