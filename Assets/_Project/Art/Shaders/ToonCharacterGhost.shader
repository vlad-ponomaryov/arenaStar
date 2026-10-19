// Own-team fighter hidden in a bush (Art-Spec §6.3, D-014, D-024): the same toon lighting, translucent, no outline.
// A depth pass first (ColorMask 0, "SRPDefaultUnlit" is drawn before "UniversalForward" in URP), then the colour pass with
// ZTest LEqual: only the front surface is blended, inner parts of the body do not show through.
Shader "Arena/ToonCharacterGhost"
{
    Properties
    {
        _BaseColor ("Base Color (alpha = opacity)", Color) = (1, 1, 1, 0.45)
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
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

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
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
        };
        ENDHLSL

        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes v) : SV_POSITION
            {
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half4 Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "GhostForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                ArenaToonSurface s;
                s.albedo = _BaseColor.rgb * i.color.rgb;
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
                return half4(ArenaToonShade(s, p), _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
