// Animated water (marsh) and lava (ember) for HD-2D. Ground plane is XY, up is -Z.
Shader "TWF/Water"
{
    Properties
    {
        _Lava ("Lava (0 = water, 1 = lava)", Float) = 0
        _Deep ("Deep Color", Color) = (0.07, 0.17, 0.16, 1)
        _Shallow ("Shallow Color", Color) = (0.2, 0.36, 0.3, 1)
        _Hot ("Lava Hot", Color) = (1.0, 0.75, 0.2, 1)
        _Cool ("Lava Crust", Color) = (0.45, 0.07, 0.03, 1)
        _PixelSize ("Pixel Size", Float) = 16
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" "RenderType"="Opaque" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 _TWF_Ambient;

            CBUFFER_START(UnityPerMaterial)
                half _Lava;
                half4 _Deep, _Shallow, _Hot, _Cool;
                float _PixelSize;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; };

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }

            float2 hash2(float2 p)
            {
                return frac(sin(float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)))) * 43758.5453);
            }

            // Distances to the nearest and second-nearest wobbling cell centres.
            float2 voronoi(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float d1 = 8, d2 = 8;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = hash2(i + g);
                    o = 0.5 + 0.38 * sin(_Time.y * 0.4 + o * 6.2831);
                    float d = length(g + o - f);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                return float2(d1, d2);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Snap to the pixel grid so it matches the pixel art.
                float2 p = floor(i.positionWS.xy * _PixelSize) / _PixelSize;
                float t = _Time.y;
                half3 color;
                if (_Lava > 0.5)
                {
                    // Crust plates drift on a warped flow; the gaps between them are bright cracks.
                    float2 flow = float2(t * 0.11, t * 0.045);
                    float2 warp = float2(noise(p * 0.45 + t * 0.06), noise(p * 0.45 + 17.3 - t * 0.05)) - 0.5;
                    float2 q = p * 1.1 + warp * 1.6 + flow;
                    float2 cracks = voronoi(q);
                    float crack = 1 - smoothstep(0.02, 0.16, cracks.y - cracks.x);
                    // Hot currents well up and fade, melting through the crust.
                    float heat = noise(p * 0.7 - flow * 1.7) * 0.65 + noise(p * 2.1 + float2(-t * 0.35, t * 0.2)) * 0.35;
                    heat = saturate(heat + 0.12 * sin(t * 1.3 + heat * 9.0));
                    float melt = smoothstep(0.6, 0.88, heat) * 0.85;
                    float crust = noise(q * 3.1 + 5.0); // mottled plates
                    float glow = saturate(max(crack * (0.5 + heat * 0.5), melt));
                    glow = floor(glow * 5) / 5;          // banded, like the pixel art
                    half3 crustCol = _Cool.rgb * (0.45 + crust * 0.5) + _Hot.rgb * 0.08 * heat;
                    half3 hotCol = lerp(_Hot.rgb * float3(1.0, 0.5, 0.25) * 1.4, _Hot.rgb * float3(1.0, 0.85, 0.6) * 2.2, glow * glow);
                    color = lerp(crustCol, hotCol, glow);
                    color = MixFog(color, i.fog);
                    return half4(color, 1);
                }
                float w = noise(p * 1.5 + float2(t * 0.3, t * 0.2)) * 0.5 + noise(p * 3.7 - float2(t * 0.25, 0)) * 0.5;
                color = lerp(_Deep.rgb, _Shallow.rgb, w);
                // Sparkles where ripples peak.
                float glint = step(0.93, noise(p * 6 + t * 0.8));
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                color = color * (light.color * light.shadowAttenuation * 0.6 + _TWF_Ambient.rgb) + glint * light.color * 0.6;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
