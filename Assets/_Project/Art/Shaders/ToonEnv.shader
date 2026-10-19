// Environment (Art-Spec §2.1, §6.3, D-110): the toon lighting of the fighters (ArenaToon.hlsl) without an outline — fighters
// pop out of the background. Albedo = _BaseColor × vertex colour (occlusion and wall colours are baked into the vertices by
// MapMeshBuilder) × _BaseMap sampled in WORLD XZ (one shared material for any map size): _WorldTiling cells per texture tile, plus
// a second, larger and turned sample of the same texture that only modulates the brightness (_MacroStrength) so the tile does
// not visibly repeat. Vertex alpha = gloss (environment: matte; berries of the berry box glossy).
// A.3b (D-125): side faces (|n.y| < 0.5) take _SideMap in world (x or z, y) — planks on wall sides, the crate, wicker of the
// basket; _BaseNormalize / _SideNormalize divide a map by its mean luminance (the vertex colour keeps the palette, the map adds
// only detail). _ReceiveMapShadow: soft wall shadows from the map mask (ArenaEnv.hlsl) — on the floor and the meadow, not on walls.
// A.3e (D-130): warm clay shading (soft light step, warm shade), a darker foot on side faces, warm ground shadows and the contact
// occlusion of the mask (G), the shader-side grade (ArenaGrade).
// uv0.x = 1 turns the texture detail off for a vertex (berries and leaves of the basket); meshes without UVs are textured.
// Opaque, one forward pass + depth.
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
        _BaseNormalize ("Divide Base Map By Its Mean (detail only)", Range(0, 1)) = 0
        _SideMap ("Side Map (world x|z, y)", 2D) = "white" {}
        _SideTiling ("Units Per Side Tile", Float) = 2.4
        _SideMeanLuminance ("Mean Luminance Of The Side Map", Range(0.05, 1)) = 1
        _SideNormalize ("Divide Side Map By Its Mean", Range(0, 1)) = 0
        _SideContrast ("Side Map Contrast", Range(0, 1.5)) = 1
        _PatchMap ("Patch Map (grass, world XZ)", 2D) = "white" {}
        _PatchTiling ("Cells Per Patch Tile", Float) = 4
        _PatchMeanLuminance ("Mean Luminance Of The Patch Map", Range(0.05, 1)) = 1
        _PatchTint ("Patch Tint", Color) = (1, 1, 1, 1)
        _PatchEdge ("Patch Edge Softness", Range(0.01, 0.5)) = 0.12
        _PatchBreakup ("Patch Edge Breakup By The Map", Range(0, 2)) = 0.6
        _ReceiveMapShadow ("Receive Wall Shadows (map mask)", Range(0, 1)) = 0
        _MapShadowColor ("Ground Shadow Multiplier (warm)", Color) = (0.63, 0.45, 0.34, 1)
        _MapAoColor ("Contact Occlusion Multiplier", Color) = (0.6, 0.4, 0.28, 1)
        _ShadeColor ("Shade Multiplier (warm clay)", Color) = (0.8, 0.64, 0.56, 1)
        _SideGradient ("Side Darkening At The Foot", Range(0, 1)) = 0.3
        _SideGradientHeight ("Side Gradient Height (world)", Float) = 0.9
        _RimColor ("Rim Color", Color) = (1, 0.965, 0.878, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12
        _RimPower ("Rim Power", Range(1, 8)) = 3
        _RimTopBias ("Rim Top Bias", Range(0, 1)) = 0.6
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.25
        _StepCenter ("Light Step Center", Range(-1, 1)) = 0.1
        _StepSoftness ("Light Step Softness", Range(0.001, 0.5)) = 0.4
        _SpecSize ("Highlight N.H Threshold", Range(0.5, 1)) = 0.99
        _SpecSoftness ("Highlight Softness", Range(0.001, 0.1)) = 0.01
        _SpecStrength ("Highlight Strength", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Include/ArenaToon.hlsl"
        #include "Include/ArenaEnv.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _BaseMap_ST;
            float _WorldTiling;
            float _MacroTiling;
            half _MacroStrength;
            half _MapMeanLuminance;
            half _MapContrast;
            half _BaseNormalize;
            float4 _SideMap_ST;
            float _SideTiling;
            half _SideMeanLuminance;
            half _SideNormalize;
            half _SideContrast;
            float _PatchTiling;
            half _PatchMeanLuminance;
            half4 _PatchTint;
            half _PatchEdge;
            half _PatchBreakup;
            half _ReceiveMapShadow;
            half4 _MapShadowColor;
            half4 _MapAoColor;
            half4 _ShadeColor;
            half _SideGradient;
            float _SideGradientHeight;
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
        TEXTURE2D(_SideMap);
        SAMPLER(sampler_SideMap);
        TEXTURE2D(_PatchMap);
        SAMPLER(sampler_PatchMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv0 : TEXCOORD0;   // x = 1: no texture detail (berries, leaves); y = grass patch weight (floor); no UVs read 0
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
                half2 plainPatch : TEXCOORD3; // x: no texture detail, y: grass patch weight
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.plainPatch = half2(saturate(v.uv0.x), saturate(v.uv0.y));
                float2 xz = o.positionWS.xz;
                o.uv.xy = xz / max(_WorldTiling, 0.001) + _BaseMap_ST.zw;
                float2 turned = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
                o.uv.zw = turned / max(_MacroTiling, 0.001) + float2(0.31, 0.73);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.xy).rgb;
                tex /= lerp(1.0h, max(_MapMeanLuminance, 0.05h), _BaseNormalize);
                // Calmer map (meadow): pull towards the mean brightness, keep the hue.
                half texLum = dot(tex, half3(0.3h, 0.59h, 0.11h));
                tex *= lerp(_MapMeanLuminance / max(texLum, 0.02h), 1.0h, _MapContrast);
                half3 macro = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.zw).rgb;
                // Brightness of the macro sample relative to the map average (a white map gives exactly 1: walls stay flat).
                half macroLum = dot(macro, half3(0.3h, 0.59h, 0.11h));
                half variation = lerp(1.0h, macroLum / max(_MapMeanLuminance, 0.05h), _MacroStrength);

                // Side faces: the side map along the face (u = world z on ±X faces, world x on ±Z faces; v = height).
                float3 an = abs(i.normalWS);
                float2 sideUv = float2(an.x > an.z ? i.positionWS.z : i.positionWS.x, i.positionWS.y) / max(_SideTiling, 0.001) + _SideMap_ST.zw;
                half3 side = SAMPLE_TEXTURE2D(_SideMap, sampler_SideMap, sideUv).rgb;
                side /= lerp(1.0h, max(_SideMeanLuminance, 0.05h), _SideNormalize);
                side = lerp(half3(1.0h, 1.0h, 1.0h), side, _SideContrast);
                half isSide = step(an.y, 0.5);
                tex *= variation;
                // A.3c: grass patches on the floor (uv0.y weight); the patch map's own brightness breaks the edge up.
                half3 patch = SAMPLE_TEXTURE2D(_PatchMap, sampler_PatchMap, i.positionWS.xz / max(_PatchTiling, 0.001)).rgb;
                half patchLum = dot(patch, half3(0.3h, 0.59h, 0.11h)) / max(_PatchMeanLuminance, 0.05h);
                half patchW = smoothstep(0.5h - _PatchEdge, 0.5h + _PatchEdge, i.plainPatch.y + (patchLum - 1.0h) * _PatchBreakup * i.plainPatch.y);
                tex = lerp(tex, patch * _PatchTint.rgb, patchW * step(0.001h, i.plainPatch.y));
                tex = lerp(lerp(tex, side, isSide), half3(1.0h, 1.0h, 1.0h), i.plainPatch.x);

                ArenaToonSurface s;
                s.albedo = _BaseColor.rgb * i.color.rgb * tex;
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
                half3 color = ArenaToonShade(s, p);
                // A.3e: vertical gradient on side faces (the foot of every object is darker — the clay look without AO passes).
                half sideness = saturate(1.0h - abs((half)i.normalWS.y) * 1.6h);
                half foot = saturate(i.positionWS.y / max(_SideGradientHeight, 0.01));
                color *= 1.0h - _SideGradient * sideness * (1.0h - foot);
                half shadow = ArenaMapShadow(i.positionWS) * _ReceiveMapShadow;
                half ao = ArenaMapOcclusion(i.positionWS) * _ReceiveMapShadow;
                color *= lerp(half3(1.0h, 1.0h, 1.0h), _MapShadowColor.rgb, shadow);
                color *= lerp(half3(1.0h, 1.0h, 1.0h), _MapAoColor.rgb, ao);
                return half4(ArenaGrade(color), 1.0h);
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
