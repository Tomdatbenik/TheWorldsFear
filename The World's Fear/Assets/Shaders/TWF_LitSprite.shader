// HD-2D sprite shader for URP: pixel-art sprites that receive light, cast and receive
// shadows, glow (emission) and optionally sway in the wind.
// The world ground is the XY plane and "up" is -Z.
Shader "TWF/LitSprite"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Emission ("Emission", Range(0,8)) = 0
        _Wind ("Wind Sway", Range(0,1)) = 0
        _Ambient ("Ambient Boost", Range(0,2)) = 1
        _FlatGround ("Ground (no self shading)", Float) = 0
        _MeshNormals ("Use Mesh Normals (cliffs)", Float) = 0
        _NormalMap ("Normal Map (pixel art)", 2D) = "bump" {}
        _UseNormalMap ("Use Normal Map", Float) = 0
        _Bands ("Light Bands", Range(2,8)) = 4
        _ShadowTint ("Shadow Tint", Color) = (0.42,0.36,0.62,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_NormalMap);
        SAMPLER(sampler_NormalMap);

        // Set by ZoneLighting for the whole scene.
        half4 _TWF_Ambient;

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cutoff;
            half _Emission;
            half _Wind;
            half _Ambient;
            half _FlatGround;
            half _MeshNormals;
            half _UseNormalMap;
            half _Bands;
            half4 _ShadowTint;
        CBUFFER_END

        // Sway the top of the sprite; the base (uv.y = 0) stays put.
        float3 ApplyWind(float3 positionWS, float2 uv)
        {
            float phase = _Time.y * 1.7 + positionWS.x * 0.35 + positionWS.y * 0.27;
            float sway = (sin(phase) * 0.6 + sin(phase * 2.3) * 0.25) * _Wind * uv.y * uv.y;
            positionWS.x += sway * 0.25;
            positionWS.y += sway * 0.12;
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fog : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float3 tangentWS : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
                float3 faceWS : TEXCOORD6;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws = ApplyWind(ws, v.uv);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                // The sprite's own axes in the world, for normal-mapped pixel art: right, up, toward the viewer.
                o.tangentWS = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                o.bitangentWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                o.faceWS = normalize(TransformObjectToWorldDir(float3(0, 0, -1)));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                clip(tex.a - _Cutoff);

                // Sprites are lit as if they face the sky, so the whole sprite gets even light
                // no matter which way the billboard turns. Shadows still fall per pixel.
                float3 up = float3(0, 0, -1);
                float3 n = _MeshNormals > 0.5 ? normalize(i.normalWS) : up;
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // Normal-mapped pixel art: each pixel is lit from its own surface direction and the
                // result snaps to a few bands, with cool tinted shadows, like hand-shaded sprites.
                if (_UseNormalMap > 0.5)
                {
                    half3 nm = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv).xyz * 2 - 1;
                    float3 pn = normalize(i.tangentWS * nm.x + i.bitangentWS * nm.y + i.faceWS * nm.z);
                    half key = saturate(dot(pn, mainLight.direction)) * mainLight.shadowAttenuation;
                    half band = floor(key * _Bands + 0.5) / _Bands;
                    half3 lit = lerp(_ShadowTint.rgb * 0.55, mainLight.color * 1.15, band);
                    half3 pointLit = 0;
                    #if defined(_ADDITIONAL_LIGHTS)
                    uint pc = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    InputData pid = (InputData)0;
                    pid.positionWS = i.positionWS;
                    pid.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    LIGHT_LOOP_BEGIN(pc)
                        Light pl = GetAdditionalLight(lightIndex, i.positionWS);
                        half pk = saturate(dot(pn, pl.direction) * 0.8 + 0.2) * pl.distanceAttenuation;
                        pointLit += pl.color * floor(pk * _Bands + 0.5) / _Bands;
                    LIGHT_LOOP_END
                    #else
                    for (uint pj = 0; pj < pc; pj++)
                    {
                        Light pl = GetAdditionalLight(pj, i.positionWS);
                        half pk = saturate(dot(pn, pl.direction) * 0.8 + 0.2) * pl.distanceAttenuation;
                        pointLit += pl.color * floor(pk * _Bands + 0.5) / _Bands;
                    }
                    #endif
                    #endif
                    half3 nmColor = tex.rgb * (lit + _TWF_Ambient.rgb * _Ambient * 0.6 + pointLit) + tex.rgb * _Emission;
                    return half4(MixFog(nmColor, i.fog), 1);
                }

                half ndl = saturate(dot(n, mainLight.direction) * 0.5 + 0.5);
                half3 light = mainLight.color * ndl * mainLight.shadowAttenuation;

                half3 ambient = _TWF_Ambient.rgb * _Ambient;

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    light += l.color * l.distanceAttenuation;
                LIGHT_LOOP_END
                #else
                for (uint li = 0; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    light += l.color * l.distanceAttenuation;
                }
                #endif
                #endif

                half3 color = tex.rgb * (light + ambient) + tex.rgb * _Emission;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 ws = ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.uv);
                float3 normalWS = float3(0, 0, -1);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - ws);
                #else
                float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, normalWS, lightDir));
                o.positionCS = ApplyShadowClamping(cs);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alpha = v.color.a * _Color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.alpha;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.uv);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alpha = v.color.a * _Color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
