// Bush (Art-Spec §2.1, A.3b, D-125; A.3d, D-128): a field of double-sided grass blades, toon lighting of ArenaToon.hlsl without
// an outline. Cull Off: the mesh normal is the soft normal of the whole grass mass, tangent.xyz the blade's own plane normal; the
// lit normal mixes them (_MassNormal), the blade part flipped on back faces — the mass reads as one soft volume of leaves for both
// team cameras (the blades face ±Z).
// Albedo = _BaseColor × vertex colour (dark at the foot, juicy at the top) × a leaf detail map sampled in world space (slanted XZ
// + height, so neighbouring bushes and the static batch need no UVs). Vertex alpha = sway weight (0 at the ground, 1 at the top):
// the vertex moves in XZ by _SwayAmplitude × sin(time · _SwaySpeed + phase of its world position) — meshes are static-batched into
// world space (D-062), the world phase keeps neighbours out of step. Gloss 0 (leaves are matte).
// The same shader is the "see-through" material around a hidden local fighter (D-024): _BaseColor.a < 1, alpha blending and no
// depth write via _SrcBlend/_DstBlend/_ZWrite (set by ViewAssetBuilder), Transparent queue.
Shader "Arena/Bush"
{
    Properties
    {
        _BaseColor ("Base Color (alpha = opacity)", Color) = (1, 1, 1, 1)
        _LeafMap ("Leaf Detail (mean ~ 1)", 2D) = "white" {}
        _LeafTiling ("Units Per Leaf Tile", Float) = 0.9
        _LeafMeanLuminance ("Mean Luminance Of The Leaf Map", Range(0.05, 1)) = 1
        _LeafContrast ("Leaf Contrast", Range(0, 1.5)) = 1
        _MassNormal ("Mass Normal Weight (vs blade normal)", Range(0, 1)) = 0.5
        _SwayAmplitude ("Sway Amplitude (world)", Range(0, 0.2)) = 0.035
        _SwaySpeed ("Sway Speed (rad/s)", Range(0, 6)) = 1.7
        _SwayPhaseScale ("Sway Phase Per World Unit", Range(0, 4)) = 1.3
        _ShadeColor ("Shade Multiplier (deep teal, A.3e)", Color) = (0.5, 0.66, 0.7, 1)
        _RimColor ("Rim Color", Color) = (1, 0.965, 0.878, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.22
        _RimPower ("Rim Power", Range(1, 8)) = 2.5
        _RimTopBias ("Rim Top Bias", Range(0, 1)) = 0.8
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.3
        _StepCenter ("Light Step Center", Range(-1, 1)) = 0.05
        _StepSoftness ("Light Step Softness", Range(0.001, 0.5)) = 0.15
        _SpecSize ("Highlight N.H Threshold", Range(0.5, 1)) = 0.99
        _SpecSoftness ("Highlight Softness", Range(0.001, 0.1)) = 0.01
        _SpecStrength ("Highlight Strength", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Include/ArenaToon.hlsl"
        #include "Include/ArenaEnv.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _LeafMap_ST;
            float _LeafTiling;
            half _LeafMeanLuminance;
            half _LeafContrast;
            half _MassNormal;
            float _SwayAmplitude;
            float _SwaySpeed;
            float _SwayPhaseScale;
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
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
        CBUFFER_END

        TEXTURE2D(_LeafMap);
        SAMPLER(sampler_LeafMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;   // xyz: the blade's plane normal (A.3d)
            half4 color : COLOR;
        };

        float3 SwayedWorld(float3 positionOS, half weight)
        {
            float3 p = TransformObjectToWorld(positionOS);
            float phase = (p.x * 0.8 + p.z * 0.6) * _SwayPhaseScale;
            float t = _Time.y * _SwaySpeed + phase;
            p.xz += float2(sin(t), sin(t * 0.83 + 1.7) * 0.6) * (_SwayAmplitude * weight);
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "BushForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 bladeWS : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = SwayedWorld(v.positionOS.xyz, v.color.a);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.bladeWS = TransformObjectToWorldDir(v.tangentOS.xyz);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float2 uv = (i.positionWS.xz + i.positionWS.yy * float2(0.45, 0.7)) / max(_LeafTiling, 0.001) + _LeafMap_ST.zw;
                half3 leaf = SAMPLE_TEXTURE2D(_LeafMap, sampler_LeafMap, uv).rgb / max(_LeafMeanLuminance, 0.05h);
                leaf = lerp(half3(1.0h, 1.0h, 1.0h), leaf, _LeafContrast);

                ArenaToonSurface s;
                s.albedo = _BaseColor.rgb * i.color.rgb * leaf;
                s.gloss = 0.0h;
                float faceSign = IS_FRONT_VFACE(face, 1.0, -1.0);
                s.normalWS = normalize(i.normalWS) * _MassNormal + normalize(i.bladeWS) * (faceSign * (1.0 - _MassNormal));
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
                return half4(ArenaGrade(ArenaToonShade(s, p)), _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes v) : SV_POSITION
            {
                return TransformWorldToHClip(SwayedWorld(v.positionOS.xyz, v.color.a));
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
