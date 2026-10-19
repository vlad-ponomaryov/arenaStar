// Environment (Art-Spec §2.1, §6.3, D-110): the toon lighting of the fighters (ArenaToon.hlsl) without an outline — fighters
// pop out of the background. Albedo = _BaseColor × vertex colour (occlusion and wall colours are baked into the vertices by
// MapMeshBuilder) × _BaseMap sampled in WORLD XZ (one shared material for any map size): _WorldTiling cells per texture tile, plus
// a second, larger and turned sample of the same texture that only modulates the brightness (_MacroStrength) so the tile does
// not visibly repeat. Vertex alpha = gloss (environment: matte). Opaque, one forward pass + depth.
Shader "Arena/ToonEnv"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Base Map (world XZ)", 2D) = "white" {}
        _WorldTiling ("Cells Per Texture Tile", Float) = 7
        _MacroTiling ("Cells Per Macro Tile", Float) = 23
        _MacroStrength ("Macro Brightness Variation", Range(0, 1)) = 0.35
        _MapMeanLuminance ("Mean Luminance Of The Map", Range(0.05, 1)) = 1
        _MapContrast ("Map Contrast (0 = flat mean colour)", Range(0, 1)) = 1
        _ShadeColor ("Shade Multiplier", Color) = (0.612, 0.576, 0.784, 1)
        _RimColor ("Rim Color", Color) = (1, 0.965, 0.878, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12
        _RimPower ("Rim Power", Range(1, 8)) = 3
        _RimTopBias ("Rim Top Bias", Range(0, 1)) = 0.6
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.25
        _StepCenter ("Light Step Center", Range(-1, 1)) = 0.1
        _StepSoftness ("Light Step Softness", Range(0.001, 0.5)) = 0.12
        _SpecSize ("Highlight N.H Threshold", Range(0.5, 1)) = 0.99
        _SpecSoftness ("Highlight Softness", Range(0.001, 0.1)) = 0.01
        _SpecStrength ("Highlight Strength", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Include/ArenaToon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _BaseMap_ST;
            float _WorldTiling;
            float _MacroTiling;
            half _MacroStrength;
            half _MapMeanLuminance;
            half _MapContrast;
            half4 _ShadeColor;
            half4 _RimColor;
            half _RimStrength;
            half _RimPower;
            half _RimTopBias;
            half _AmbientStrength;
            half _StepCenter;
            half _StepSoftness;
            half _SpecSize;
            half _SpecSoftness;
            half _SpecStrength;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
        };
        ENDHLSL

        Pass
        {
            Name "ToonEnvForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 uv : TEXCOORD2; // xy: detail tile, zw: macro tile (turned ≈ 37°)
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                float2 xz = o.positionWS.xz;
                o.uv.xy = xz / max(_WorldTiling, 0.001) + _BaseMap_ST.zw;
                float2 turned = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
                o.uv.zw = turned / max(_MacroTiling, 0.001) + float2(0.31, 0.73);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.xy).rgb;
                // Calmer map (meadow): pull towards the mean brightness, keep the hue.
                half texLum = dot(tex, half3(0.3h, 0.59h, 0.11h));
                tex *= lerp(_MapMeanLuminance / max(texLum, 0.02h), 1.0h, _MapContrast);
                half3 macro = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.zw).rgb;
                // Brightness of the macro sample relative to the map average (a white map gives exactly 1: walls stay flat).
                half macroLum = dot(macro, half3(0.3h, 0.59h, 0.11h));
                half variation = lerp(1.0h, macroLum / max(_MapMeanLuminance, 0.05h), _MacroStrength);

                ArenaToonSurface s;
                s.albedo = _BaseColor.rgb * i.color.rgb * tex * variation;
                s.gloss = i.color.a;
                s.normalWS = i.normalWS;
                s.positionWS = i.positionWS;

                ArenaToonParams p;
                p.shadeColor = _ShadeColor.rgb;
                p.rimColor = _RimColor.rgb;
                p.rimStrength = _RimStrength;
                p.rimPower = _RimPower;
                p.rimTopBias = _RimTopBias;
                p.ambientStrength = _AmbientStrength;
                p.stepCenter = _StepCenter;
                p.stepSoftness = _StepSoftness;
                p.specSize = _SpecSize;
                p.specSoftness = _SpecSoftness;
                p.specStrength = _SpecStrength;
                return half4(ArenaToonShade(s, p), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes v) : SV_POSITION
            {
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
