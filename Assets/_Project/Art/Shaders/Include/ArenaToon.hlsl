// Shared toon lighting of the Arena/* shaders (Art-Spec §6.3, D-110): stepped main light, lilac shade, ambient SH, rim,
// stepped Blinn-Phong highlight scaled by gloss (vertex colour alpha), pixel-constant outline offset.
// The CBUFFER lives in each shader (identical in all its passes, SRP Batcher); this file only has functions.
#ifndef ARENA_TOON_INCLUDED
#define ARENA_TOON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct ArenaToonSurface
{
    half3 albedo;
    half gloss;         // 0 matte (fur), 1 glossy (eyes, helmet)
    float3 normalWS;
    float3 positionWS;
};

struct ArenaToonParams
{
    half3 shadeColor;   // shade = albedo × shadeColor
    half3 rimColor;
    half rimStrength;
    half rimPower;
    half rimTopBias;    // 0: rim equal everywhere, 1: only on upper faces
    half ambientStrength;
    half stepCenter;    // N·L of the light/shade border
    half stepSoftness;  // half width of the border (no MSAA: a hard step shimmers)
    half specSize;      // N·H threshold of the highlight
    half specSoftness;
    half specStrength;
};

half3 ArenaToonShade(ArenaToonSurface s, ArenaToonParams p)
{
    Light light = GetMainLight();
    half3 n = normalize(s.normalWS);
    half3 v = GetWorldSpaceNormalizeViewDir(s.positionWS);
    half3 l = light.direction;

    half ndl = dot(n, l);
    half lit = smoothstep(p.stepCenter - p.stepSoftness, p.stepCenter + p.stepSoftness, ndl);
    half3 color = lerp(s.albedo * p.shadeColor, s.albedo * light.color, lit);

    color += s.albedo * SampleSH(n) * p.ambientStrength;

    half rim = pow(1.0h - saturate(dot(n, v)), p.rimPower) * p.rimStrength;
    rim *= lerp(1.0h, saturate(n.y * 0.5h + 0.5h), p.rimTopBias);
    color += p.rimColor * rim;

    half ndh = saturate(dot(n, SafeNormalize(l + v)));
    half spec = smoothstep(p.specSize - p.specSoftness, p.specSize + p.specSoftness, ndh) * lit;
    color += light.color * spec * s.gloss * p.specStrength;
    return color;
}

// Clip-space position pushed out along the normal by a constant number of pixels: `pixelsAtRef` at a screen of
// `refHeight` pixels (scales with the resolution, does not depend on the distance).
float4 ArenaToonOutlinePosition(float3 positionOS, float3 normalOS, float pixelsAtRef, float refHeight)
{
    float4 positionCS = TransformObjectToHClip(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);
    float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
    float len = length(normalCS);
    float2 dir = len > 1e-5 ? normalCS / len : float2(0.0, 0.0);
    float2 offset = dir * (pixelsAtRef * 2.0 / refHeight) * positionCS.w;
    offset.x *= _ScreenParams.y / _ScreenParams.x;
    positionCS.xy += offset;
    return positionCS;
}

// Outline colour: own albedo darkened, mixed with the outline colour (a soft chocolate, not black).
half3 ArenaToonOutlineColor(half3 albedo, half darken, half3 outlineColor, half mix)
{
    return lerp(albedo * darken, outlineColor, mix);
}

#endif
