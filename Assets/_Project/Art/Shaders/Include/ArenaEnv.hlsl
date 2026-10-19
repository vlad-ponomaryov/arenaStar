// Shared helpers of the environment shaders (Art-Spec §2.1, A.3b, D-125): soft shadows of walls on the ground without shadow maps.
// MapView bakes a small mask of the map (MapShadowMask: 1 = wall, less = lower caster such as a crate, 0 = open) and sets the
// mask globals; MatchLightRig sets _ArenaShadowReach — the world XZ step from a ground point to its caster at full caster height,
// turned with the camera (shadows fall to the screen right and towards the viewer for both teams, so they are seen beside walls,
// not hidden behind them). A ground point is in shadow if a caster tall enough lies on that way. Globals (not in UnityPerMaterial):
// unset → strength 0 → no shadow (menus, editor previews).
// A.3e (D-130): the mask is RG — R = blurred caster height (soft edges), G = contact occlusion (footprints blurred wide);
// _ArenaShadowParams.w = occlusion strength. _ArenaGrade (MatchLightRig) — a cheap colour grade of the environment instead of a
// post effect: x = saturation change, y = warmth, z = warm lift of the darks (0 = none).
#ifndef ARENA_ENV_INCLUDED
#define ARENA_ENV_INCLUDED

TEXTURE2D(_ArenaShadowMask);
SAMPLER(sampler_ArenaShadowMask);
float4 _ArenaShadowMaskRect;   // xy: world XZ of the mask's corner, zw: 1 / world size
float4 _ArenaShadowParams;     // x: caster height (world), y: strength (0 = off), z: softness of the height test, w: contact AO strength
float4 _ArenaShadowReach;      // xy: world XZ offset to the caster's top
float4 _ArenaGrade;            // x: saturation change, y: warmth, z: warm lift of the darks

half ArenaMaskAt(float2 xz)
{
    float2 uv = (xz - _ArenaShadowMaskRect.xy) * _ArenaShadowMaskRect.zw;
    return SAMPLE_TEXTURE2D_LOD(_ArenaShadowMask, sampler_ArenaShadowMask, uv, 0).r;
}

// 0 = lit, 1 = fully shadowed by a wall. Four taps along the way to the caster; a tap at a fraction t of the caster height is
// shadowed by a caster whose mask value (its relative height) is above t.
half ArenaMapShadow(float3 positionWS)
{
    if (_ArenaShadowParams.y <= 0.0) return 0.0h;
    float2 reach = _ArenaShadowReach.xy;
    half k = 1.0h / max((half)_ArenaShadowParams.z, 0.02h);
    half s = 0.0h;
    s = max(s, saturate((ArenaMaskAt(positionWS.xz + reach * 0.15) - 0.15h) * k + 0.5h));
    s = max(s, saturate((ArenaMaskAt(positionWS.xz + reach * 0.4) - 0.4h) * k + 0.5h));
    s = max(s, saturate((ArenaMaskAt(positionWS.xz + reach * 0.65) - 0.65h) * k + 0.5h));
    s = max(s, saturate((ArenaMaskAt(positionWS.xz + reach * 0.9) - 0.9h) * k + 0.5h));
    return s * (half)_ArenaShadowParams.y;
}

// 0 = open ground, 1 = right at a footprint's edge: contact occlusion around every caster (G is 0.5 at a footprint edge).
half ArenaMapOcclusion(float3 positionWS)
{
    if (_ArenaShadowParams.w <= 0.0) return 0.0h;
    float2 uv = (positionWS.xz - _ArenaShadowMaskRect.xy) * _ArenaShadowMaskRect.zw;
    half g = SAMPLE_TEXTURE2D_LOD(_ArenaShadowMask, sampler_ArenaShadowMask, uv, 0).g;
    return saturate(g * 2.0h) * (half)_ArenaShadowParams.w;
}

half3 ArenaGrade(half3 c)
{
    half l = dot(c, half3(0.3h, 0.59h, 0.11h));
    c = lerp(half3(l, l, l), c, 1.0h + (half)_ArenaGrade.x);
    c *= half3(1.0h + (half)_ArenaGrade.y, 1.0h, 1.0h - (half)_ArenaGrade.y);
    c += (half)_ArenaGrade.z * saturate(1.0h - l * 2.0h) * half3(1.0h, 0.75h, 0.5h);
    return max(c, 0.0h);
}

#endif
