// Fighter body (Art-Spec §6.3, D-110): toon lighting, pixel-constant outline (inverted hull), depth.
// Albedo = _BaseColor × vertex colour; vertex alpha = gloss. _Flash = 1 → white body (hit flash), the outline stays.
// Textured sources (A-Theo3D v3): _BaseMapOn = 1 (set per renderer by FighterSkin) takes colour and gloss from _BaseMap where
// uv.x >= 0 (untextured parts of the same mesh have uv = (-1, -1)); the outline keeps the vertex colours (= blurred texture).
Shader "Arena/ToonCharacter"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade Multiplier", Color) = (0.612, 0.576, 0.784, 1)
        _RimColor ("Rim Color", Color) = (1, 0.965, 0.878, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.35
        _RimPower ("Rim Power", Range(1, 8)) = 3
        _RimTopBias ("Rim Top Bias", Range(0, 1)) = 0.6
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.35
        _StepCenter ("Light Step Center", Range(-1, 1)) = 0.15
        _StepSoftness ("Light Step Softness", Range(0.001, 0.5)) = 0.06
        _SpecSize ("Highlight N.H Threshold", Range(0.5, 1)) = 0.975
        _SpecSoftness ("Highlight Softness", Range(0.001, 0.1)) = 0.015
        _SpecStrength ("Highlight Strength", Range(0, 1)) = 0.5
        _OutlineColor ("Outline Color", Color) = (0.169, 0.114, 0.071, 1)
        _OutlineDarken ("Outline Albedo Darken", Range(0, 1)) = 0.35
        _OutlineMix ("Outline Color Mix", Range(0, 1)) = 0.5
        _OutlinePx ("Outline Pixels At Ref Height", Range(0, 6)) = 2
        _OutlineRefHeight ("Outline Ref Screen Height", Float) = 1080
        _Flash ("Flash", Range(0, 1)) = 0
        _BaseMap ("Base Map (RGB colour, A gloss)", 2D) = "white" {}
        _BaseMapOn ("Base Map On", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Include/ArenaToon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
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
            half4 _OutlineColor;
            half _OutlineDarken;
            half _OutlineMix;
            float _OutlinePx;
            float _OutlineRefHeight;
            half _Flash;
            float4 _BaseMap_ST;
            half _BaseMapOn;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
        };

        ArenaToonParams CharacterParams()
        {
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
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ToonForward"
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
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(i.uv, _BaseMap));
                half on = _BaseMapOn * step(0.0, i.uv.x);
                ArenaToonSurface s;
                s.albedo = _BaseColor.rgb * lerp(i.color.rgb, t.rgb, on);
                s.gloss = lerp(i.color.a, t.a, on);
                s.normalWS = i.normalWS;
                s.positionWS = i.positionWS;
                half3 color = ArenaToonShade(s, CharacterParams());
                return half4(lerp(color, half3(1.0h, 1.0h, 1.0h), _Flash), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = ArenaToonOutlinePosition(v.positionOS.xyz, v.normalOS, _OutlinePx, _OutlineRefHeight);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 albedo = _BaseColor.rgb * i.color.rgb;
                return half4(ArenaToonOutlineColor(albedo, _OutlineDarken, _OutlineColor.rgb, _OutlineMix), 1.0h);
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
