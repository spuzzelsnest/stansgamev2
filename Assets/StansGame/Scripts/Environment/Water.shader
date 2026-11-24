// Stan's Game water (the Caen canal, the Orne, ponds): two layers of ripples drifting with the current, the surface
// mirroring the world above it (PlanarReflection.cs renders the reflection at the water height; without it the
// reflection probe / sky colour), a Fresnel blend from the dark water when looking down to the mirror at a glancing
// angle, and the glitter of the moon or the sun on the ripples. Materials: Water_Canal, Water_Orne, Water_Pond
// (PegasusLevelBuilder.Ground.cs WaterShaderMaterial).
Shader "StansGame/Water"
{
    Properties
    {
        _Color ("Water (looking down)", Color) = (0.028, 0.05, 0.046, 1)
        _SkyColor ("Sky reflected (by day; scaled by the main light)", Color) = (0.30, 0.36, 0.48, 1)
        [Normal][NoScaleOffset] _NormalMap ("Ripples (normal map)", 2D) = "bump" {}
        _NormalScale ("Ripple strength", Float) = 0.5
        _Tiling ("Ripple tiling (per metre)", Float) = 0.2
        _Flow ("Current (uv per second)", Vector) = (0.003, 0.01, 0, 0)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.94
        _ReflStrength ("Reflection strength", Range(0, 1)) = 0.9
        _Distortion ("Reflection wobble", Float) = 0.025
        _Glitter ("Moon / sun glitter", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Color, _SkyColor, _Flow;
            float _NormalScale, _Tiling, _Smoothness, _ReflStrength, _Distortion, _Glitter;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_PlanarReflectionTex); SAMPLER(sampler_PlanarReflectionTex);
            float _PlanarReflectionHeight;
            float _PlanarReflectionOn;
            float _StansRain;   // Weather.cs: rain on the water roughens it

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  fog        : TEXCOORD2;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.positionWS.xz * _Tiling;
                // two ripple layers crossing each other, the second finer and drifting the other way
                float3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv + _Flow.xy * t), _NormalScale);
                float3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 1.73 + float2(0.37, 0.11) - _Flow.yx * t * 1.3), _NormalScale * 0.7);
                float3 nt = normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
                // rain: a fine, fast-changing third layer of ripples
                float3 n3 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 7.3 + float2(t * 0.9, -t * 0.7)), _NormalScale * 1.6 * _StansRain);
                nt = normalize(float3(nt.xy + n3.xy, nt.z));
                float3 N = normalize(float3(nt.x, nt.z, nt.y));     // the surface lies flat: tangent x -> world x, y -> z

                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float NdotV = saturate(dot(N, V));
                float fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float lightLevel = saturate(dot(L.color, float3(0.3, 0.59, 0.11)));

                // what the surface mirrors: the world (planar reflection) where there is one at this height, else the sky
                float3 R = reflect(-V, N);
                float3 sky = GlossyEnvironmentReflection(R, i.positionWS, 1.0 - _Smoothness, 1.0) + _SkyColor.rgb * lightLevel;
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float3 planar = SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, suv + N.xz * _Distortion).rgb;
                float w = _PlanarReflectionOn * saturate(1.0 - abs(i.positionWS.y - _PlanarReflectionHeight) * 2.0);
                float3 refl = lerp(sky, planar, w);

                // the water itself, lit a little by the sky and the main light
                float3 body = _Color.rgb * (SampleSH(N) + L.color * saturate(L.direction.y) * 0.35);

                // the moon's or the sun's path on the ripples
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(N, H)), 700.0 * _Smoothness) * _Glitter;
                float3 glitter = spec * L.color * L.shadowAttenuation;

                float3 col = lerp(body, refl, saturate(fresnel * _ReflStrength + 0.08 * w)) + glitter;
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert (Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half frag (Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert (Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag (Varyings i) : SV_Target { return half4(0.0, 1.0, 0.0, 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
