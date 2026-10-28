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
                    float n = noise(p * 0.8 + float2(t * 0.15, t * 0.1)) * 0.6 + noise(p * 2.3 - t * 0.3) * 0.4;
                    color = lerp(_Cool.rgb, _Hot.rgb * 2.5, smoothstep(0.45, 0.8, n));
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
