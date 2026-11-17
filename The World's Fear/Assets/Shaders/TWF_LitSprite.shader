// HD-2D sprite shader for URP: pixel-art sprites that receive light, cast and receive
// shadows, glow (emission) and optionally sway in the wind.
// The world is Y-up: the ground is the XZ plane.
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
        _NoReveal ("Never fades in front of the hero (stairs)", Float) = 0
        _MeshNormals ("Use Mesh Normals (cliffs)", Float) = 0
        _NormalMap ("Normal Map (pixel art)", 2D) = "bump" {}
        _UseNormalMap ("Use Normal Map", Float) = 0
        _Bands ("Light Bands", Range(2,8)) = 4
        _ShadowTint ("Shadow Tint", Color) = (0.42,0.36,0.62,1)
        _Relief ("Relief (height from the texture's own light and dark)", Range(0,4)) = 0
        _PathMask ("Rounded path edges (TEXCOORD1: tile uv, cut corners, filled corners)", Float) = 0
        _Frost ("Frost (a creature of the snow line: drawn in ice blues)", Range(0,1)) = 0
        _Terrain ("Terrain (slopes and what lies on them: shaded by the way each face leans)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/TWF_Reveal.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_NormalMap);
        SAMPLER(sampler_NormalMap);

        // Set by ZoneLighting for the whole scene.
        half4 _TWF_Ambient;
        // Scales every material's relief: 0 as authored, -1 off (the debug scene's slider).
        half _TWF_ReliefBoost;

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cutoff;
            half _Emission;
            half _Wind;
            half _Ambient;
            half _FlatGround;
            half _NoReveal;
            half _MeshNormals;
            half _UseNormalMap;
            half _Bands;
            half4 _ShadowTint;
            half _Relief;
            half _Frost;
            float4 _MainTex_TexelSize;
            half _PathMask;
            half _Terrain;
        CBUFFER_END

        // Rounded path edges, like the ponds: m.xy is the spot in its tile (0..1), m.z the corners cut to a quarter
        // circle (grass showing), m.w the corners filled outside a quarter circle (a path bend sweeping over a grass
        // tile's corner). Corner bits: 1 lower left, 2 lower right, 4 upper left, 8 upper right. On the pixel grid,
        // with a little wobble so the edge reads as worn ground, not a drawn circle.
        float PathHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float PathNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(PathHash(i), PathHash(i + float2(1, 0)), f.x), lerp(PathHash(i + float2(0, 1)), PathHash(i + 1), f.x), f.y);
        }

        // Worn ground (paths, shores) as an organic patch: wear holds how much of the ground round each corner of the
        // tile is worn (x lower left, y lower right, z upper right, w upper left), local the spot in the tile. The blend
        // of the four, wobbled by noise, is the path where it passes a half; a darker rim of trodden grass and dirt
        // runs along the edge. On the pixel grid (16 per tile), like the art. Returns how far inside the edge (0..1).
        float PathEdge(float4 wear, float2 local, float3 positionWS)
        {
            float2 f = (floor(saturate(local) * 16.0) + 0.5) / 16.0;
            float v = lerp(lerp(wear.x, wear.y, f.x), lerp(wear.w, wear.z, f.x), f.y);
            float2 g = floor(positionWS.xz * 16.0) / 16.0;
            // Each kind of blend (paths 1, ground of another biome 2) wobbles to its own noise, so their edges differ.
            float seed = _PathMask * 17.3;
            v += (PathNoise(g * 2.3 + seed) - 0.5) * 0.3;
            // A soft band, not a line: across it the dirt thins out pixel by pixel into the grass, the chance of a
            // dirt pixel rising from none at its outer side to all at its inner side.
            float t = saturate((v - 0.32) / 0.36);
            if (PathHash(floor(positionWS.xz * 16.0) + 0.37 + seed) > t * t * (3 - 2 * t)) discard;
            return t;
        }

        // The texture as a heightmap: bright stones stand up, dark mortar and gaps sink.
        float TexHeight(float2 uv)
        {
            half4 c = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0);
            return dot(c.rgb, half3(0.3, 0.59, 0.11)) * c.a;
        }

        // The surface normal of one texel of the heightmap, in the world: one flat facet per texel, so the relief
        // stays crisp pixel art. The tangent frame comes from screen derivatives, so any mesh or tile works.
        float3 ReliefNormal(float3 n, float3 positionWS, float2 uv)
        {
            float2 ts = _MainTex_TexelSize.xy;
            float2 c = (floor(uv / ts) + 0.5) * ts;
            float hl = TexHeight(c - float2(ts.x, 0)), hr = TexHeight(c + float2(ts.x, 0));
            float hd = TexHeight(c - float2(0, ts.y)), hu = TexHeight(c + float2(0, ts.y));
            float3 dp1 = ddx(positionWS), dp2 = ddy(positionWS);
            float2 duv1 = ddx(uv), duv2 = ddy(uv);
            float3 p2 = cross(dp2, n), p1 = cross(n, dp1);
            float3 t = p2 * duv1.x + p1 * duv2.x;
            float3 b = p2 * duv1.y + p1 * duv2.y;
            float inv = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-12));
            t *= inv; b *= inv;
            return normalize(n - _Relief * (1 + _TWF_ReliefBoost) * (t * (hr - hl) + b * (hu - hd)));
        }
        

        // Trees, bushes and 3D walls dither away in front of a hero; the ground and units never do.
        bool RevealsHero() { return _NoReveal < 0.5 && _FlatGround < 0.5 && (_Wind > 0.001 || _MeshNormals > 0.5); }

        // Sway the top of the sprite; the base (uv.y = 0) stays put.
        float3 ApplyWind(float3 positionWS, float2 uv)
        {
            float phase = _Time.y * 1.7 + positionWS.x * 0.35 + positionWS.z * 0.27;
            float sway = (sin(phase) * 0.6 + sin(phase * 2.3) * 0.25) * _Wind * uv.y * uv.y;
            positionWS.x += sway * 0.25;
            positionWS.z += sway * 0.12;
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
                float4 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 pathMask : TEXCOORD7;
                float2 pathLocal : TEXCOORD8;
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
                o.pathMask = v.uv1;
                o.pathLocal = v.uv2;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                clip(tex.a - _Cutoff);
                if (_Frost > 0)
                {
                    half lum = dot(tex.rgb, half3(0.3, 0.59, 0.11));
                    tex.rgb = lerp(tex.rgb, saturate(half3(0.50, 0.74, 1.0) * (lum * 1.15 + 0.06)), _Frost);
                }

                if (_PathMask > 0.5)
                {
                    float inside = PathEdge(i.pathMask, i.pathLocal, i.positionWS);
                    // the outer band: darker, trodden grass over the dirt
                    // Near the grass the dirt is mixed with it: greener and darker, sprinkled through the grass.
                    if (_PathMask < 1.5)
                        tex.rgb = lerp(lerp(tex.rgb, half3(0.27, 0.42, 0.13), 0.55), tex.rgb, smoothstep(0.15, 0.85, inside));
                }
                if (RevealsHero()) TWF_RevealClip(i.positionWS, i.positionWS, i.positionCS);

                // Sprites are lit as if they face the sky, so the whole sprite gets even light
                // no matter which way the billboard turns. Shadows still fall per pixel.
                float3 up = float3(0, 1, 0);
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
                half terrainShade = 1;
                if (_Terrain > 0.5)
                {
                    // Slopes read as height: each face is lit by the way it leans, against how flat ground is lit,
                    // in a few hard steps like hand-shaded pixel art. The light leans a little from the north-west
                    // even at noon (as on a map's hill shading), so a slope never washes out to the flat ground's tone.
                    float3 fn = normalize(i.normalWS);
                    if (fn.y < 0) fn = -fn;
                    float3 L = normalize(mainLight.direction + float3(-0.45, 0, 0.45) * saturate(mainLight.direction.y));
                    half flatK = saturate(dot(up, L)) + 0.3;
                    half k = clamp((saturate(dot(fn, L)) + 0.3) / flatK, 0.62, 1.22);
                    // Steep faces see less of the sky, too.
                    k *= lerp(0.86, 1.0, fn.y * fn.y);
                    // Nearly level ground stays as it is: only a real lean is shaded, so gentle swells don't stripe.
                    k = lerp(1, k, smoothstep(0.02, 0.12, 1 - fn.y));
                    terrainShade = floor(k * 10 + 0.5) / 10;
                    n = up;
                    ndl = saturate(dot(n, mainLight.direction) * 0.5 + 0.5);
                }
                // Relief: each texel lit by the slope of its own heightmap, against the flat surface's light,
                // in a few steps like hand-shaded pixel art.
                if (_Relief * (1 + _TWF_ReliefBoost) > 0.001)
                {
                    float3 pn = ReliefNormal(n, i.positionWS, i.uv);
                    half rel = clamp(1 + (dot(pn, mainLight.direction) - dot(n, mainLight.direction)) * 1.6, 0.55, 1.45);
                    ndl *= floor(rel * 6 + 0.5) / 6;
                }
                half3 light = mainLight.color * ndl * mainLight.shadowAttenuation;

                half3 ambient = _TWF_Ambient.rgb * _Ambient;
                light *= terrainShade;
                ambient *= lerp(1, terrainShade, 0.75);

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    light += l.color * min(l.distanceAttenuation, 0.6);   // point blank must not blow a sprite out
                LIGHT_LOOP_END
                #else
                for (uint li = 0; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    light += l.color * min(l.distanceAttenuation, 0.6);   // point blank must not blow a sprite out
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
                float3 normalWS = float3(0, 1, 0);
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
                float4 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 pathMask : TEXCOORD3;
                float2 pathLocal : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.uv);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alpha = v.color.a * _Color.a;
                o.pathMask = v.uv1;
                o.pathLocal = v.uv2;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.alpha - _Cutoff);
                // The same cut as the colour pass: depth priming draws only where the depth pass wrote.
                if (_PathMask > 0.5) PathEdge(i.pathMask, i.pathLocal, i.positionWS);
                if (RevealsHero()) TWF_RevealClip(i.positionWS, i.positionWS, i.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }
}
