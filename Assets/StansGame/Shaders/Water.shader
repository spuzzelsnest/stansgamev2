// Stan's Game water: the Caen canal (dark, almost still) and the tidal Orne (muddy, a slight current).
// Two slowly drifting ripple layers in world space, sky/moon reflection with fresnel, moonlight glints.
Shader "StansGame/Water"
{
    Properties
    {
        _Color ("Water colour", Color) = (0.035, 0.06, 0.065, 1)
        _SkyColor ("Reflection tint", Color) = (0.30, 0.36, 0.48, 1)
        _NormalMap ("Ripples", 2D) = "bump" {}
        _NormalScale ("Ripple strength", Range(0, 2)) = 0.55
        _Tiling ("Ripple scale (per metre)", Float) = 0.22
        _Flow ("Flow (m/s, xz)", Vector) = (0.004, 0.012, 0, 0)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.93
        _Fresnel ("Fresnel power", Range(1, 8)) = 4.5
        _Glint ("Moon glint", Range(0, 10)) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _SkyColor;
                float4 _NormalMap_ST;
                half _NormalScale;
                float _Tiling;
                float4 _Flow;
                half _Smoothness;
                half _Fresnel;
                half _Glint;
            CBUFFER_END
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.positionWS.xz * _Tiling;
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv + _Flow.xy * t * _Tiling), _NormalScale);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 1.73 + float2(-_Flow.y, _Flow.x) * t * _Tiling * 1.6 + 0.37), _NormalScale * 0.7);
                half3 nt = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
                half3 N = normalize(half3(nt.x, nt.z, nt.y));            // flat surface: tangent x,y -> world x,z

                half3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = pow(1.0h - saturate(dot(N, V)), _Fresnel);
                half3 R = reflect(-V, N);
                half3 env = GlossyEnvironmentReflection(R, PerceptualSmoothnessToPerceptualRoughness(_Smoothness), 1.0h);

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(N, L.direction));
                half3 H = normalize(L.direction + V);
                half spec = pow(saturate(dot(N, H)), exp2(10.0h * _Smoothness + 1.0h)) * _Glint;

                half3 body = _Color.rgb * (SampleSH(N) + L.color * (0.25h + 0.5h * ndl) * L.shadowAttenuation);
                half3 refl = env * _SkyColor.rgb * 1.6h + _SkyColor.rgb * 0.22h;
                half3 col = lerp(body, refl, saturate(0.04h + fres * 0.9h));
                col += L.color * spec * L.shadowAttenuation;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert (float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half frag () : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
