// Water (Art-Spec §2.1, A.3b, D-125): a living toon pond. Vertex alpha = shore factor written by MapMeshBuilder.BuildSheet
// (1 at a non-water neighbour → 0 after the foam width); vertex rgb multiplies the colour. Colour: deep → shallow towards the
// shore; two copies of a ripple map (R = soft cell-edge lines) scroll in different directions in world XZ and their sum, stepped,
// gives moving light glints; a foam band along the shore wobbles with the noise channel (G) and breathes with time, and a thin
// ripple line runs out from it. Main light only tints (water is bright in the shade too); soft wall shadows from the map mask.
// Opaque, one forward pass + depth.
Shader "Arena/Water"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _DeepColor ("Deep", Color) = (0.247, 0.718, 0.910, 1)
        _ShallowColor ("Shallow (near the shore)", Color) = (0.42, 0.82, 0.95, 1)
        _GlintColor ("Glint", Color) = (0.561, 0.890, 1.0, 1)
        _FoamColor ("Foam", Color) = (0.910, 0.984, 1.0, 1)
        _RippleMap ("Ripple (R lines, G noise)", 2D) = "black" {}
        _RippleTiling ("Units Per Ripple Tile", Float) = 2.2
        _FlowSpeed ("Flow Speed (units/s)", Range(0, 1)) = 0.12
        _GlintThreshold ("Glint Threshold (sum of two lines)", Range(0, 2)) = 1.4
        _GlintSoftness ("Glint Softness", Range(0.001, 0.3)) = 0.08
        _LineStrength ("Ripple Line Lightening", Range(0, 1)) = 0.14
        _GlintStrength ("Glint Strength", Range(0, 1)) = 0.85
        _FoamEdge ("Foam Edge (shore factor)", Range(0, 1)) = 0.72
        _FoamWobble ("Foam Wobble", Range(0, 1.5)) = 0.9
        _FoamBreath ("Foam Breathing", Range(0, 0.3)) = 0.06
        _FoamSpeed ("Foam Speed (rad/s)", Range(0, 6)) = 1.6
        _RingStrength ("Ripple Ring Strength", Range(0, 1)) = 0.55
        _RingWidth ("Ripple Ring Width", Range(0.005, 0.2)) = 0.045
        _LightInfluence ("Light Influence", Range(0, 1)) = 0.35
        _MapShadowColor ("Wall Shadow Multiplier", Color) = (0.56, 0.6, 0.78, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Include/ArenaToon.hlsl"
        #include "Include/ArenaEnv.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _DeepColor;
            half4 _ShallowColor;
            half4 _GlintColor;
            half4 _FoamColor;
            float4 _RippleMap_ST;
            float _RippleTiling;
            float _FlowSpeed;
            half _GlintThreshold;
            half _GlintSoftness;
            half _GlintStrength;
            half _LineStrength;
            half _FoamEdge;
            half _FoamWobble;
            half _FoamBreath;
            float _FoamSpeed;
            half _RingStrength;
            half _RingWidth;
            half _LightInfluence;
            half4 _MapShadowColor;
        CBUFFER_END

        TEXTURE2D(_RippleMap);
        SAMPLER(sampler_RippleMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
        };
        ENDHLSL

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 uv : TEXCOORD1;   // two scrolling ripple samples
                float2 uvNoise : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                float2 xz = o.positionWS.xz / max(_RippleTiling, 0.001);
                float t = _Time.y * _FlowSpeed / max(_RippleTiling, 0.001);
                o.uv.xy = xz + float2(t, t * 0.35);
                float2 turned = float2(xz.x * 0.6 - xz.y * 0.8, xz.x * 0.8 + xz.y * 0.6) * 0.83;
                o.uv.zw = turned + float2(-t * 0.6, t * 0.9) + float2(0.37, 0.61);
                o.uvNoise = xz * 0.5 + float2(t * 0.2, -t * 0.15);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half shore = i.color.a;
                half3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(shore * 1.6h));

                half lines = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, i.uv.xy).r + SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, i.uv.zw).r;
                half glint = smoothstep(_GlintThreshold - _GlintSoftness, _GlintThreshold + _GlintSoftness, lines);
                col = lerp(col, _GlintColor.rgb, saturate(lines) * _LineStrength);
                col = lerp(col, _GlintColor.rgb, glint * _GlintStrength * (1.0h - shore * 0.5h));

                half noise = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, i.uvNoise).g;
                half breath = sin(_Time.y * _FoamSpeed) * _FoamBreath;
                half f = shore + (noise - 0.5h) * _FoamWobble + breath;
                half foam = smoothstep(_FoamEdge - 0.03h, _FoamEdge + 0.03h, f);
                // A thin line running out from the shore and fading (period ≈ 2π / _FoamSpeed).
                half ringPos = frac(_Time.y * _FoamSpeed * 0.16h);
                half ringAt = _FoamEdge * (1.0h - ringPos);
                half ring = (1.0h - smoothstep(_RingWidth * 0.5h, _RingWidth, abs(f - ringAt))) * (1.0h - ringPos) * step(0.02h, shore);
                col = lerp(col, _FoamColor.rgb, saturate(max(foam, ring * _RingStrength)));

                Light light = GetMainLight();
                col *= lerp(half3(1.0h, 1.0h, 1.0h), light.color, _LightInfluence);
                col *= _BaseColor.rgb * i.color.rgb;
                half shadow = ArenaMapShadow(i.positionWS);
                return half4(col * lerp(half3(1.0h, 1.0h, 1.0h), _MapShadowColor.rgb, shadow), 1.0h);
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
