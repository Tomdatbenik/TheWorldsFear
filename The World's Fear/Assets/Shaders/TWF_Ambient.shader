// Ambient life drawn from one static mesh per chunk: fireflies, butterflies, embers, ash and mist.
// Every particle is a quad whose four corners share an anchor; all motion happens here in the
// vertex shader, so the CPU never touches them after the chunk is built.
// Ground plane is XY, up is -Z. Premultiplied alpha: glows return alpha 0 (additive), the rest blend normally.
Shader "TWF/Ambient"
{
    Properties
    {
        _MainTex ("Butterfly Atlas", 2D) = "white" {}
        _Atlas ("Atlas (variants, frames)", Vector) = (4, 3, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Ambient"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _TWF_Ambient;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Atlas;
            CBUFFER_END

            #define KIND_FIREFLY 0
            #define KIND_BUTTERFLY 1
            #define KIND_EMBER 2
            #define KIND_ASH 3
            #define KIND_MIST 4
            #define KIND_BEAM 5   // a shaft of sunlight leaning toward the sun; data.w = how high it reaches
            #define KIND_LEAF 6
            #define KIND_MOTE 7   // dust glinting in a sunbeam; data.w = height along the beam 0..1

            struct Attributes
            {
                float3 positionOS : POSITION;  // anchor (same for all four corners)
                float2 corner : TEXCOORD0;     // -0.5..0.5
                float4 data : TEXCOORD1;       // kind, seed 0..1, size, spare
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float4 info : TEXCOORD1; // kind, brightness/alpha, atlas variant, atlas frame
                half4 color : COLOR;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = _Time.y;
                int kind = (int)round(v.data.x);
                float s = v.data.y;
                float size = v.data.z;
                float ph = s * 6.2831853;
                float3 anchor = TransformObjectToWorld(v.positionOS);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float2 c = v.corner;
                float3 off = 0;
                float level = 1;
                float2 cell = 0;
                bool flat = false;

                if (kind == KIND_FIREFLY)
                {
                    off = float3(sin(t * 0.37 + ph) * 0.9 + sin(t * 0.83 + ph * 3.1) * 0.35,
                                 cos(t * 0.29 + ph * 1.7) * 0.9 + sin(t * 0.71 + ph * 2.3) * 0.35,
                                 -(0.35 + 0.45 * (0.5 + 0.5 * sin(t * 0.5 + ph * 5.0))));
                    float b = saturate(sin(t * (0.9 + s * 0.8) + ph * 7.0) * 1.6 - 0.4);
                    // Brighter in dim places (marsh, deep forest); still faintly there in the village sun.
                    float dark = saturate(1.45 - dot(_TWF_Ambient.rgb, 0.3333) * 2.5);
                    level = (0.12 + b * b) * (0.35 + dark * 0.75);
                }
                else if (kind == KIND_BUTTERFLY)
                {
                    float dir = s > 0.5 ? 1.0 : -1.0;
                    float k = (0.35 + frac(s * 7.31) * 0.3) * dir;
                    float a = t * k + ph;
                    off = float3(cos(a) * 1.6 + sin(a * 2.3) * 0.5,
                                 sin(a * 1.2) * 1.2 + sin(t * 1.7 + ph * 3.0) * 0.35,
                                 -(0.6 + 0.22 * sin(t * 2.7 + ph)));
                    float3 vel = float3(-sin(a) * 1.6 + cos(a * 2.3) * 1.15, cos(a * 1.2) * 1.44, 0) * k;
                    float facing = dot(vel, right) >= 0 ? 1.0 : -1.0;
                    c.x *= facing;
                    float f = floor(frac(t * 7.0 + s * 5.0) * 4.0); // 0 1 2 1: open, half, closed, half
                    cell = float2(floor(frac(s * 13.7) * _Atlas.x), f > 2.5 ? 1.0 : f);
                }
                else if (kind == KIND_EMBER)
                {
                    float life = frac(t * (0.18 + s * 0.15) + s * 7.3);
                    off = float3(sin(life * 5.0 + ph * 4.0) * 0.35 + (s - 0.5) * 0.8 * life,
                                 cos(life * 3.0 + ph) * 0.25 + life * 0.4,
                                 -0.1 - life * 4.0);
                    level = 2.0 * pow(1.0 - life, 1.5) * smoothstep(0.0, 0.06, life) * (0.65 + 0.35 * sin(t * 19.0 + ph * 30.0));
                    size *= 1.0 - life * 0.5;
                }
                else if (kind == KIND_ASH)
                {
                    float life = frac(t * (0.05 + s * 0.04) + s * 3.7);
                    off = float3(sin(t * 0.6 + ph * 5.0) * 0.6 + (life - 0.5) * 2.0,
                                 cos(t * 0.4 + ph * 3.0) * 0.4 - (life - 0.5),
                                 -5.0 * (1.0 - life));
                    level = smoothstep(0.0, 0.15, life) * smoothstep(1.0, 0.85, life) * 0.75;
                }
                else if (kind == KIND_MIST) // a soft puff lying on the water
                {
                    off = float3(sin(t * 0.07 + ph * 6.0) * 1.2, cos(t * 0.05 + ph * 4.0) * 0.8, -0.2);
                    level = 0.6 + 0.4 * sin(t * 0.3 + ph * 9.0);
                    flat = true;
                }
                else if (kind == KIND_LEAF)
                {
                    float life = frac(t * (0.06 + s * 0.04) + s * 5.1);
                    off = float3(sin(t * 1.3 + ph) * 0.8 + (life - 0.5) * 1.5,
                                 cos(t * 0.9 + ph * 2.0) * 0.4,
                                 -5.5 * (1.0 - life));
                    c.x *= cos(t * (2.0 + s * 2.0) + ph); // tumbling
                    level = smoothstep(0.0, 0.08, life) * smoothstep(1.0, 0.93, life);
                }

                // Sunbeams and their dust follow the sun's direction, so each biome's light leans its own way.
                float3 toSun = normalize(_MainLightPosition.xyz);
                float3 beamAxis = toSun * (6.0 / max(0.25, -toSun.z));
                // Sunbeams are fully lit in bright biomes and fade in dim ones.
                float day = saturate(dot(_MainLightColor.rgb, 0.3333) * 1.4);
                if (kind == KIND_MOTE)
                {
                    float h = frac(v.data.w + t * 0.015 * (s - 0.5));
                    off = beamAxis * h * 0.85 + float3(sin(t * 0.21 + ph * 3.0), cos(t * 0.17 + ph * 5.0), sin(t * 0.13 + ph)) * 0.35;
                    float tw = saturate(sin(t * (0.6 + s) + ph * 11.0));
                    level = (0.15 + tw * tw * 0.85) * day * 0.5;
                }

                float3 quad = flat ? float3(c.x, c.y, 0) * size : (right * c.x + up * c.y) * size;
                float3 positionWS = anchor + off + quad;
                if (kind == KIND_BEAM)
                {
                    // A ribbon from the ground up toward the sun, turned to face the camera, a little wider at the top.
                    float along = c.y + 0.5;
                    float3 side = cross(beamAxis, UNITY_MATRIX_V[2].xyz);
                    side = normalize(side) * (dot(side, right) < 0 ? -1.0 : 1.0);
                    positionWS = anchor + beamAxis * along + side * c.x * size * (0.8 + 0.4 * along);
                    level = (0.7 + 0.3 * sin(t * 0.35 + ph) * sin(t * 0.21 + ph * 3.0)) * day;
                    cell = float2(s, 0);
                }
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.corner + 0.5; // a flipped c.x mirrors the quad while the uv stays put
                o.info = float4(kind, level, cell);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                int kind = (int)round(i.info.x);
                float level = i.info.y;

                if (kind == KIND_BEAM)
                {
                    // Pixel-stepped falloff across the shaft; it fades out up high and glows faintly where it meets the ground.
                    float2 q = (floor(i.uv * float2(12.0, 28.0)) + 0.5) / float2(12.0, 28.0);
                    float core = saturate(1.0 - abs(q.x - 0.5) * 2.0);
                    float fade = smoothstep(0.0, 0.08, q.y) * smoothstep(1.0, 0.6, q.y);
                    float streak = 0.75 + 0.25 * sin(q.y * 9.0 - _Time.y * 0.45 + i.info.z * 40.0);
                    float a = floor(pow(core, 1.4) * fade * streak * 4.0) / 4.0;
                    half3 col = _MainLightColor.rgb * i.color.rgb * a * level * 0.85;
                    col = MixFogColor(col, half3(0, 0, 0), i.fog);
                    return half4(col, 0);
                }
                if (kind == KIND_LEAF)
                {
                    float2 q = (floor(i.uv * 5.0) + 0.5) / 5.0;
                    float leaf = abs(q.x - 0.5) * 1.6 + abs(q.y - 0.5) < 0.45 ? 1.0 : 0.0;
                    float a = leaf * level;
                    half3 lit = _TWF_Ambient.rgb * 1.2 + _MainLightColor.rgb * 0.85;
                    half3 col = MixFog(i.color.rgb * lit * (q.y > 0.5 ? 1.0 : 0.8), i.fog);
                    return half4(col * a, a);
                }
                if (kind == KIND_FIREFLY || kind == KIND_EMBER || kind == KIND_MOTE)
                {
                    // An 8x8 pixel glow: a bright 2x2 core and a soft halo.
                    float2 q = (floor(i.uv * 8.0) + 0.5) / 8.0;
                    float d = length(q - 0.5) * 2.0;
                    float core = d < 0.3 ? 1.0 : 0.0;
                    float halo = saturate(1.0 - d);
                    half3 col = i.color.rgb * (core * 4.0 + halo * halo * 1.2) * level;
                    col = MixFogColor(col, half3(0, 0, 0), i.fog);
                    return half4(col, 0);
                }
                if (kind == KIND_BUTTERFLY)
                {
                    float2 uv = (i.info.zw + saturate(i.uv)) / _Atlas.xy;
                    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                    clip(tex.a - 0.5);
                    half3 lit = _TWF_Ambient.rgb * 1.2 + _MainLightColor.rgb * 0.85;
                    half3 col = MixFog(tex.rgb * i.color.rgb * lit, i.fog);
                    return half4(col, 1);
                }
                if (kind == KIND_ASH)
                {
                    float2 q = (floor(i.uv * 4.0) + 0.5) / 4.0;
                    float a = (abs(q.x - 0.5) < 0.3 && abs(q.y - 0.5) < 0.3 ? 1.0 : 0.0) * level;
                    half3 col = MixFog(i.color.rgb * (_TWF_Ambient.rgb * 1.5 + 0.35), i.fog);
                    return half4(col * a, a);
                }
                // Mist: a pixelated soft blob in banded steps.
                float2 m = (floor(i.uv * 20.0) + 0.5) / 20.0;
                float md = length(m - 0.5) * 2.0;
                float ma = floor(saturate(1.0 - md) * saturate(1.0 - md) * 4.0) / 4.0 * i.color.a * level;
                half3 mc = MixFog(i.color.rgb * (_TWF_Ambient.rgb * 1.2 + 0.45), i.fog);
                return half4(mc * ma, ma);
            }
            ENDHLSL
        }
    }
}
