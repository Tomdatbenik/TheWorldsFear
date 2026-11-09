// World art baked from 3D (blender/env_kit.py): a flat camera-facing sprite that sorts per pixel.
// Alpha holds, for each pixel, how far along the view its spot on the ground lies from the sprite's own plane
// (0 = empty, 1..255 = -_DepthRange.._DepthRange). Written as depth, so a hero standing behind a wall is hidden by
// it and one standing in front is not, wherever the wall is on the sprite.
// _WallMode: the same rule for real 3D walls (mesh normals, depth taken at the wall's foot, _BaseZ).
// Ground is the XY plane, up is -Z.
Shader "TWF/EnvSprite"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _DepthRange ("Depth Range", Float) = 10
        _WallMode ("Wall Mode", Float) = 0
        _BaseZ ("Wall Foot Z", Float) = 0
        _GlowTex ("Night Glow Mask", 2D) = "black" {}
        _GlowStrength ("Night Glow Strength", Float) = 2.2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/TWF_Reveal.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_GlowTex);
        SAMPLER(sampler_GlowTex);
        float _TWF_Day;
        half4 _TWF_Ambient;

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            float _DepthRange;
            float _WallMode;
            float _BaseZ;
            float _GlowStrength;
        CBUFFER_END

        // A sprite's lower half leans below the ground plane, and its baked depth drifts while the camera swings.
        // Never let a pixel fall behind the ground its own view ray meets.
        float KeepAboveGround(float3 positionWS, float depth)
        {
            // Orthographic cameras look along one direction; perspective ones along the ray through the pixel.
            float3 ray = unity_OrthoParams.w > 0.5 ? -UNITY_MATRIX_V[2].xyz : positionWS - _WorldSpaceCameraPos;
            if (abs(ray.z) < 1e-4) return depth;
            float t = (_BaseZ - positionWS.z) / ray.z;
            float3 g = positionWS + ray * t - normalize(ray) * 0.05;
            float4 cs = TransformWorldToHClip(g);
            float gd = cs.z / cs.w;
            #if UNITY_REVERSED_Z
            return max(depth, gd);
            #else
            return min(depth, gd);
            #endif
        }

        float EnvDepth(float3 positionWS, float alpha, float4 positionCS)
        {
            float3 p = positionWS;
            if (_WallMode > 0.5)
            {
                p.z = _BaseZ;   // a wall sorts by its foot
            }
            else
            {
                clip(alpha - 0.5 / 255.0);
                float d = ((alpha * 255.0 - 1.0) / 254.0) * 2.0 * _DepthRange - _DepthRange;
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                p += forward * d;
            }
            TWF_RevealClip(positionWS, p, positionCS);
            float4 cs = TransformWorldToHClip(p);
            return KeepAboveGround(positionWS, cs.z / cs.w);
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float fog : TEXCOORD2; float3 normalWS : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i, out float depth : SV_Depth) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                depth = EnvDepth(i.positionWS, tex.a, i.positionCS);

                // Light as the ground is lit, so the two stay one picture; walls use their own normals.
                float3 up = float3(0, 0, -1);
                float3 n = _WallMode > 0.5 ? normalize(i.normalWS) : up;
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = _WallMode > 0.5 ? mainLight.shadowAttenuation : 1;
                half ndl = saturate(dot(n, mainLight.direction) * 0.5 + 0.5);
                half3 light = mainLight.color * ndl * shadow + _TWF_Ambient.rgb;

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

                half3 color = tex.rgb * i.color.rgb * light;
                // Windows and lamp heads light up as the day goes.
                half glow = SAMPLE_TEXTURE2D(_GlowTex, sampler_GlowTex, i.uv).r * (1.0 - _TWF_Day);
                color += tex.rgb * glow * _GlowStrength;
                return half4(MixFog(color, i.fog), 1);
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

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float frag(Varyings i, out float depth : SV_Depth) : SV_Target
            {
                depth = EnvDepth(i.positionWS, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a, i.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }
}
